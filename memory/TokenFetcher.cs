// Script for fetching valid tokens from PostgresSQL DB
using Npgsql;

public static class TokenFetcher
{
    public static async Task<List<string>> GetValidTokensAsync(string token)
    {
        var connString = Environment.GetEnvironmentVariable("POSTGRES_URL");

        if (string.IsNullOrWhiteSpace(connString))
            throw new InvalidOperationException("POSTGRES_URL is not set in environment.");

        var tokens = new List<string>();

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        const string query = "SELECT token FROM proxy_tokens WHERE is_active = TRUE";

        await using var cmd = new NpgsqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tokens.Add(reader.GetString(0));
        }

        return tokens;
    }
}
