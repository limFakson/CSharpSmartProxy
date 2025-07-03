public class UpstreamTarget
{
    public int Id { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public bool IsResidential { get; set; } = false;
    public bool IsOnline { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
}

public class UpstreamPing
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public bool IsOnline { get; set; } = true;
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
}