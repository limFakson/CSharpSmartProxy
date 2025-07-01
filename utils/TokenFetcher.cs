using Npgsql;
// Script for fetching valid tokens from PostgresSQL DB
public static class TokenFetcher
{
    public static async Task<HashSet<string>> GetValidTokensAsync()
    {
        var connString = Environment.GetEnvironmentVariable("POSTGRES_URL");
        var psqlProvider = new PostgresTokenProvider(connString);

        var tokens = await psqlProvider.GetActiveTokensAsync();
        if (tokens == null)
        {
            throw new InvalidOperationException("Postgres token provider is not configured.");
        }

        return new HashSet<string>(tokens);
    }

    public static async Task<bool> TableExistsAsync(string tableName = "ProxyTokens")
    {
        var _connectionString = Environment.GetEnvironmentVariable("POSTGRES_URL");
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @tableName);",
            conn);
        cmd.Parameters.AddWithValue("tableName", tableName);

        return (bool)await cmd.ExecuteScalarAsync();
    }
}
