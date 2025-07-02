using Fleck;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

public class ProxyGateway
{
    private static ConcurrentDictionary<string, IWebSocketConnection> ConnectedNodes = new();
    private static ConcurrentDictionary<string, ClientJob> ActiveJobs = new();

    public static void StartWebSocketServer(UpstreamTarget node)
    {
        var server = new WebSocketServer($"ws://{node.Host}:{node.Port}/ws/gateway/");

        server.Start(socket =>
        {
            string? token = null;

            socket.OnOpen = () => Console.WriteLine($"🔌 Node connected: {socket.ConnectionInfo.ClientIpAddress}");

            socket.OnClose = () =>
            {
                if (token != null)
                {
                    ConnectedNodes.TryRemove(token, out _);
                    Console.WriteLine($"❌ Node disconnected: {token}");
                }
            };

            socket.OnMessage = message =>
            {
                var msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);

                if (msg != null && msg.ContainsKey("type"))
                {
                    var type = msg["type"].ToString();

                    if (type == "register")
                    {
                        token = msg["token"].ToString();
                        ConnectedNodes[token] = socket;
                        Console.WriteLine($"✅ Registered node: {token}");
                    }
                    else if (type == "response")
                    {
                        // Handle response from node
                        string jobId = msg["job_id"].ToString();
                        string body = msg["body"].ToString();
                        string status = msg["status_code"].ToString();

                        Console.WriteLine($"📩 Response received for Job {jobId}: {status}");

                        // TODO: Send response back to the client from stored connection
                        if (ActiveJobs.TryRemove(jobId, out var job))
                        {
                            var writer = new StreamWriter(job.Client.GetStream()) { AutoFlush = true };
                            writer.WriteLine($"HTTP/1.1 {status}\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
                            job.Client.Close();
                        }
                    }
                }
            };
        });
    }

    public static bool SendJobToNode(string token, string jobId, string method, string url, Dictionary<string, string> headers, string body, TcpClient client)
    {
        if (ConnectedNodes.TryGetValue(token, out var socket))
        {
            var job = new
            {
                type = "request",
                job_id = jobId,
                method,
                url,
                headers,
                body
            };

            ActiveJobs[jobId] = new ClientJob
            {
                Client = client,
                JobId = jobId,
                Token = token
            };

            string json = JsonConvert.SerializeObject(job);
            socket.Send(json);
            return true;
        }
        return false;
    }

    private class ClientJob
    {
        public TcpClient Client { get; set; }
        public string JobId { get; set; }
        public string Token { get; set; }
    }
}
