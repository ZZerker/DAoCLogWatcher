using System.Text.RegularExpressions;

namespace DAoCLogWatcher.Core;

public static class CharacterDiscoveryService
{
    private static readonly Regex ServerSuffixRegex = new(@"-\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> GetCharacterNames() => GetCharacterNames(GetEdenPath());

    public static IReadOnlyList<string> GetCharacterNames(string edenPath)
    {
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
