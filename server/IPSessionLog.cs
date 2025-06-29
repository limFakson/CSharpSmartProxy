public class IPSessionLog
{
    public string Token { get; set; } = "";
    public string IPAddress { get; set; } = "";
    public string? Country { get; set; }
    public string? City { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public long BytesUp { get; set; }
    public long BytesDown { get; set; }
}

public static class IPSessionLogger
{
    private static readonly List<IPSessionLog> Logs = new();
    private static readonly object LockObj = new();

    public static void Log(string token, string ip, long up, long down, string? country = null, string? city = null)
    {
        lock (LockObj)
        {
            Logs.Add(new IPSessionLog
            {
                Token = token,
                IPAddress = ip,
                Country = country,
                City = city,
                BytesUp = up,
                BytesDown = down
            });

            // Optional: Keep log size under control
            if (Logs.Count > 1000)
                Logs.RemoveAt(0);
        }
    }

    public static List<IPSessionLog> GetLogs()
    {
        lock (LockObj)
        {
            return Logs.ToList();
        }
    }
}
