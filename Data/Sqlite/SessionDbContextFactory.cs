using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class SessionDbContextFactory : IDesignTimeDbContextFactory<SessionDbContext>
{
    public SessionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SessionDbContext>();
        optionsBuilder.UseSqlite("Data Source=sessions.db");

        return new SessionDbContext(optionsBuilder.Options);
    }
}