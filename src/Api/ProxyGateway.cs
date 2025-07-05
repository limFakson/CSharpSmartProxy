using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

public class NodeConnection
{
    public WebSocket Socket { get; set; }
    public DateTime LastPing { get; set; } = DateTime.UtcNow;
    public string Id { get; set; }
    public SemaphoreSlim StreamLock { get; set; } = new(1, 1);
}

public class WebSocketServer
{
    private static readonly ConcurrentDictionary<string, NodeConnection> ConnectedNodes = new();

    public static async Task HandleWebSocket(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var nodeId = Guid.NewGuid().ToString();
        ConnectedNodes[nodeId] = new NodeConnection { Socket = socket, Id = nodeId };

        Console.WriteLine($"🟢 Node connected: {nodeId}");

        await ReceiveLoop(socket, nodeId);
    }

    private static async Task ReceiveLoop(WebSocket socket, string nodeId)
    {
        var buffer = new byte[8192];
        var messageBuffer = new MemoryStream();

        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var encodedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (encodedMessage == "ping")
                {
                    ConnectedNodes[nodeId].LastPing = DateTime.UtcNow;
                    Console.WriteLine($"📶 Ping from {nodeId} at {DateTime.UtcNow:HH:mm:ss}");
                }
                else
                {
                    Console.WriteLine($"📩 Text from {nodeId}: {encodedMessage}");
                }
            }
            else
            {
                messageBuffer.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    Console.WriteLine($"📩 Binary from {nodeId}: {messageBuffer.Length} bytes");
                    messageBuffer.SetLength(0);
                }
            }
        }

        ConnectedNodes.TryRemove(nodeId, out _);
        Console.WriteLine($"🔴 Node disconnected: {nodeId}");
    }

    public static async Task PingMonitor()
    {
        while (true)
        {
            foreach (var node in ConnectedNodes.Values)
            {
                if ((DateTime.UtcNow - node.LastPing).TotalSeconds > 190)
                {
                    Console.WriteLine($"❌ Node timeout: {node.Id}");
                    ConnectedNodes.TryRemove(node.Id, out _);
                }
                else
                {
                    var pingBytes = Encoding.UTF8.GetBytes("ping");
                    await node.Socket.SendAsync(new ArraySegment<byte>(pingBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(3));
        }
    }

    public static NodeConnection PickAvailableNode()
    {
        foreach (var kv in ConnectedNodes)
        {
            if (kv.Value.Socket.State == WebSocketState.Open)
                return kv.Value;
        }
        return null;
    }

    public static async Task HandleTunnel(TcpClient client, string target)
    {
        var node = PickAvailableNode();
        if (node == null)
        {
            Console.WriteLine("❌ No available node");
            using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Bad Gateway\r\n\r\nNo available node");
            return;
        }

        await node.StreamLock.WaitAsync();
        var socket = node.Socket;
        try
        {
            var targetBytes = Encoding.UTF8.GetBytes(target + "\n");
            await socket.SendAsync(new ArraySegment<byte>(targetBytes), WebSocketMessageType.Text, true, CancellationToken.None);

            var networkStream = client.GetStream();

            async Task PipeToNode()
            {
                var buffer = new byte[8192];
                while (true)
                {
                    int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;
                    await socket.SendAsync(new ArraySegment<byte>(buffer, 0, bytesRead), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
            }

            async Task PipeToClient()
            {
                var buffer = new byte[8192];
                var messageBuffer = new MemoryStream();

                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close || result.Count == 0)
                        break;

                    messageBuffer.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        var fullMessage = messageBuffer.ToArray();
                        Console.WriteLine($"➡️  From target to client: {fullMessage.Length} bytes");
                        await networkStream.WriteAsync(fullMessage, 0, fullMessage.Length);
                        await networkStream.FlushAsync();
                        messageBuffer.SetLength(0);
                    }
                }
            }

            await Task.WhenAny(PipeToNode(), PipeToClient());
        }
        finally
        {
            node.StreamLock.Release();
        }
    }
}
