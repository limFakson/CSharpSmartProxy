public class ActiveTokenState
{
    public int ActiveConnections { get; set; }
    public bool IsBlocked { get; set; }
    public long BytesUp { get; set; }
    public long BytesDown { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}