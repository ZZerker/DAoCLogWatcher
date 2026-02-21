using System.Text.RegularExpressions;

namespace DAoCLogWatcher.Core;

public static class CharacterDiscoveryService
{
    private static readonly Regex ServerSuffixRegex = new(@"-\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns deduplicated character names found in the DAoC eden profile directory.
    /// Files are named e.g. "Barnabas-41.ini" or "Barnabas-42.ini"; the suffix is stripped.
    /// </summary>
    public static IReadOnlyList<string> GetCharacterNames()
    {
        var edenPath = GetEdenPath();

        if (!Directory.Exists(edenPath))
            return [];

        return Directory
            .EnumerateFiles(edenPath, "*.ini")
            .Select(f => ServerSuffixRegex.Replace(Path.GetFileNameWithoutExtension(f), string.Empty))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetEdenPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Electronic Arts", "Dark Age of Camelot", "eden");
    }
}
