using System.Globalization;
using System.Text;

namespace RainReminder.Core;

public sealed record HolidayCache(DateTimeOffset UpdatedAt, string[] Dates)
{
    public bool Covers(int year) => Dates.Count(x => x.StartsWith(year + "-", StringComparison.Ordinal)) >= 16;
    public bool IsHoliday(string date) => Dates.Contains(date, StringComparer.Ordinal);
}

public static class JapanCalendar
{
    public const string Source = "https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv";

    public static HolidayCache Parse(byte[] csv, DateTimeOffset updatedAt)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(932).GetString(csv);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var dates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Trim().Split(',', 2);
            if (fields.Length != 2 || !DateOnly.TryParseExact(fields[0].Trim('"'), "yyyy/M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidDataException("内阁府假日 CSV 格式无效。");
            dates.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        if (dates.Count < 16) throw new InvalidDataException("假日日历数据不完整。");
        return new HolidayCache(updatedAt, dates.Order(StringComparer.Ordinal).ToArray());
    }
}
