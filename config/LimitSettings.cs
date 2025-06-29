public class LimitSettings
{
    public int MaxConnectionsPerToken { get; set; } = 5;
    public int MaxRequestsPerToken { get; set; } = 100;
    public long ByteLimit { get; set; }
    public int TimeframeMinutes { get; set; }
}