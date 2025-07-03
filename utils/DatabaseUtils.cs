using Microsoft.EntityFrameworkCore;

public static class DatabaseUtils
{
    public static DbContextOptions<ProxyDbContext> GetOptions()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProxyDbContext>();
        var connStr = Environment.GetEnvironmentVariable("POSTGRES_URL")
                      ?? "Host=localhost;Port=5432;Database=proxy;Username=postgres;Password=postgres";
        optionsBuilder.UseNpgsql(connStr);
        return optionsBuilder.Options;
    }
}
