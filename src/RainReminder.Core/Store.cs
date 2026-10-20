using System.Collections;
using System.Globalization;
using System.Web.Script.Serialization;

namespace RainReminder.Core;

public static class Store
{
    public static string SharedRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RainReminder");
    public static string ConfigPath => Path.Combine(SharedRoot, "config", "settings.json");
    public static string StatusPath => Path.Combine(SharedRoot, "status", "forecast.json");
    public static string UserRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RainReminder");
    public static string UserStatePath => Path.Combine(UserRoot, "state.json");
    private static Dictionary<string, object?> Map(params object?[] pairs)
    {
        var result = new Dictionary<string, object?>();
        for (var i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i]!, pairs[i + 1]);
        return result;
    }
    // Explicit field maps retain the old camelCase JSON and ISO timestamps.
    private static object ToJson(object value) => value switch
    {
        WeatherConfig c => Map("name", c.Name, "latitude", c.Latitude, "longitude", c.Longitude, "timeZone", c.TimeZone,
            "morningStart", c.MorningStart, "morningEnd", c.MorningEnd, "probabilityThreshold", c.ProbabilityThreshold,
            "precipitationThreshold", c.PrecipitationThreshold, "excludedDates", c.ExcludedDates, "showTrayIcon", c.ShowTrayIcon),
        Outlook o => Map("wet", o.Wet, "firstIntervalEnd", o.FirstIntervalEnd?.ToString("O"), "maxProbability", o.MaxProbability, "maxPrecipitation", o.MaxPrecipitation),
        ServiceStatus s => Map("checkedAt", s.CheckedAt.ToString("O"), "configId", s.ConfigId, "localDate", s.LocalDate,
            "outlook", s.Outlook is null ? null : ToJson(s.Outlook), "error", s.Error, "isHoliday", s.IsHoliday),
        UserState u => Map("notifiedDate", u.NotifiedDate),
        HolidayCache h => Map("updatedAt", h.UpdatedAt.ToString("O"), "dates", h.Dates),
        _ => throw new InvalidDataException("Unsupported stored data type.")
    };
    public static string Serialize<T>(T value) => new JavaScriptSerializer().Serialize(ToJson(value!));
    public static Dictionary<string, object> ParseObject(string json) => new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
    public static object? Value(IDictionary<string, object> map, string key) => map.TryGetValue(key, out var value) ? value : null;
    public static string Text(IDictionary<string, object> map, string key, string fallback = "") => Value(map, key) as string ?? fallback;
    public static double Number(IDictionary<string, object> map, string key, double fallback = 0) => Value(map, key) is { } value ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : fallback;
    public static bool Flag(IDictionary<string, object> map, string key) => Value(map, key) is true;
    public static object?[] ArrayValue(object? value) => value is IEnumerable items && value is not string ? items.Cast<object?>().ToArray() : [];

    public static T? Read<T>(string path) where T : class
    {
        string json;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        var d = ParseObject(json);
        object result;
        if (typeof(T) == typeof(WeatherConfig))
            result = new WeatherConfig
            {
                Name = Text(d, "name"), Latitude = Number(d, "latitude"), Longitude = Number(d, "longitude"),
                TimeZone = Text(d, "timeZone", "Asia/Tokyo"), MorningStart = Text(d, "morningStart", "06:00"), MorningEnd = Text(d, "morningEnd", "10:00"),
                ProbabilityThreshold = (int)Number(d, "probabilityThreshold", 40), PrecipitationThreshold = Number(d, "precipitationThreshold", 0.1),
                ExcludedDates = ArrayValue(Value(d, "excludedDates")).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "").ToArray(),
                ShowTrayIcon = Flag(d, "showTrayIcon")
            };
        else if (typeof(T) == typeof(ServiceStatus))
        {
            Outlook? outlook = null;
            if (Value(d, "outlook") is Dictionary<string, object> o)
                outlook = new Outlook(Flag(o, "wet"), Value(o, "firstIntervalEnd") is string stamp ? DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture) : null,
                    (int)Number(o, "maxProbability"), Number(o, "maxPrecipitation"));
            result = new ServiceStatus(DateTimeOffset.Parse(Text(d, "checkedAt"), CultureInfo.InvariantCulture), Text(d, "configId"), Text(d, "localDate"), outlook, Value(d, "error") as string, Flag(d, "isHoliday"));
        }
        else if (typeof(T) == typeof(UserState)) result = new UserState(Value(d, "notifiedDate") as string);
        else if (typeof(T) == typeof(HolidayCache))
            result = new HolidayCache(DateTimeOffset.Parse(Text(d, "updatedAt"), CultureInfo.InvariantCulture), ArrayValue(Value(d, "dates")).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "").ToArray());
        else throw new InvalidDataException("Unsupported stored data type.");
        return (T)result;
    }
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, Serialize(value));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Log(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 256_000)
            {
                if (File.Exists(path + ".previous")) File.Delete(path + ".previous");
                File.Move(path, path + ".previous");
            }
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

