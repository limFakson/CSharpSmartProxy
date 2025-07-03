public static class RateLimiter
{
    private static readonly Dictionary<string, DateTime> TokenLastRequest = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    public static bool AllowRequest(string token)
    {
        lock (TokenLastRequest)
        {
            if (!TokenLastRequest.TryGetValue(token, out var last))
            {
                TokenLastRequest[token] = DateTime.UtcNow;
                return true;
            }

            if (DateTime.UtcNow - last > Cooldown)
            {
                TokenLastRequest[token] = DateTime.UtcNow;
                return true;
            }

            return false;
        }
    }
}