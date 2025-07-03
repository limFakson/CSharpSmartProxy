using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class ProxyDbContextFactory : IDesignTimeDbContextFactory<ProxyDbContext>
{
    public ProxyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProxyDbContext>();
        var connStr = Environment.GetEnvironmentVariable("POSTGRES_URL")
                      ?? "Host=localhost;Port=5432;Username=root;Password=mypassword;Database=mydevdatabase";

        optionsBuilder.UseNpgsql(connStr);
        return new ProxyDbContext(optionsBuilder.Options);
    }
}
public class ProxyDbContext : DbContext
{
    public DbSet<UpstreamTarget> Nodes { get; set; }

    public ProxyDbContext(DbContextOptions<ProxyDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            var connStr = Environment.GetEnvironmentVariable("POSTGRES_URL")
                          ?? "Host=localhost;Port=5432;Username=root;Password=mypassword;Database=mydevdatabase";
            options.UseNpgsql(connStr);
        }
    }
}