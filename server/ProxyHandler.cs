using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;

public static class ProxyHandler
{
    public static async Task HandleHttpsTunnelAsync(
        TcpClient client, string target, List<string> headers, string token)
    {
        var split = target.Split(':');
        var host = split[0];
        var port = split.Length > 1 ? int.Parse(split[1]) : 443;

        using var server = new TcpClient();
        await server.ConnectAsync(host, port);

        var clientStream = client.GetStream();
        var serverStream = server.GetStream();

        var writer = new StreamWriter(clientStream, Encoding.ASCII) { AutoFlush = true };
        await writer.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n");

        var clientToServer = ProxyDataTransfer.ProxyData(clientStream, serverStream);
        var serverToClient = ProxyDataTransfer.ProxyData(serverStream, clientStream);

        await Task.WhenAny(clientToServer, serverToClient);
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

        // Serialize payload to forward to node
        var payload = new
        {
            method = requestLine.Split(' ')[0],
            url = requestLine.Split(' ')[1],
            headers = headers.ToDictionary(
                h => h.Split(":", 2)[0].Trim(),
                h => h.Split(":", 2)[1].Trim())
        };

        using var httpClient = new HttpClient();
        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"));

        try
        {
            var response = await httpClient.PostAsync($"http://{node.Host}:{node.Port}/proxy-request", content);
            var bodyBytes = await response.Content.ReadAsByteArrayAsync();
            var clientWriter = new StreamWriter(clientStream) { AutoFlush = true };

            await clientWriter.WriteAsync($"HTTP/1.1 {response.StatusCode}\r\n\r\n");
            await clientStream.WriteAsync(bodyBytes);
        }
        catch (Exception ex)
        {
            var writer = new StreamWriter(clientStream) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Proxy Error\r\n\r\n" + ex.Message);
        }
    }
}