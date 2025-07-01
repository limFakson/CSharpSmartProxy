public class TokenSession
{
    public int Id { get; set; }
    required public string Token { get; set; }
    public long BytesUp { get; set; }
    public long BytesDown { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}