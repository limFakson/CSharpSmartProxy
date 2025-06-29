using Microsoft.EntityFrameworkCore;

public class SessionDbContext : DbContext
{
    public DbSet<TokenSession> Sessions { get; set; }

    public SessionDbContext(DbContextOptions<SessionDbContext> options)
        : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=sessions.db");
        }
    }
}