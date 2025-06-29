using Serilog;
using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

// Load .env
DotNetEnv.Env.Load("../../../.env");

// Logger setup
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/proxy.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

// Load Config
var config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build();

await Main();

async Task Main()
{
    // Load PostgresSQL DB
    var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
    optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("POSTGRES_URL")!);

    using (var db = new PostgresDbContext(optionsBuilder.Options))
    {
        db.Database.Migrate(); // Apply pending migrations
        Log.Information("PostgresSQL DB is ready.");
    }

    bool exists = await TokenFetcher.TableExistsAsync("ProxyTokens");
    Log.Information("Checking if ProxyTokens table exists: {Exists}", exists);
    if (!exists)
    {
        Log.Error("ProxyTokens table does not exist in the database. Please run migrations.");
        return;
    }

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
        var node = UpstreamManager.GetResidentialNodes().FirstOrDefault();
        var reader = new StreamReader(inbound.GetStream());
        var requestLine = await reader.ReadLineAsync();
        string? requestedHost = null;
        int requestedPort = 80;

        if (requestLine == null || !requestLine.StartsWith("CONNECT") && !requestLine.StartsWith("GET") && !requestLine.StartsWith("HEAD") && !requestLine.StartsWith("POST"))
        {
            Log.Warning($"[INVALID] Invalid request from {((IPEndPoint)inbound.Client.RemoteEndPoint).Address}");
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\nInvalid request");
            inbound.Close();
            return;
        }

        string? token = null;
        string line;


        while (!string.IsNullOrWhiteSpace(line = await reader.ReadLineAsync()))
        {
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                var hostHeader = line.Split(':', 2)[1].Trim();

                // Check if host includes a port (e.g., host:port)
                if (hostHeader.Contains(":"))
                {
                    var parts = hostHeader.Split(':');
                    requestedHost = parts[0];
                    requestedPort = int.Parse(parts[1]);
                }
                else
                {
                    requestedHost = hostHeader;
                    // 🟡 THIS LINE IS NEEDED 👇
                    requestedPort = requestLine.StartsWith("CONNECT") ? 443 : 80;
                }
            }

            if (line.StartsWith("X-Proxy-Token:", StringComparison.OrdinalIgnoreCase))
            {
                token = line.Split(':', 2)[1].Trim();
            }
        }


        if (string.IsNullOrWhiteSpace(token) || !validPSQLTokens.Contains(token))
        {
            Log.Warning($"[AUTH] Rejected connection from {((IPEndPoint)inbound.Client.RemoteEndPoint).Address}");
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 403 Forbidden\r\n\r\nUnauthorized");
            inbound.Close();
            return;
        }

        if (TokenSessionTracker.IsTokenBlocked(token, limitSettings))
        {
            Log.Warning("Blocked token tried to connect: {Token}", token);
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 429 Too Many Requests\r\n\r\nBlocked or throttled");
            inbound.Close();
            return;
        }

        if (node == null)
        {
            Log.Warning("No live residential node available.");
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Bad Gateway\r\n\r\nNo residential node available");
            inbound.Close();
            return;
        }

        requestedHost = node.Host;
        requestedPort = node.Port;

        var stopwatch = Stopwatch.StartNew();
        long bytesUp = 0;
        long bytesDown = 0;

        bool connected = false;
        TcpClient? outbound = null;
        int maxAttempts = 3;
        int attempts = 0;

        if (requestedHost == null)
        {
            Log.Warning("Host not found in request headers.");
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\nMissing Host header");
            inbound.Close();
            return;
        }

        TokenSessionTracker.RecordStart(token!);
        while (!connected && attempts < maxAttempts)
        {
            try
            {
                outbound = new TcpClient();
                await outbound.ConnectAsync(requestedHost, requestedPort);
                connected = true;
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to connect to {Host}:{Port} (Attempt {Attempt}/{Max}) - {Error}",
                    requestedHost, requestedPort, attempts + 1, maxAttempts, ex.Message);

                if (proxySettings.RoutingStrategy == "RoundRobin" || proxySettings.RoutingStrategy == "Random")
                {
                    // Pick another upstream target to try as fallback
                    var alt = GetNextTarget();
                    requestedHost = alt.Host;
                    requestedPort = alt.Port;
                    Log.Information("Retrying with alternative target {AltHost}:{AltPort}", alt.Host, alt.Port);
                }

                await Task.Delay(1000); // Optional delay before retry
                Log.Information("Retrying now...");
            }
            attempts++;
        }

        if (!connected || outbound == null)
        {
            Log.Error("All connection attempts failed to {Host}:{Port}", requestedHost, requestedPort);
            var writer = new StreamWriter(inbound.GetStream()) { AutoFlush = true };
            await writer.WriteAsync("HTTP/1.1 502 Bad Gateway\r\n\r\nConnection failed");
            inbound.Close();
            return;
        }

        try
        {
            var connectionId = Guid.NewGuid().ToString().Substring(0, 8);
            Log.Information("[{ConnectionId}] Incoming connection from {IP}", connectionId, ((IPEndPoint)inbound.Client.RemoteEndPoint).Address);
            Log.Information("Connected to {Host}:{Port}", requestedHost, requestedPort);
            var inboundStream = inbound.GetStream();
            var outboundStream = outbound.GetStream();

            var upTask = ProxyData(inboundStream, outboundStream, count => bytesUp += count);
            var downTask = ProxyData(outboundStream, inboundStream, count => bytesDown += count);

            await Task.WhenAny(upTask, downTask);
        }
        finally
        {
            stopwatch.Stop();
            var ip = ((IPEndPoint)inbound.Client.RemoteEndPoint!).Address.ToString();

            string? country = null;
            string? city = null;

            // Optional lookup
            (country, city) = await ApiClient.GetGeoInfo(ip);

            IPSessionLogger.Log(token!, ip, bytesUp, bytesDown, country, city);
            Log.Information("Connection closed: {ClientIP} → {Host}:{Port} | {Duration}ms | ↑{BytesUp} ↓{BytesDown}",
                ((IPEndPoint)inbound.Client.RemoteEndPoint).Address,
                requestedHost, requestedPort,
                stopwatch.ElapsedMilliseconds, bytesUp, bytesDown
            );

            inbound?.Dispose();
            outbound?.Dispose();
            TokenSessionTracker.RecordStop(token!, bytesUp, bytesDown);
        }
    }

    async Task ProxyData(Stream source, Stream destination, Action<long> onBytesTransferred)
    {
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            onBytesTransferred(read);
        }
    }
}