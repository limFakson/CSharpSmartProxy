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
        string? token = headers.FirstOrDefault(h => h.StartsWith("X-Proxy-Token:"))?.Split(":", 2)[1].Trim();
        string? hostHeader = headers.FirstOrDefault(h => h.StartsWith("Host:"))?.Split(":", 2)[1].Trim();

        // if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(hostHeader))
        // {
        //     await writer.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\nMissing Token or Host");
        //     return;
        // }

        // // Check token in DB (implement actual validation as needed)
        // if (!validPSQLTokens.Contains(token))
        // {
        //     await writer.WriteAsync("HTTP/1.1 403 Forbidden\r\n\r\nInvalid Token");
        //     return;
        // }

        var parts = requestLine.Split(' ');
        if (parts.Length < 2) return;

        var method = parts[0];
        var target = parts[1];

        if (method.ToUpper() == "CONNECT")
            await ProxyHandler.HandleHttpsTunnelAsync(inbound, target, headers, token);
        else
            await ProxyHandler.HandleHttpForwardAsync(inbound, requestLine, reader, stream, headers, token);
    }
}