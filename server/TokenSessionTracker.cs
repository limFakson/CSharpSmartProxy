public class TokenSession
{
    public int ActiveConnections { get; set; }
    public long TotalBytesUp { get; set; }
    public long TotalBytesDown { get; set; }
    public int TotalRequests { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public static class TokenSessionTracker
{
    private static readonly Dictionary<string, TokenSession> Sessions = new();
    private static readonly object LockObj = new();

    public static void RecordStart(string token)
    {
        lock (LockObj)
        {
            if (!Sessions.ContainsKey(token))
                Sessions[token] = new TokenSession();

            Sessions[token].ActiveConnections++;
            Sessions[token].TotalRequests++;
            Sessions[token].LastActivity = DateTime.UtcNow;
        }
    }

    public static void RecordStop(string token, long up, long down)
    {
        lock (LockObj)
        {
            if (Sessions.ContainsKey(token))
            {
                Sessions[token].ActiveConnections--;
                Sessions[token].TotalBytesUp += up;
                Sessions[token].TotalBytesDown += down;
                Sessions[token].LastActivity = DateTime.UtcNow;
            }
        }
    }

    public static Dictionary<string, TokenSession> GetSnapshot()
    {
        lock (LockObj)
        {
            return Sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }

    public static bool IsTokenBlocked(string token, LimitSettings limits)
    {
        lock (LockObj)
        {
            if (!Sessions.ContainsKey(token)) return false;

            var session = Sessions[token];

            if (session.IsBlocked) return true;

            if (session.ActiveConnections > limits.MaxConnectionsPerToken ||
                session.TotalRequests > limits.MaxRequestsPerToken)
            {
                session.IsBlocked = true;
                return true;
            }

            return false;
        }
    }
}

