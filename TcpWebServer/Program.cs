using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const int Port = 8080;
const string WebRoot = "webroot";
const int BufferSize = 8 * 1024;              // 8 KB
const string LogFile = "requests.log";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    Console.WriteLine("\n[!] Ctrl+C pressed – shutting down…");
    e.Cancel = true;          // prevent immediate process kill
    cts.Cancel();
};

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
Console.WriteLine($"[+] Listening on http://localhost:{Port}/  (Ctrl+C to stop)");

try
{
    // ── Accept loop ────────────────────────────────────────────────────────────
    while (!cts.IsCancellationRequested)
    {
        if (!listener.Pending())
        {
            await Task.Delay(100, cts.Token);    // poll every 100 ms
            continue;
        }

        TcpClient client = await listener.AcceptTcpClientAsync(cts.Token);
        _ = Task.Run(() => HandleClientAsync(client, cts.Token), cts.Token);
    }
}
catch (OperationCanceledException) { /* expected on shutdown */ }
finally
{
    listener.Stop();
    Console.WriteLine("[+] Listener closed. Bye!");
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  Local functions
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

async Task HandleClientAsync(TcpClient client, CancellationToken token)
{
    using var tcp = client;
    using var stream = tcp.GetStream();

    // 1️⃣  Read request headers (max 8 KB)
    var buffer = new byte[BufferSize];
    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
    if (bytesRead == 0) return;

    string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
    string[] lines = requestText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    string requestLine = lines[0];
    string[] parts = requestLine.Split(' ');

    if (parts.Length != 3)
    {
        await SendErrorAsync(stream, 400, "Bad Request", token);
        return;
    }

    string method = parts[0].ToUpperInvariant();   // GET / POST / …
    string urlPath = parts[1];                      // /index.html
    string version = parts[2];                      // HTTP/1.1

    // ── Log the request ───────────────────────────────────────────
    LogRequest(new
    {
        Time = DateTime.UtcNow,
        Client = tcp.Client.RemoteEndPoint?.ToString(),
        Method = method,
        Path = urlPath,
        Vers = version
    });

    // 2️⃣  Only GET is allowed
    if (method != "GET")
    {
        await SendErrorAsync(stream, 405, "Method Not Allowed", token);
        return;
    }

    // 3️⃣  Normalise & validate path
    if (urlPath == "/") urlPath = "/index.html";
    urlPath = Uri.UnescapeDataString(urlPath);
    if (urlPath.Contains("..", StringComparison.Ordinal))
    {
        await SendErrorAsync(stream, 403, "Forbidden", token);
        return;
    }

    // 4️⃣  Determine content-type (now allowing no-extension → HTML)
    string ext = Path.GetExtension(urlPath).ToLowerInvariant();

    string? contentType = ext switch
    {
        "" => "text/html",              // <── assume HTML when no extension
        ".html" => "text/html",
        ".css" => "text/css",
        ".js" => "application/javascript",
        _ => null                      // unknown/blocked extension
    };

    if (contentType is null)
    {
        await SendErrorAsync(stream, 403, "Forbidden", token);
        return;
    }

    // 5️⃣  Locate file
    string fullPath = Path.Combine(WebRoot, urlPath.TrimStart('/'));
    if (!File.Exists(fullPath))
    {
        await SendErrorAsync(stream, 404, "Not Found", token);
        return;
    }

    // 6️⃣  Serve file
    byte[] bodyBytes = await File.ReadAllBytesAsync(fullPath, token);
    await SendHeadersAsync(stream, 200, "OK", contentType, bodyBytes.Length, token);
    await stream.WriteAsync(bodyBytes, token);
}


static async Task SendHeadersAsync(NetworkStream stream,
                                   int status,
                                   string reason,
                                   string contentType,
                                   int contentLength,
                                   CancellationToken token)
{
    string h =
        $"HTTP/1.1 {status} {reason}\r\n" +
        $"Content-Type: {contentType}\r\n" +
        $"Content-Length: {contentLength}\r\n" +
        $"Connection: close\r\n\r\n";
    await stream.WriteAsync(Encoding.UTF8.GetBytes(h), token);
}

static async Task SendErrorAsync(NetworkStream stream,
                                 int status,
                                 string reason,
                                 CancellationToken token)
{
    string body = TryLoadCustomErrorPage(status) ??
                  $"<html><head><title>{status} {reason}</title></head>" +
                  $"<body><h1>Error {status}: {reason}</h1></body></html>";

    byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
    await SendHeadersAsync(stream, status, reason, "text/html", bodyBytes.Length, token);
    await stream.WriteAsync(bodyBytes, token);
}

static string? TryLoadCustomErrorPage(int statusCode)
{
    string customPath = Path.Combine(WebRoot, "error.html");
    if (!File.Exists(customPath)) return null;

    string html = File.ReadAllText(customPath);
    // Optionally replace a placeholder like {{status}} in the custom page
    html = html.Replace("{{status}}", statusCode.ToString());
    return html;
}

static void LogRequest(object record)
{
    try
    {
        string line = JsonSerializer.Serialize(record);
        File.AppendAllText(LogFile, line + Environment.NewLine);
    }
    catch
    {
        // swallow logging errors, don't crash the server
    }
}
