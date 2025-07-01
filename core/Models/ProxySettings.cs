public class ProxySettings
{
    public int ListenPort { get; set; }
    public List<UpstreamTarget> UpstreamTargets { get; set; }
    public string[] ValidTokens { get; set; } = [];
    public string RoutingStrategy { get; set; } = "RoundRobin";
}
