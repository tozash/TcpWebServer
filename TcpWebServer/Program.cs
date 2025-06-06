using System.Net;
using System.Net.Sockets;
using System.Text;

const int Port = 8080;
const string WebRoot = "webroot";      // relative to executable

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
Console.WriteLine($"[+] Listening on http://localhost:{Port}/  (Ctrl+C to quit)");

while (true)
{
    using var client = await listener.AcceptTcpClientAsync();
    Console.WriteLine("[*] Client connected");

    using var networkStream = client.GetStream();

    // Read up to 4096 bytes (enough for a simple GET line + headers).
    // We'll upgrade this to a proper loop when we parse HTTP.
    var buffer = new byte[4096];
    int bytesRead = await networkStream.ReadAsync(buffer);

    var requestText = Encoding.UTF8.GetString(buffer.AsSpan(0, bytesRead));
    Console.WriteLine("===== Raw request =====");
    Console.WriteLine(requestText);
    Console.WriteLine("========================");

    // Always respond with a hard-coded 200 + index.html for now
    var responseBody = File.ReadAllText(Path.Combine(WebRoot, "index.html"));
    var responseBytes = Encoding.UTF8.GetBytes(
        $"HTTP/1.1 200 OK\r\n" +
        $"Content-Type: text/html\r\n" +
        $"Content-Length: {responseBody.Length}\r\n" +
        $"\r\n" +
        responseBody);

    await networkStream.WriteAsync(responseBytes);
    Console.WriteLine("[+] Response sent, closing");
}
