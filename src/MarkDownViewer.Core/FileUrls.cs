using System.Text.RegularExpressions;

namespace MarkDownViewer.Core;

/// <summary>Bildet lokale Dateipfade auf URL-Pfade ("f/seg/seg") ab und zurück.</summary>
public static partial class FileUrls
{
    public static string ToUrlPath(string fullPath)
    {
        var segments = fullPath.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "f/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    /// <param name="escapedUrlPath">Pfad ohne Token-Präfix, beginnend mit "f/", noch URL-kodiert.</param>
    public static bool TryFromUrlPath(string escapedUrlPath, out string path)
    {
        path = "";
        if (!escapedUrlPath.StartsWith("f/", StringComparison.Ordinal)) return false;
        var segments = escapedUrlPath[2..].Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || s.Contains('\0'))) return false;
        path = DriveRegex().IsMatch(segments[0])
            ? string.Join('/', segments)
            : "/" + string.Join('/', segments);
        path = Path.GetFullPath(path);
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z]:$")]
    private static partial Regex DriveRegex();
}
