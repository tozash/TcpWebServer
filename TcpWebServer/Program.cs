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
    // Dispose both the TcpClient and its NetworkStream when the method exits.
    using var tcp = client;
    using var stream = tcp.GetStream();

    // 1️⃣  Read the incoming request (headers only; no body support yet)
    var buffer = new byte[BufferSize];          // BufferSize is 8 KB constant in Program.cs
    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
    if (bytesRead == 0) return;                     // client closed connection immediately

    string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

    // 2️⃣  Parse the request line: e.g.   GET /index.html HTTP/1.1
    string[] lines = requestText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    string requestLine = lines[0];
    string[] parts = requestLine.Split(' ');

    if (parts.Length != 3)
    {
        await SendSimpleError(stream, 400, "Bad Request",
            "<h1>400 Bad Request</h1>");
        return;
    }

    string method = parts[0].ToUpperInvariant();   // GET / POST / etc.
    string urlPath = parts[1];                      // /index.html
    // string version = parts[2];                   // HTTP/1.1  (unused for now)

    // 3️⃣  Reject anything except GET
    if (method != "GET")
    {
        await SendSimpleError(stream, 405, "Method Not Allowed",
            "<h1>Error 405: Method Not Allowed</h1>");
        return;
    }

    // 4️⃣  For now, we haven’t written file‐serving logic → return stub 501
    await SendSimpleError(stream, 501, "Not Implemented",
        "<h1>501 Not Implemented (file serving stub)</h1>");
}


static async Task SendSimpleError(NetworkStream stream,
                                  int statusCode,
                                  string reasonPhrase,
                                  string bodyHtml)
{
    byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyHtml);
    string response =
        $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
        $"Content-Type: text/html\r\n" +
        $"Content-Length: {bodyBytes.Length}\r\n" +
        $"Connection: close\r\n" +
        $"\r\n";
    byte[] headerBytes = Encoding.UTF8.GetBytes(response);

    await stream.WriteAsync(headerBytes);
    await stream.WriteAsync(bodyBytes);
}
