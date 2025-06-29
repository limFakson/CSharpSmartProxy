public class ProxySettings
{
    public int ListenPort { get; set; }
    public List<UpstreamTarget> UpstreamTargets { get; set; }
    public string[] ValidTokens { get; set; } = [];
    public string RoutingStrategy { get; set; } = "RoundRobin";
}

public class LimitSettings
{
    public int MaxConnectionsPerToken { get; set; } = 5;
    public int MaxRequestsPerToken { get; set; } = 100;
    public long ByteLimit { get; set; }
    public int TimeframeMinutes { get; set; } 
}