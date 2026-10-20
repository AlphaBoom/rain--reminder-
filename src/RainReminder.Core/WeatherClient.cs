using System.Globalization;
using System.Net.Http;

namespace RainReminder.Core;

public sealed class WeatherClient(HttpClient http)
{
    public async Task<Location[]> SearchAsync(string query, CancellationToken token)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=10&language=zh&format=json&name=" + Uri.EscapeDataString(query.Trim());
        var reply = Store.ParseObject(await GetTextAsync(url, token));
        return Store.ArrayValue(Store.Value(reply, "results")).OfType<Dictionary<string, object>>()
            .Select(x => new Location { Name = Store.Text(x, "name"), Latitude = Store.Number(x, "latitude"), Longitude = Store.Number(x, "longitude"),
                TimeZone = Store.Text(x, "timezone"), Country = Store.Text(x, "country"), Region = Store.Text(x, "admin1") })
            .Where(x => !string.IsNullOrWhiteSpace(x.TimeZone)).ToArray();
    }
    public async Task<ForecastReply> ForecastAsync(WeatherConfig config, CancellationToken token)
    {
        var url = "https://api.open-meteo.com/v1/forecast?forecast_days=2&timeformat=unixtime&hourly=precipitation_probability,precipitation"
            + "&latitude=" + config.Latitude.ToString(CultureInfo.InvariantCulture)
            + "&longitude=" + config.Longitude.ToString(CultureInfo.InvariantCulture)
            + "&timezone=" + Uri.EscapeDataString(config.TimeZone);
        var reply = Store.ParseObject(await GetTextAsync(url, token));
        if (Store.Value(reply, "hourly") is not Dictionary<string, object> h) throw new InvalidDataException("天气接口返回空数据。");
        return new ForecastReply(new HourlyData(
            Store.ArrayValue(Store.Value(h, "time")).Select(x => Convert.ToInt64(x, CultureInfo.InvariantCulture)).ToArray(),
            Numbers(Store.Value(h, "precipitation_probability")), Numbers(Store.Value(h, "precipitation"))));
    }
    private static double?[] Numbers(object? values) => Store.ArrayValue(values).Select(x => x is null ? (double?)null : Convert.ToDouble(x, CultureInfo.InvariantCulture)).ToArray();
    private async Task<string> GetTextAsync(string url, CancellationToken token)
    {
        using var response = await http.GetAsync(url, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
    public async Task<byte[]> HolidayBytesAsync(CancellationToken token)
    {
        using var response = await http.GetAsync(JapanCalendar.Source, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }
    public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(20), DefaultRequestHeaders = { { "User-Agent", "RainReminder/0.2" } } };
}

