public class UpstreamTarget
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public bool IsResidential { get; set; } = false;
    public bool IsOnline { get; set; } = true;
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
}