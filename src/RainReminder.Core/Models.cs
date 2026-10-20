using System.Globalization;


namespace RainReminder.Core;

public sealed record WeatherConfig
{
    public string Name { get; init; } = "";
    public string Region { get; init; } = "";
    public string Country { get; init; } = "";
    public string PlaceLabel => Location.FormatLabel(Name, Region, Country);
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string TimeZone { get; init; } = "Asia/Tokyo";
    public string MorningStart { get; init; } = "06:00";
    public string MorningEnd { get; init; } = "10:00";
    public int ProbabilityThreshold { get; init; } = 40;
    public double PrecipitationThreshold { get; init; } = 0.1;
    public string[] ExcludedDates { get; init; } = [];
    public bool ShowTrayIcon { get; init; }

    public TimeZoneInfo Zone => Compatibility.FindZone(TimeZone);
    public TimeSpan Start => ParseTime(MorningStart, "开始时间");
    public TimeSpan End => ParseTime(MorningEnd, "结束时间");
    private static TimeSpan ParseTime(string value, string field)
    {
        var parts = (value ?? "").Trim().Replace('：', ':').Split(':');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 2 || parts[1].Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || hours is < 0 or > 23 || minutes is < 0 or > 59)
            throw new ArgumentException($"{field}无效：请填写 24 小时制时间，例如 06:00 或 6:00。");
        return new TimeSpan(hours, minutes, 0);
    }

    public static string[] ParseExcludedDates(string text)
    {
        return (text ?? "").Split(new[] { ',', '，', '、', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).Select(NormalizeDate).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeDate(string text)
    {
        var value = (text ?? "").Trim();
        if (!DateTime.TryParseExact(value, new[] { "yyyy-MM-dd", "yyyy-M-d", "yyyy/M/d", "yyyy.MM.dd", "yyyy.M.d" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException($"额外休假日期“{value}”无效：请填写完整年月日，例如 2026-11-03；多个日期用逗号分隔。");
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public WeatherConfig Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("请选择地点。");
        if (!Compatibility.IsFinite(Latitude) || Latitude is < -90 or > 90 ||
            !Compatibility.IsFinite(Longitude) || Longitude is < -180 or > 180)
            throw new ArgumentException("经纬度无效。");
        _ = Zone;
        if (Start >= End) throw new ArgumentException("结束时间必须晚于开始时间。");
        if (ProbabilityThreshold is < 1 or > 100 || !Compatibility.IsFinite(PrecipitationThreshold) || PrecipitationThreshold <= 0)
            throw new ArgumentException("降水阈值无效。");
        if (ExcludedDates is null) throw new ArgumentException("排除日期不能为空值。");
        foreach (var date in ExcludedDates) _ = NormalizeDate(date);
        return this with { ExcludedDates = ExcludedDates.Select(NormalizeDate).Distinct(StringComparer.Ordinal).ToArray() };
    }
}

public sealed record Outlook(bool Wet, DateTimeOffset? FirstIntervalEnd, int MaxProbability, double MaxPrecipitation);
public sealed record ServiceStatus(DateTimeOffset CheckedAt, string ConfigId, string LocalDate, Outlook? Outlook, string? Error, bool IsHoliday = false);
public sealed record UserState(string? NotifiedDate = null);

public sealed record Location
{
    public string Name { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string TimeZone { get; init; } = "";
    public string Country { get; init; } = "";
    public string Region { get; init; } = "";
    public static string FormatLabel(params string[] parts) => string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal));
    public override string ToString() => FormatLabel(Name, Region, Country);
}

public sealed record GeocodingReply(Location[]? Results);
public sealed record ForecastReply(HourlyData? Hourly);
public sealed record HourlyData(
    long[]? Time,
    double?[]? Probability,
    double?[]? Precipitation);
