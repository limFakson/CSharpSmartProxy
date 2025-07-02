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
            Log.Information("PostgresSQL DB is ready.");
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
        return "Databases are ready.";
    }
}