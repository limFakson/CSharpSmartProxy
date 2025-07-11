public class TokenSession
{
    public int Id { get; set; }
    required public string Token { get; set; }
    public long BytesUp { get; set; } = 0;
    public long BytesDown { get; set; } = 0;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}