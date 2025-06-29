using Npgsql;
using Microsoft.EntityFrameworkCore;

public class PostgresTokenProvider
{
    private readonly string _connectionString;

    public PostgresTokenProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<string>> GetActiveTokensAsync()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var tokens = new List<string>();
        var cmd = new NpgsqlCommand("SELECT \"Token\" FROM \"ProxyTokens\" WHERE \"IsActive\" = TRUE AND \"IsBlocked\" = FALSE", conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tokens.Add(reader.GetString(0));
        }

        return tokens;
    }
}