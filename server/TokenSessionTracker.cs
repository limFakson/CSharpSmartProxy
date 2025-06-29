using Microsoft.EntityFrameworkCore;

public static class TokenSessionTracker
{
    private static readonly Dictionary<string, ActiveTokenState> ActiveTokens = new();
    private static readonly object LockObj = new();

    public static void RecordStart(string token)
    {
        lock (LockObj)
        {
            if (!ActiveTokens.ContainsKey(token))
                ActiveTokens[token] = new ActiveTokenState();

            ActiveTokens[token].ActiveConnections++;
            ActiveTokens[token].LastActivity = DateTime.UtcNow;
        }

        var optionsBuilder = new DbContextOptionsBuilder<SessionDbContext>();

        using var db = new SessionDbContext(optionsBuilder.Options);
        db.Sessions.Add(new TokenSession
        {
            Token = token,
            StartedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    public static void RecordStop(string token, long up, long down)
    {
        lock (LockObj)
        {
            if (ActiveTokens.TryGetValue(token, out var state))
            {
                state.ActiveConnections--;
                state.LastActivity = DateTime.UtcNow;
            }
        }
        var optionsBuilder = new DbContextOptionsBuilder<SessionDbContext>();

        using var db = new SessionDbContext(optionsBuilder.Options);
        var session = db.Sessions
            .Where(s => s.Token == token && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefault();

        if (session != null)
        {
            session.EndedAt = DateTime.UtcNow;
            session.BytesUp = up;
            session.BytesDown = down;
            db.SaveChanges();
        }
    }

    public static bool IsTokenBlocked(string token, LimitSettings limits)
    {
        lock (LockObj)
        {
            if (ActiveTokens.TryGetValue(token, out var state))
            {
                if (state.IsBlocked) return true;
                if (state.ActiveConnections > limits.MaxConnectionsPerToken)
                {
                    state.IsBlocked = true;
                    return true;
                }
            }
        }

        var optionsBuilder = new DbContextOptionsBuilder<SessionDbContext>();

        // DB-based traffic limit (last X mins)
        using var db = new SessionDbContext(optionsBuilder.Options);
        var since = DateTime.UtcNow.AddMinutes(-limits.TimeframeMinutes);

        var totalBytes = db.Sessions
            .Where(s => s.Token == token && s.StartedAt >= since)
            .Sum(s => s.BytesUp + s.BytesDown);

        return totalBytes >= limits.ByteLimit;
    }

    public static void BlockToken(string token)
    {
        lock (LockObj)
        {
            if (!ActiveTokens.ContainsKey(token))
                ActiveTokens[token] = new ActiveTokenState();

            ActiveTokens[token].IsBlocked = true;
        }
    }

    public static void UnblockToken(string token)
    {
        lock (LockObj)
        {
            if (ActiveTokens.TryGetValue(token, out var state))
                state.IsBlocked = false;
        }
    }

    public static Dictionary<string, ActiveTokenState> GetSnapshot()
    {
        lock (LockObj)
        {
            return ActiveTokens.ToDictionary(k => k.Key, v => v.Value);
        }
    }
}