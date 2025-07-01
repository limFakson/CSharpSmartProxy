using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class TokenSeeder
{
    public static async Task SeedAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("POSTGRES_URL")!);

        using var db = new PostgresDbContext(optionsBuilder.Options);

        var token = "TEST-TOKEN-123";
        if (!await db.ProxyTokens.AnyAsync(t => t.Token == token))
        {
            db.ProxyTokens.Add(new ProxyToken { Token = token });
            await db.SaveChangesAsync();
            Console.WriteLine($"✅ Seeded token: {token}");
        }
        else
        {
            Console.WriteLine("⚠️ Token already exists");
        }
    }
}
