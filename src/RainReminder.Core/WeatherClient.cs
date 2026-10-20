using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace RainReminder.Core;

public sealed class WeatherClient(HttpClient http)
{
    private static readonly Dictionary<string, Location[]> SearchCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Location[]> SearchAsync(string query, CancellationToken token, string? countryCode = null)
    {
        query = query.Trim().Normalize(NormalizationForm.FormKC);
        if (query.Length == 0) return [];
        var cacheKey = (countryCode ?? "*") + ":" + query;
        lock (SearchCache) if (SearchCache.TryGetValue(cacheKey, out var cached)) return cached;
        Location[] results = [];
        if (countryCode == "JP" && query.Any(c => c >= '\u3040' && c <= '\u9fff'))
        {
            try { results = await SearchJapanAsync(query, token); }
            catch (HttpRequestException) { /* Keep the global source available. */ }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
        }
        if (results.Length == 0) results = await SearchGlobalAsync(query, token, countryCode);
        results = results.GroupBy(x => x.Name + "|" + x.Latitude.ToString("F4", CultureInfo.InvariantCulture) + "|" + x.Longitude.ToString("F4", CultureInfo.InvariantCulture))
            .Select(g => g.First()).Take(20).ToArray();
        if (results.Length > 0) lock (SearchCache)
        {
            if (SearchCache.Count >= 64) SearchCache.Clear();
            SearchCache[cacheKey] = results;
        }
        return results;
    }

    private async Task<Location[]> SearchGlobalAsync(string query, CancellationToken token, string? countryCode)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=20&language=ja&format=json&name=" + Uri.EscapeDataString(query)
            + (countryCode is null ? "" : "&countryCode=" + Uri.EscapeDataString(countryCode));
        var reply = Store.ParseObject(await GetTextAsync(url, token));
        return Store.ArrayValue(Store.Value(reply, "results")).OfType<Dictionary<string, object>>()
            .Select(x => new Location { Name = Store.Text(x, "name"), Latitude = Store.Number(x, "latitude"), Longitude = Store.Number(x, "longitude"),
                TimeZone = Store.Text(x, "timezone"), Country = Store.Text(x, "country"), Region = Store.Text(x, "admin1") })
            .Where(x => !string.IsNullOrWhiteSpace(x.TimeZone)).ToArray();
    }

    private async Task<Location[]> SearchJapanAsync(string query, CancellationToken token)
    {
        // GSI's Japanese locality index covers kanji names missing from GeoNames.
        // Only explicit user searches call this endpoint; results are cached.
        var json = await GetTextAsync("https://msearch.gsi.go.jp/address-search/AddressSearch?q=" + Uri.EscapeDataString(query), token);
        var results = new List<Location>();
        foreach (var row in Store.ArrayValue(Store.ParseJson(json)).OfType<Dictionary<string, object>>())
        {
            if (Store.Value(row, "properties") is not Dictionary<string, object> p
                || Store.Value(row, "geometry") is not Dictionary<string, object> geometry) continue;
            var title = Store.Text(p, "title");
            if (title.Length == 0 || Store.Text(p, "dataSource") == "3") continue; // Exclude facility POIs.
            var coordinates = Store.ArrayValue(Store.Value(geometry, "coordinates"));
            if (coordinates.Length < 2) continue;
            var prefecture = Regex.Match(title, @"^(東京都|北海道|京都府|大阪府|.{2,3}県)");
            var region = prefecture.Success ? prefecture.Value : "";
            var name = prefecture.Success ? title.Substring(region.Length) : title;
            if (name.Length == 0) { name = title; region = ""; }
            results.Add(new Location { Name = name, Region = region, Country = "日本", TimeZone = "Asia/Tokyo",
                Longitude = Convert.ToDouble(coordinates[0], CultureInfo.InvariantCulture), Latitude = Convert.ToDouble(coordinates[1], CultureInfo.InvariantCulture) });
        }
        return results.OrderBy(x => x.Name == query || x.Name == query + "市" ? 0 : 1).ToArray();
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
    public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(15), DefaultRequestHeaders = { { "User-Agent", "RainReminder/0.3 (+https://github.com/AlphaBoom/rain--reminder-)" } } };
}

