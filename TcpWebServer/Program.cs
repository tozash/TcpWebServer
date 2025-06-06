using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

const int Port = 8080;
const string WebRoot = "webroot";
const int BufferSize = 8192;         // 8 KB is plenty for headers

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
Console.WriteLine($"[+] Listening on http://localhost:{Port}/  (Ctrl+C to quit)");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    // 🔹 spin the connection off to a background task so that the loop can
    //    accept the next client immediately
    _ = Task.Run(() => HandleClientAsync(client));
}

async Task HandleClientAsync(TcpClient client)
{
    using var tcp = client;
    using var stream = tcp.GetStream();

    // 1️⃣  Read headers (max 8 KB)
    var buffer = new byte[BufferSize];
    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
    if (bytesRead == 0) return;

    string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
    string[] lines = requestText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    string requestLine = lines[0];
    string[] parts = requestLine.Split(' ');

    if (parts.Length != 3)
    {
        await SendSimpleError(stream, 400, "Bad Request", "<h1>400 Bad Request</h1>");
        return;
    }

    string method = parts[0].ToUpperInvariant();
    string urlPath = parts[1];               // e.g.  /index.html
    // string version = parts[2];           // HTTP/1.1 (unused)

    // 2️⃣  Only GET is allowed
    if (method != "GET")
    {
        await SendSimpleError(stream, 405, "Method Not Allowed",
            "<h1>Error 405: Method Not Allowed</h1>");
        return;
    }

    // 3️⃣  Normalise path & security checks
    if (urlPath == "/") urlPath = "/index.html";
    urlPath = Uri.UnescapeDataString(urlPath);          // decode %20 etc.

    if (urlPath.Contains("..", StringComparison.Ordinal))
    {
        await SendSimpleError(stream, 403, "Forbidden", "<h1>Error 403: Forbidden</h1>");
        return;
    }

    // 4️⃣  Extension whitelist
    string ext = Path.GetExtension(urlPath).ToLowerInvariant();
    string? contentType = ext switch
    {
        ".html" => "text/html",
        ".css" => "text/css",
        ".js" => "application/javascript",
        _ => null
    };

    if (contentType is null)
    {
        await SendSimpleError(stream, 403, "Forbidden", "<h1>Error 403: Forbidden</h1>");
        return;
    }

    // 5️⃣  Locate file inside webroot
    string fullPath = Path.Combine(WebRoot, urlPath.TrimStart('/'));

    if (!File.Exists(fullPath))
    {
        await SendSimpleError(stream, 404, "Not Found",
            "<h1>Error 404: Page Not Found</h1>");
        return;
    }

    // 6️⃣  Send 200 OK with file contents
    byte[] bodyBytes = await File.ReadAllBytesAsync(fullPath);
    string header =
        $"HTTP/1.1 200 OK\r\n" +
        $"Content-Type: {contentType}\r\n" +
        $"Content-Length: {bodyBytes.Length}\r\n" +
        $"Connection: close\r\n" +
        $"\r\n";

    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
    await stream.WriteAsync(headerBytes);
    await stream.WriteAsync(bodyBytes);
}


static async Task SendSimpleError(NetworkStream stream,
                                  int statusCode,
                                  string reasonPhrase,
                                  string bodyHtml)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyHtml);
        string header =
            $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
            $"Content-Type: text/html\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            $"Connection: close\r\n" +
            $"\r\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(header));
        await stream.WriteAsync(bodyBytes);
    }

