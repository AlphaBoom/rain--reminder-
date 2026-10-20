using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace System.Runtime.CompilerServices
{
    // Compiler-only marker for init properties and records. No runtime package.
    public sealed class IsExternalInit { }
}

namespace RainReminder.Core
{
    public static class Compatibility
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static readonly Lazy<Dictionary<string, string>> WindowsZones = new(() =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RainReminder.Core.windowsZones.xml")!;
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            return XDocument.Load(reader).Descendants("mapZone")
                .SelectMany(x => ((string)x.Attribute("type")!).Split(' ').Select(id => new { Id = id, Windows = (string)x.Attribute("other")! }))
                .GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Windows, StringComparer.Ordinal);
        });

        public static TimeZoneInfo FindZone(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException)
            {
                if (WindowsZones.Value.TryGetValue(id, out var windows)) return TimeZoneInfo.FindSystemTimeZoneById(windows);
                throw;
            }
        }
    }
}
