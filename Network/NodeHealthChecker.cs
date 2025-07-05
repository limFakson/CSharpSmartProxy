
public static class NodeHealthManager
{
    public static void StartNodeStaleChecker()
    {
        Console.WriteLine("Node Health Checker task beings");
        Task.Run(async () =>
        {
            while (true)
            {
                UpstreamManager.MarkOffline();
                await Task.Delay(TimeSpan.FromSeconds(95)); // check every minute
            }
        });
    }
}
