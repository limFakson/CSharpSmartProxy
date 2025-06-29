public class ProxyToken
{
    public int Id { get; set; }
    public string Token { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}