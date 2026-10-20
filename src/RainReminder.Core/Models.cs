using System.Globalization;
using System.Text.Json.Serialization;

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

    [JsonIgnore] public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
    [JsonIgnore] public TimeOnly Start => ParseTime(MorningStart);
    [JsonIgnore] public TimeOnly End => ParseTime(MorningEnd);
    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    public WeatherConfig Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("请选择地点。");
        if (!double.IsFinite(Latitude) || Latitude is < -90 or > 90 ||
            !double.IsFinite(Longitude) || Longitude is < -180 or > 180)
            throw new ArgumentException("经纬度无效。");
        _ = Zone;
        if (Start >= End) throw new ArgumentException("结束时间必须晚于开始时间。");
        if (ProbabilityThreshold is < 1 or > 100 || !double.IsFinite(PrecipitationThreshold) || PrecipitationThreshold <= 0)
            throw new ArgumentException("降水阈值无效。");
        if (ExcludedDates is null) throw new ArgumentException("排除日期不能为空值。");
        foreach (var date in ExcludedDates) _ = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return this;
    }
}

public sealed record Outlook(bool Wet, DateTimeOffset? FirstIntervalEnd, int MaxProbability, double MaxPrecipitation);
public sealed record ServiceStatus(DateTimeOffset CheckedAt, string ConfigId, string LocalDate, Outlook? Outlook, string? Error, bool IsHoliday = false);
public sealed record UserState(string? NotifiedDate = null);

public sealed record Location
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
    [JsonPropertyName("timezone")] public string TimeZone { get; init; } = "";
    [JsonPropertyName("country")] public string Country { get; init; } = "";
    [JsonPropertyName("admin1")] public string Region { get; init; } = "";
    public override string ToString() => $"{Name} · {Region} · {Country}";
}

public sealed record GeocodingReply([property: JsonPropertyName("results")] Location[]? Results);
public sealed record ForecastReply([property: JsonPropertyName("hourly")] HourlyData? Hourly);
public sealed record HourlyData(
    [property: JsonPropertyName("time")] long[]? Time,
    [property: JsonPropertyName("precipitation_probability")] double?[]? Probability,
    [property: JsonPropertyName("precipitation")] double?[]? Precipitation);
