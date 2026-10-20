using System.Globalization;
using System.Net.Http.Json;

namespace RainReminder.Core;

public sealed class WeatherClient(HttpClient http)
{
    public async Task<Location[]> SearchAsync(string query, CancellationToken token)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=10&language=zh&format=json&name=" + Uri.EscapeDataString(query.Trim());
        var reply = await http.GetFromJsonAsync<GeocodingReply>(url, Store.Json, token);
        return reply?.Results?.Where(x => !string.IsNullOrWhiteSpace(x.TimeZone)).ToArray() ?? [];
    }

    public async Task<ForecastReply> ForecastAsync(WeatherConfig config, CancellationToken token)
    {
        var url = "https://api.open-meteo.com/v1/forecast?forecast_days=2&timeformat=unixtime&hourly=precipitation_probability,precipitation"
            + "&latitude=" + config.Latitude.ToString(CultureInfo.InvariantCulture)
            + "&longitude=" + config.Longitude.ToString(CultureInfo.InvariantCulture)
            + "&timezone=" + Uri.EscapeDataString(config.TimeZone);
        return await http.GetFromJsonAsync<ForecastReply>(url, Store.Json, token)
            ?? throw new InvalidDataException("天气接口返回空数据。");
    }

    public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(20), DefaultRequestHeaders = { { "User-Agent", "RainReminder/0.1" } } };
}
