using Serilog;
using Microsoft.EntityFrameworkCore;
public static class DBLoader
{
    public static async Task<string> DBLoad()
    {
        // Load PostgresSQL DB
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("POSTGRES_URL")!);

        using (var db = new PostgresDbContext(optionsBuilder.Options))
        {
            db.Database.Migrate(); // Apply pending migrations
            Log.Information("Postgres DB is ready.");
        }

        var proxyOptionsBuilder = new DbContextOptionsBuilder<ProxyDbContext>();
        proxyOptionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("POSTGRES_URL")!);

        using (var proxyDb = new ProxyDbContext(proxyOptionsBuilder.Options))
        {
            proxyDb.Database.Migrate(); // Apply pending migrations
            Log.Information("Proxy DB is ready.");
        }

        // Load SQLite DB
        using (var liteDb = new SessionDbContext())
        {
            liteDb.Database.Migrate();
            Log.Information("SQLite DB is ready.");
        }

        bool exists = await TokenFetcher.TableExistsAsync("ProxyTokens");
        Log.Information("Checking if ProxyTokens table exists: {Exists}", exists);
        if (!exists)
        {
            Log.Error("ProxyTokens table does not exist in the database. Please run migrations.");
            return "ProxyTokens table does not exist in the database. Please run migrations.";
        }

        bool proxyExists = await TokenFetcher.TableExistsAsync("Nodes");
        Log.Information("Checking if Nodes table exists: {Exists}", proxyExists);
        if (!proxyExists)
        {
            Log.Error("Nodes table does not exist in the database. Please run migrations.");
            return "Nodes table does not exist in the database. Please run migrations.";
        }

        UpstreamManager.LoadFromDatabase();
        return "Databases are ready.";
    }
}