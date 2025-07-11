public static class ProxyDataTransfer
{
    public static async Task ProxyData(Stream source, Stream destination)
    {
        try
        {
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            await destination.FlushAsync();
        }           
        } catch {}
    }
}