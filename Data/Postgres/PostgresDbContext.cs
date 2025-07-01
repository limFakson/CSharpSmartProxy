using Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class DesignTimePostgresDbContextFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        // Use a fallback connection string or read from .env or appsettings.json as needed
        var connStr = Environment.GetEnvironmentVariable("POSTGRES_URL") 
                      ?? "Host=localhost;Port=5432;Database=proxy;Username=postgres;Password=postgres";
        optionsBuilder.UseNpgsql(connStr);
        return new PostgresDbContext(optionsBuilder.Options);
    }
}

public class PostgresDbContext : DbContext
{
    public DbSet<ProxyToken> ProxyTokens { get; set; }

    public PostgresDbContext(DbContextOptions<PostgresDbContext> options)
        : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            var connectionString = Environment.GetEnvironmentVariable("POSTGRES_URL")!;
            options.UseNpgsql(connectionString);
        }
    }
}