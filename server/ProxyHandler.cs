using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Serilog;

public static class ProxyHandler
{
    public static async Task HandleHttpsTunnelAsync(
        TcpClient client, string target, List<string> headers, string token)
    {
        var split = target.Split(':');
        var host = split[0];
        var port = split.Length > 1 ? int.Parse(split[1]) : 443;

        var node = UpstreamManager.GetResidentialNodes("RoundRobin");
        if (node == null)
        {
            var writer = new StreamWriter(client.GetStream(), Encoding.ASCII) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Bad Gateway\r\n\r\nNo available node");
            return;
        }

        try
        {
            Log.Information("🔁 Connecting to tunnel node {Host}:{Port}", node.Host, node.Port);

            using var nodeSocket = new TcpClient();
            await nodeSocket.ConnectAsync(node.Host, 8081);
            var nodeStream = nodeSocket.GetStream();

            // Send CONNECT method line directly
            var connectLine = $"CONNECT {host}:{port} HTTP/1.1\r\n";
            var headerBlock = string.Join("\r\n", headers) + "\r\n\r\n";
            var connectBytes = Encoding.ASCII.GetBytes(connectLine + headerBlock);
            await nodeStream.WriteAsync(connectBytes, 0, connectBytes.Length);

            // Read 200 Connection Established response
            var reader = new StreamReader(nodeStream, Encoding.ASCII);
            string line = await reader.ReadLineAsync();
            if (!line.Contains("200")) throw new Exception("Tunnel not established by node");

            var clientStream = client.GetStream();
            var clientWriter = new StreamWriter(clientStream, Encoding.ASCII) { AutoFlush = true };
            await clientWriter.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n");

            var c2n = ProxyDataTransfer.ProxyData(clientStream, nodeStream);
            var n2c = ProxyDataTransfer.ProxyData(nodeStream, clientStream);

            await Task.WhenAny(c2n, n2c);
            Log.Information("✅ Tunnel closed");
        }
        catch (Exception ex)
        {
            Log.Error("❌ Tunnel setup failed: {Error}", ex.Message);
            var writer = new StreamWriter(client.GetStream(), Encoding.ASCII) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 500 Internal Server Error\r\n\r\nTunnel failure");
        }
    }

    public static async Task HandleHttpForwardAsync(
        TcpClient client,
        string requestLine,
        StreamReader reader,
        NetworkStream clientStream,
        List<string> headers,
        string token)
    {
        var node = UpstreamManager.GetResidentialNodes("RoundRobin");
        if (node == null)
        {
            var writer = new StreamWriter(clientStream) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Bad Gateway\r\n\r\nNo nodes available");
            return;
        }

        try
        {
            Log.Information("🔁 Connecting to tunnel node {Host}:{Port}", node.Host, node.Port);

            using var nodeSocket = new TcpClient();
            await nodeSocket.ConnectAsync(node.Host, 8081);
            var nodeStream = nodeSocket.GetStream();

            // Forward raw HTTP request
            var firstLineBytes = Encoding.ASCII.GetBytes(requestLine + "\r\n");
            await nodeStream.WriteAsync(firstLineBytes, 0, firstLineBytes.Length);

            foreach (var header in headers)
            {
                var headerLine = Encoding.ASCII.GetBytes(header + "\r\n");
                await nodeStream.WriteAsync(headerLine, 0, headerLine.Length);
            }

            await nodeStream.WriteAsync(Encoding.ASCII.GetBytes("\r\n")); // End of headers

            // Body (if any)
            var remaining = await reader.ReadToEndAsync();
            if (!string.IsNullOrEmpty(remaining))
            {
                var bodyBytes = Encoding.UTF8.GetBytes(remaining);
                await nodeStream.WriteAsync(bodyBytes);
            }

            var c2n = ProxyDataTransfer.ProxyData(clientStream, nodeStream);
            var n2c = ProxyDataTransfer.ProxyData(nodeStream, clientStream);

            await Task.WhenAny(c2n, n2c);
        }
        catch (Exception ex)
        {
            var writer = new StreamWriter(clientStream) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Proxy Error\r\n\r\n" + ex.Message);
        }
    }
}