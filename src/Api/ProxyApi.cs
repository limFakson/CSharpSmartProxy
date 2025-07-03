using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

public static class ProxyApi
{
    public static WebApplication CreateApiApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        
        app.MapGet("/stats", () =>
        {
            var data = TokenSessionTracker.GetSnapshot();
            return Results.Json(data);
        });

        app.MapGet("/api/tokens", () =>
        {
            var data = TokenSessionTracker.GetSnapshot();
            return Results.Json(data);
        });

        app.MapPost("/api/tokens/block", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<Dictionary<string, string>>();
            if (form != null && form.TryGetValue("token", out var token))
            {
                TokenSessionTracker.BlockToken(token);
                return Results.Ok(new { message = $"Token {token} blocked." });
            }
            return Results.BadRequest(new { error = "Token required" });
        });

        app.MapPost("/api/tokens/unblock", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFromJsonAsync<Dictionary<string, string>>();
            if (form != null && form.TryGetValue("token", out var token))
            {
                TokenSessionTracker.UnblockToken(token);
                return Results.Ok(new { message = $"Token {token} unblocked." });
            }
            return Results.BadRequest(new { error = "Token required" });
        });

        app.MapGet("/api/system/stats", () =>
        {
            var snapshot = TokenSessionTracker.GetSnapshot();
            var totalConnections = snapshot.Sum(x => x.Value.ActiveConnections);
            var totalBytesUp = snapshot.Sum(x => x.Value.BytesUp);
            var totalBytesDown = snapshot.Sum(x => x.Value.BytesDown);

            return Results.Json(new
            {
                uptime = Environment.TickCount64 / 1000,
                active_tokens = snapshot.Count,
                total_connections = totalConnections,
                bytes_up = totalBytesUp,
                bytes_down = totalBytesDown
            });
        });

        app.MapGet("/api/ip-logs", () =>
        {
            var logs = IPSessionLogger.GetLogs();
            return Results.Json(logs);
        });

        app.MapPost("/api/nodes/register", async (HttpContext ctx) =>
        {
            var data = await ctx.Request.ReadFromJsonAsync<UpstreamTarget>();
            if (data == null || string.IsNullOrWhiteSpace(data.Host))
                return Results.BadRequest(new { error = "Invalid node data" });

            UpstreamManager.RegisterNode(data);
            return Results.Ok(new { message = $"Node {data.Host}:{data.Port} registered." });
        });

        app.MapGet("/api/nodes/online", () =>
        {
            var nodes = UpstreamManager.GetResidentialNodes();

            // if (nodes.Count == 0)
            // {
            //     return Results.NotFound(new { error = "No online nodes available" });
            // }
            return Results.Json(nodes);
        });

        app.MapPost("/api/nodes/ping", async (HttpContext ctx) =>
        {
            var pingData = await ctx.Request.ReadFromJsonAsync<UpstreamPing>();
            if (pingData == null || string.IsNullOrWhiteSpace(pingData.Host))
                return Results.BadRequest(new { error = "Invalid ping data" });

            UpstreamManager.NodePing(pingData);
            return Results.Ok(new { message = $"Node {pingData.Host}:{pingData.Port} pinged." });
        });

        return app;
    }
}
// In a real system this would be in a separate file