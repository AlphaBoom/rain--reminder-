using System.Globalization;


namespace RainReminder.Core;

public sealed record WeatherConfig
{
    public string Name { get; init; } = "";
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
    public TimeSpan Start => ParseTime(MorningStart);
    public TimeSpan End => ParseTime(MorningEnd);
    private static TimeSpan ParseTime(string value) => DateTime.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture).TimeOfDay;

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
        foreach (var date in ExcludedDates) _ = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return this;
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
    public override string ToString() => $"{Name} · {Region} · {Country}";
}

public sealed record GeocodingReply(Location[]? Results);
public sealed record ForecastReply(HourlyData? Hourly);
public sealed record HourlyData(
    long[]? Time,
    double?[]? Probability,
    double?[]? Precipitation);
