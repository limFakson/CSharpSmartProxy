using Serilog;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Migrations;
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

    _ = apiApp.RunAsync("http://localhost:9000");

    var listenPort = proxySettings.ListenPort;

    var validTokens = proxySettings.ValidTokens;

    var listener = new TcpListener(IPAddress.Any, listenPort);
    listener.Start();

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
        var nodes = UpstreamManager.GetResidentialNodes();

        var reader = new StreamReader(inbound.GetStream());
        var requestLine = await reader.ReadLineAsync();
        var headers = new List<string>();
        string? line;

        bool connected = false;
        int maxAttempts = 3;
        int attempts = 0;
        bool isSuccessful = false;

        while (!string.IsNullOrWhiteSpace(line = await reader.ReadLineAsync()))
            headers.Add(line);

        if(!headers.Contains("X-Forwarded-Host:") || !headers.Contains("X-Proxy-Token:"))
        {
            Log.Warning($"[INVALID] Invalid request from {((IPEndPoint)inbound.Client.RemoteEndPoint).Address}");
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 404 Bad Request\r\n\r\nInvalid request X-Proxy-Token or X-Forwarded-Host header not found in request.");
            inbound.Close();
            return;
        }

        string? token = headers.FirstOrDefault(h => h.StartsWith("X-Proxy-Token:"))?.Split(":", 2)[1].Trim();
        string? forwardedHost = headers.FirstOrDefault(h => h.StartsWith("X-Forwarded-Host:"))?.Split(":", 2)[1].Trim();

        if (token == null || forwardedHost == null)
        {
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\nMissing token or host header");
            inbound.Close();
            return;
        }

        if (string.IsNullOrWhiteSpace(token) || !validPSQLTokens.Contains(token))
        {
            Log.Warning($"[AUTH] Rejected connection from {((IPEndPoint)inbound.Client.RemoteEndPoint).Address}");
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 403 Forbidden\r\n\r\nUnauthorized");
            Log.Error($"Invalid or missing token, token is null - {string.IsNullOrWhiteSpace(token)} or it not in validPSQLTokens - {validPSQLTokens.Contains(token)}");
            inbound.Close();
            return;
        }

        try
        {
            Console.WriteLine("🔍 About to check if token is blocked");
            if (await TokenSessionTracker.IsTokenBlocked(token, limitSettings))
            {
                Log.Warning("Blocked token tried to connect: {Token}", token);
                var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
                await writer.WriteAsync("HTTP/1.1 429 Too Many Requests\r\n\r\nBlocked or throttled");
                inbound.Close();
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🔥 condition error in IsTokenBlocked: {ex.Message}");
        }

        if (nodes == null)
        {
            Log.Warning("No live residential node available.");
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Bad Gateway\r\n\r\nNo residential node available");
            inbound.Close();
            return;
        }

        var httpClient = new HttpClient();
        var payload = new
        {
            method = "GET",
            url = $"https://{forwardedHost}",
            headers = headers.ToDictionary(
                h => h.Split(":", 2)[0].Trim(),
                h => h.Split(":", 2)[1].Trim())
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        await TokenSessionTracker.RecordStartAsync(token!);
        foreach (var node in nodes)
        {
            while (!connected && attempts < maxAttempts)
            {
                Log.Information("🔄 Attempt {Attempt} to connect to node: {Node}", attempts + 1, node.Host);
                try
                {
                    Log.Information("🔗 Forwarding request to node: {Node}", node.Host);
                    var response = await httpClient.PostAsync($"http://{node.Host}:{node.Port}/proxy-request", content);
                    var responseBody = await response.Content.ReadAsByteArrayAsync();
                    connected = true;

                    var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
                    await writer.WriteAsync("HTTP/1.1 200 OK\r\n\r\n");
                    await inbound.GetStream().WriteAsync(responseBody);
                    isSuccessful = true;
                }
                catch (Exception ex)
                {
                    Log.Error("❌ Failed to fetch via node: {Error}", ex.Message);
                    var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
                    await writer.WriteAsync("HTTP/1.1 500 Internal Server Error\r\n\r\nProxy Node failed");
                }
                finally
                {
                    inbound.Close();
                    await TokenSessionTracker.RecordStopAsync(token!, 0, 0);
                }
            }

            if (isSuccessful)
            {
                Log.Information("✅ Successfully forwarded request back from node: {Node}", node.Host);
                break;
            }

        }

        inbound?.Dispose();
    }
}