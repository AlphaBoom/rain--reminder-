using System.Security.Cryptography;
using System.Text;

namespace RainReminder.Core;

public static class Policy
{
    public static string LocalDate(WeatherConfig config, DateTimeOffset now) =>
        TimeZoneInfo.ConvertTime(now, config.Zone).ToString("yyyy-MM-dd");

    public static string ConfigId(WeatherConfig config)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Store.Serialize(config)))).Replace("-", "");
    }

    public static bool IsMorning(WeatherConfig config, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, config.Zone);
        var time = local.TimeOfDay;
        return local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            && time >= config.Start && time < config.End
            && !config.ExcludedDates.Contains(LocalDate(config, now), StringComparer.Ordinal);
    }

    public static bool ShouldFetch(WeatherConfig config, ServiceStatus? status, DateTimeOffset now)
    {
        if (!IsMorning(config, now)) return false;
        if (status is null || status.ConfigId != ConfigId(config) || status.LocalDate != LocalDate(config, now)) return true;
        var elapsed = now - status.CheckedAt;
        return elapsed < TimeSpan.Zero || elapsed >= TimeSpan.FromMinutes(status.Error is null ? 30 : 5);
    }

    public static bool ShouldNotify(WeatherConfig config, ServiceStatus? status, UserState state, DateTimeOffset now) =>
        IsMorning(config, now) && state.NotifiedDate != LocalDate(config, now)
        && status is { Error: null, Outlook.Wet: true, IsHoliday: false }
        && status.ConfigId == ConfigId(config) && status.LocalDate == LocalDate(config, now)
        && now >= status.CheckedAt && now - status.CheckedAt <= TimeSpan.FromMinutes(35);

    public static Outlook Analyze(ForecastReply reply, WeatherConfig config, DateTimeOffset now)
    {
        if (reply.Hourly is not { Time: { } times, Probability: { } probs, Precipitation: { } amounts }
            || times.Length == 0 || times.Length != probs.Length || times.Length != amounts.Length)
            throw new InvalidDataException("天气数据不完整，稍后重试。");
        var local = TimeZoneInfo.ConvertTime(now, config.Zone);
        var tomorrow = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        var endOfDay = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(tomorrow, config.Zone));
        var rows = new SortedDictionary<DateTimeOffset, (double Probability, double Amount)>();
        for (var i = 0; i < times.Length; i++)
        {
            var end = DateTimeOffset.FromUnixTimeSeconds(times[i]);
            if (end <= now || end > endOfDay) continue;
            if (probs[i] is not { } p || amounts[i] is not { } mm ||
                !Compatibility.IsFinite(p) || !Compatibility.IsFinite(mm) || p is < 0 or > 100 || mm < 0)
                throw new InvalidDataException("天气数据暂时缺失，稍后重试。");
            if (rows.ContainsKey(end)) throw new InvalidDataException("天气时间数据重复。");
            rows.Add(end, (p, mm));
        }
        // Precipitation is the preceding hour's amount. Retain the interval
        // ending at tomorrow 00:00 to cover rain between 23:00 and midnight.
        var localHour = DateTime.SpecifyKind(local.DateTime.AddTicks(-(local.Ticks % TimeSpan.TicksPerHour)), DateTimeKind.Unspecified);
        var firstEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localHour, config.Zone)).AddHours(1);
        for (var t = firstEnd; t <= endOfDay; t = t.AddHours(1))
            if (!rows.ContainsKey(t)) throw new InvalidDataException("预报未完整覆盖今天剩余时段。");
        if (rows.Count == 0) throw new InvalidDataException("没有今天的天气预报。");
        var wet = rows.Where(r => r.Value.Probability >= config.ProbabilityThreshold || r.Value.Amount >= config.PrecipitationThreshold).ToArray();
        return new Outlook(wet.Length > 0, wet.Length > 0 ? wet[0].Key : null,
            (int)Math.Round(rows.Values.Max(r => r.Probability)), rows.Values.Max(r => r.Amount));
    }
}
