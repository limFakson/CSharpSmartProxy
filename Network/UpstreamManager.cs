using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Serilog;

public static class UpstreamManager
{
    private static readonly List<UpstreamTarget> Nodes = new();
    private static int currentIndex = 0;
    private static readonly TimeSpan ExpiryThreshold = TimeSpan.FromSeconds(190);
    private static readonly object LockObj = new();

    public static void LoadFromDatabase()
    {
        using var db = new ProxyDbContext(DatabaseUtils.GetOptions());

        var dbNodes = db.Nodes
            .Where(n => n.IsResidential && n.IsOnline)
            .ToList();

        lock (LockObj)
        {
            Nodes.Clear();
            Nodes.AddRange(dbNodes);
        }

        Console.WriteLine($"🔄 Loaded {dbNodes.Count} active residential nodes from DB.");
    }

    public static void RegisterNode(UpstreamTarget target)
    {
        lock (LockObj)
        {
            using var db = new ProxyDbContext(DatabaseUtils.GetOptions());

            var existing = db.Nodes.FirstOrDefault(n => n.Host == target.Host && n.Port == target.Port);
            if (existing != null)
            {
                existing.IsResidential = target.IsResidential;
                existing.IsOnline = true;
                existing.LastChecked = DateTime.UtcNow;
            }
            else
            {
                target.CreatedAt = DateTime.UtcNow;
                db.Nodes.Add(target);
            }

            db.SaveChanges();

            Nodes.RemoveAll(n => n.Host == target.Host && n.Port == target.Port);
            Nodes.Add(target);
        }
        LoadFromDatabase();
        Console.WriteLine($"✅ Registered node: {target.Host}:{target.Port} (Residential: {target.IsResidential})");
        Console.WriteLine("Calls Load from db to get nodes refreshed.");
    }

    public static UpstreamTarget? GetResidentialNodes(string strategy = "RoundRobin")
    {
        lock (LockObj)
        {
            var liveNodes = Nodes
                .Where(n => n.IsResidential && n.IsOnline)
                .ToList();

            if (strategy == "Random")
            {
                var rand = new Random();
                return liveNodes[rand.Next(liveNodes.Count)];
            }

            // Default to RoundRobin
            var node = liveNodes[currentIndex % liveNodes.Count];
            currentIndex++;
            return node;
        }
    }

    public static void MarkOffline()
    {
        lock (LockObj)
        {
            var now = DateTime.UtcNow;
            using var db = new ProxyDbContext(DatabaseUtils.GetOptions());

            var nodes = db.Nodes.Where(n => n.IsOnline && (now - n.LastChecked) > ExpiryThreshold).ToList();
            if (nodes.Count > 0)
            {
                foreach (var node in nodes)
                {
                    node.IsOnline = false;
                    node.LastChecked = DateTime.UtcNow;
                    db.SaveChanges();
                }
            }
            Log.Information($"{nodes.Count} has been marked offline for being inactive for 3mins");
            LoadFromDatabase();
        }
    }

    public static bool NodePing(UpstreamPing ping)
    {
        lock (LockObj)
        {
            using var db = new ProxyDbContext(DatabaseUtils.GetOptions());

            var node = db.Nodes.FirstOrDefault(n => n.Host == ping.Host && n.Port == ping.Port);
            if (node != null)
            {
                node.IsOnline = ping.IsOnline;
                node.LastChecked = ping.LastChecked;
                db.SaveChanges();

                var memNode = Nodes.FirstOrDefault(n => n.Host == ping.Host && n.Port == ping.Port);
                if (memNode != null)
                {
                    memNode.IsOnline = ping.IsOnline;
                    memNode.LastChecked = ping.LastChecked;
                }
                return true;
            }

            return false;
        }
    }
}