using RainReminder.Core;
using System.Net.Http;

var count = 0;
void Test(string name, Action action)
{
    action();
    Console.WriteLine("PASS " + name);
    count++;
}
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Throws(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception("Expected invalid data");
}
var config = new WeatherConfig { Name = "仙台", Latitude = 38.27, Longitude = 140.87 }.Validate();
var now = DateTimeOffset.Parse("2026-10-20T08:00:00+09:00");
ForecastReply Forecast(int wetHour = -1, double probability = 0, double mm = 0)
{
    var midnight = DateTimeOffset.Parse("2026-10-20T00:00:00+09:00");
    return new(new HourlyData(
        Enumerable.Range(0, 25).Select(x => midnight.AddHours(x).ToUnixTimeSeconds()).ToArray(),
        Enumerable.Range(0, 25).Select(x => (double?)(x == wetHour ? probability : 0)).ToArray(),
        Enumerable.Range(0, 25).Select(x => (double?)(x == wetHour ? mm : 0)).ToArray()));
}
ServiceStatus Status(Outlook outlook) => new(now, Policy.ConfigId(config), "2026-10-20", outlook, null);

Test("weekday in morning", () => Assert(Policy.IsMorning(config, now)));
Test("exclude weekend", () => Assert(!Policy.IsMorning(config, now.AddDays(4))));
Test("end time exclusive", () => Assert(!Policy.IsMorning(config, now.AddHours(2))));
Test("start time inclusive", () => Assert(Policy.IsMorning(config, now.AddHours(-2))));
Test("custom leave day", () => Assert(!Policy.IsMorning(config with { ExcludedDates = ["2026-10-20"] }, now)));
Test("computer UTC does not alter Japan morning", () => Assert(Policy.IsMorning(config, now.ToUniversalTime())));
Test("dry day is silent", () => Assert(!Policy.Analyze(Forecast(), config, now).Wet));
Test("40 percent threshold", () => Assert(Policy.Analyze(Forecast(13, 40), config, now).Wet));
Test("39 percent stays silent", () => Assert(!Policy.Analyze(Forecast(13, 39), config, now).Wet));
Test("amount independently triggers", () => Assert(Policy.Analyze(Forecast(18, 10, 0.1), config, now).Wet));
Test("past rain ignored", () => Assert(!Policy.Analyze(Forecast(7, 100, 3), config, now).Wet));
Test("current hour retained", () => Assert(Policy.Analyze(Forecast(9, 100), config, now.AddMinutes(25)).Wet));
Test("late night rain retained", () => Assert(Policy.Analyze(Forecast(24, 80), config, now).Wet));
Test("mismatched response rejected", () => Throws(() => Policy.Analyze(new(new HourlyData([1], [], [])), config, now)));
Test("missing hour rejected", () => {
    var f = Forecast(); f.Hourly!.Time![15] = f.Hourly.Time[15] + 60;
    Throws(() => Policy.Analyze(f, config, now));
});
Test("null data rejected", () => {
    var f = Forecast(); f.Hourly!.Probability![15] = null;
    Throws(() => Policy.Analyze(f, config, now));
});
Test("stale prior-day forecast rejected", () => Throws(() => Policy.Analyze(Forecast(), config, now.AddDays(1))));
Test("first check runs immediately", () => Assert(Policy.ShouldFetch(config, null, now)));
Test("dry check refreshed after 30 minutes", () => {
    var s = Status(Policy.Analyze(Forecast(), config, now));
    Assert(!Policy.ShouldFetch(config, s, now.AddMinutes(29)));
    Assert(Policy.ShouldFetch(config, s, now.AddMinutes(30)));
});
Test("network errors retry after 5 minutes", () => {
    var s = new ServiceStatus(now, Policy.ConfigId(config), "2026-10-20", null, "offline");
    Assert(!Policy.ShouldFetch(config, s, now.AddMinutes(4)));
    Assert(Policy.ShouldFetch(config, s, now.AddMinutes(5)));
});
Test("new morning does not inherit old delay", () => Assert(Policy.ShouldFetch(config, Status(new(false, null, 0, 0)), now.AddDays(1))));
Test("rain reminder delivered once per day", () => {
    var s = Status(Policy.Analyze(Forecast(18, 80), config, now));
    Assert(Policy.ShouldNotify(config, s, new UserState(), now));
    Assert(!Policy.ShouldNotify(config, s, new UserState("2026-10-20"), now));
});
Test("locked until after morning cannot yield late reminder", () => {
    var s = Status(Policy.Analyze(Forecast(18, 80), config, now));
    Assert(!Policy.ShouldNotify(config, s, new UserState(), now.AddHours(3)));
});
Test("stale status never notifies", () => {
    var s = Status(Policy.Analyze(Forecast(18, 80), config, now));
    Assert(!Policy.ShouldNotify(config, s, new UserState(), now.AddMinutes(36)));
});
Test("changed location invalidates old status", () => {
    var s = Status(Policy.Analyze(Forecast(18, 80), config, now));
    Assert(!Policy.ShouldNotify(config with { Name = "東京" }, s, new UserState(), now));
});
Test("hidden tray default", () => Assert(!config.ShowTrayIcon));
Test("computed config properties are not serialized", () => {
    var json = Store.Serialize(config);
    Assert(!json.Contains("\"zone\"") && !json.Contains("\"start\""));
});
Test("official Japan calendar handles statutory and substitute holidays", () => {
    var path = Path.Combine(AppContext.BaseDirectory, "japan-holidays.csv");
    var holidays = JapanCalendar.Parse(File.ReadAllBytes(path), now);
    Assert(holidays.Covers(2026) && holidays.Covers(2027));
    Assert(holidays.IsHoliday("2026-11-03"));
    Assert(holidays.IsHoliday("2026-05-06")); // 振替休日
    Assert(holidays.IsHoliday("2026-09-22")); // 国民の休日
    Assert(!holidays.IsHoliday("2026-10-20"));
    Assert(!holidays.Covers(2099));
});
Test("holiday blocks even a rainy status", () => {
    var s = Status(Policy.Analyze(Forecast(18, 80), config, now)) with { IsHoliday = true };
    Assert(!Policy.ShouldNotify(config, s, new UserState(), now));
});
Test("invalid holiday CSV rejected", () => Throws(() => JapanCalendar.Parse(System.Text.Encoding.UTF8.GetBytes("<html>outage</html>"), now)));
Test("atomic JSON round trip", () => {
    var path = Path.Combine(Path.GetTempPath(), "rain-test-" + Guid.NewGuid().ToString("N"), "config.json");
    try { Store.Write(path, config); Assert(Store.Read<WeatherConfig>(path)?.Name == config.Name); }
    finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
});
Test("uses Windows built-in Framework CLR", () => Assert(typeof(object).Assembly.GetName().Name == "mscorlib" && Environment.Version.Major == 4));
Test("IANA Japan timezone resolves on Windows", () => Assert(Compatibility.FindZone("Asia/Tokyo").BaseUtcOffset == TimeSpan.FromHours(9)));
Test("cache and status JSON retain ISO timestamps", () => {
    var directory = Path.Combine(Path.GetTempPath(), "rain-state-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, "state.json");
    try
    {
        var status = Status(Policy.Analyze(Forecast(18, 80), config, now));
        Store.Write(path, status);
        Assert(Store.Read<ServiceStatus>(path) == status);
        var calendar = new HolidayCache(now, ["2026-11-03", "2026-11-23"]);
        Store.Write(path, calendar);
        var copy = Store.Read<HolidayCache>(path)!;
        Assert(copy.UpdatedAt == now && copy.Dates.SequenceEqual(calendar.Dates));
        Store.Write(path, new UserState("2026-10-20"));
        Assert(Store.Read<UserState>(path)?.NotifiedDate == "2026-10-20");
    }
    finally { Directory.Delete(directory, true); }
});
Test("old settings remain compatible", () => {
    var path = Path.Combine(Path.GetTempPath(), "rain-old-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        File.WriteAllText(path, "{\"name\":\"Sendai\",\"latitude\":38.27,\"longitude\":140.87,\"timeZone\":\"Asia/Tokyo\",\"morningStart\":\"06:00\",\"morningEnd\":\"10:00\",\"probabilityThreshold\":40,\"precipitationThreshold\":0.1,\"excludedDates\":[],\"showTrayIcon\":false}");
        Assert(Store.Read<WeatherConfig>(path)!.Validate().Name == "Sendai");
    }
    finally { File.Delete(path); }
});
Test("built-in JSON parser reads forecast and geocoding API shapes", () => {
    using var http = new HttpClient(new FixtureHandler());
    var client = new WeatherClient(http);
    var matches = client.SearchAsync("Sendai", CancellationToken.None).GetAwaiter().GetResult();
    Assert(matches.Length == 1 && matches[0].TimeZone == "Asia/Tokyo");
    var result = client.ForecastAsync(config, CancellationToken.None).GetAwaiter().GetResult();
    Assert(result.Hourly!.Time!.Length == 2 && result.Hourly.Probability![0] == 40 && result.Hourly.Precipitation![1] is null);
});
Console.WriteLine($"{count} tests passed.");
if (args.Contains("--live"))
{
    using var http = WeatherClient.CreateHttpClient();
    var client = new WeatherClient(http);
    var matches = await client.SearchAsync("Sendai", CancellationToken.None);
    Assert(matches.Length > 0);
    var forecast = await client.ForecastAsync(config, CancellationToken.None);
    Assert(forecast.Hourly?.Time?.Length >= 48);
    Console.WriteLine($"Live API contract passed: {matches.Length} location(s), {forecast.Hourly!.Time!.Length} forecast hours.");
}

sealed class FixtureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var json = request.RequestUri!.Host.StartsWith("geocoding", StringComparison.Ordinal)
            ? "{\"results\":[{\"name\":\"Sendai\",\"latitude\":38.27,\"longitude\":140.87,\"timezone\":\"Asia/Tokyo\",\"country\":\"Japan\",\"admin1\":\"Miyagi\"}]}"
            : "{\"hourly\":{\"time\":[1792454400,1792458000],\"precipitation_probability\":[40,null],\"precipitation\":[0.1,null]}}";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
