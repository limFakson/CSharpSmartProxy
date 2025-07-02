public static class UpstreamManager
{
    private static readonly List<UpstreamTarget> Nodes = new();

    public static void RegisterNode(UpstreamTarget target)
    {
        lock (Nodes)
        {
            Nodes.RemoveAll(n => n.Host == target.Host && n.Port == target.Port);
            Nodes.Add(target);
        }
    }

    public static List<UpstreamTarget> GetResidentialNodes()
    {
        lock (Nodes)
        {
            return Nodes.Where(n => n.IsResidential == true && n.IsOnline == true).ToList();
        }
    }

    public static void MarkOffline(string host, int port)
    {
        lock (Nodes)
        {
            var node = Nodes.FirstOrDefault(n => n.Host == host && n.Port == port);
            if (node != null) node.IsOnline = false;
        }
    }
}
