using System.Net.Http.Json;

public static class ApiClient
{
    public static async Task<(string? Country, string? City)> GetGeoInfo(string ip)
    {
        try
        {
            using var http = new HttpClient();
            var response = await http.GetFromJsonAsync<dynamic>($"http://ip-api.com/json/{ip}?fields=country,city");
            return (response?.country, response?.city);
        }
        catch
        {
            return (null, null);
        }
    }
}