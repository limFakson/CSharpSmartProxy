using Serilog;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Text.Json;
using System.Text;

// Load .env
DotNetEnv.Env.Load("../../../.env");

// Initialize Logger
Logger.Initialize();

// Load Config
var config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build();

await Main();

async Task Main()
{
    await DBLoader.DBLoad();

    var proxySettings = config.GetSection("Proxy").Get<ProxySettings>();
    var limitSettings = config.GetSection("Proxy:Limits").Get<LimitSettings>();
    var upstreamTargets = proxySettings.UpstreamTargets;
    NodeHealthManager.StartNodeStaleChecker();

    // Load Tokens from fetcher
    HashSet<string> validPSQLTokens = await TokenFetcher.GetValidTokensAsync();
    _ = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            validPSQLTokens = await TokenFetcher.GetValidTokensAsync();
        }
    });
    Log.Information("Loaded {Count} tokens from PostgreSQL", validPSQLTokens.Count);
    Log.Debug("Token loaded: {Token}", validPSQLTokens);

    var rrCounter = 0;
    UpstreamTarget GetNextTarget()
    {
        return proxySettings.RoutingStrategy switch
        {
            "Random" => upstreamTargets[new Random().Next(upstreamTargets.Count)],
            "RoundRobin" => upstreamTargets[rrCounter++ % upstreamTargets.Count],
            _ => upstreamTargets[0] // fallback
        };
    }

    var apiApp = ProxyApi.CreateApiApp(args);

    var listenPort = proxySettings.ListenPort;
    var validTokens = proxySettings.ValidTokens;

    // Start TCP listener for proxy connections
    var listener = new TcpListener(IPAddress.Any, listenPort);
    listener.Start();

    // Start Web API and WebSocket server
    var builder = WebApplication.CreateBuilder();

    // app.UseWebSockets();
    // app.Map("/ws/node", WebSocketServer.HandleWebSocket);

    _ = apiApp.RunAsync("http://0.0.0.0:9000"); // Run API/WebSocket server in the background
    // _ = Task.Run(WebSocketServer.PingMonitor); // Start ping monitor in the background

    Log.Information("═══════════════════════════════════════");
    Log.Information("🚀 Proxy Server Started on port {Port}", listenPort);
    Log.Information("═══════════════════════════════════════");

    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();
        _ = HandleConnectionAsync(client);
    }

    async Task HandleConnectionAsync(TcpClient inbound)
    {
        using var stream = inbound.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true };

        string? requestLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(requestLine)) return;

        var headers = new List<string>();
        string? line;
        while (!string.IsNullOrWhiteSpace(line = await reader.ReadLineAsync()))
        {
            headers.Add(line);
        }

        Log.Information("Received request: {Headers}", requestLine);
        string? hostHeader = headers.FirstOrDefault(h => h.StartsWith("Host:"))?.Split(":", 2)[1].Trim();
        string? token = null;

        foreach (var header in headers)
        {
            if (header.StartsWith("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
            {
                // Split by the first colon to get the key/value
                var keyValueParts = header.Split(new[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (keyValueParts.Length != 2)
                {
                    Log.Warning("⚠️ Invalid Proxy-Authorization header format.");
                    continue;
                }

                var authSchemeAndValue = keyValueParts[1].Trim(); // "Basic VEVTVC1UT0tFTi0xMjM6"
                if (!authSchemeAndValue.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("⚠️ Unsupported auth scheme: {Scheme}", authSchemeAndValue);
                    continue;
                }

                // Extract base64 part
                var base64 = authSchemeAndValue.Substring("Basic".Length).Trim();
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                    Log.Information("🔓 Decoded Proxy Auth");

                    token = decoded.Split(':')[0];  // e.g., "TEST-TOKEN-123"
                    Log.Information("✅ Extracted token from proxy URL");
                }
                catch (Exception ex)
                {
                    Log.Error("❌ Failed to decode Proxy-Authorization: {Error}", ex.Message);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(hostHeader))
        {
            await writer.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\nMissing Token or Host");
            return;
        }

        // Check token in DB (implement actual validation as needed)
        if (!validPSQLTokens.Contains(token))
        {
            await writer.WriteAsync("HTTP/1.1 403 Forbidden\r\n\r\nInvalid Token");
            return;
        }

        var parts = requestLine.Split(' ');
        if (parts.Length < 2) return;

        var method = parts[0];
        var target = parts[1];

        if (method.ToUpper() == "CONNECT")
        {
            Console.WriteLine("Https tunnel");
            await ProxyHandler.HandleHttpsTunnelAsync(inbound, target, headers, token);
        }
        else
        {
            Console.WriteLine("Http turnnel");
            await ProxyHandler.HandleHttpForwardAsync(inbound, requestLine, reader, stream, headers, token);
        }
    }
}