using System.Text.RegularExpressions;

namespace MarkDownViewer.Core;

public enum LinkKind { Anchor, Markdown, LocalFile, External }

public sealed record ResolvedLink(LinkKind Kind, string? Path, string? Fragment);

public static partial class LinkResolver
{
    public static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd"];

    public static bool IsMarkdown(string path) =>
        MarkdownExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static ResolvedLink Resolve(string? href, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(href)) return new(LinkKind.Anchor, null, null);
        href = href.Trim();
        if (href[0] == '#') return new(LinkKind.Anchor, null, Unescape(href[1..]));

        if (SchemeRegex().IsMatch(href))
        {
            if (href.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(href, UriKind.Absolute, out var u))
                return FromPath(u.LocalPath, Unescape(u.Fragment.TrimStart('#')));
            return new(LinkKind.External, null, null);
        }

        string? fragment = null;
        var hash = href.IndexOf('#');
        if (hash >= 0) { fragment = Unescape(href[(hash + 1)..]); href = href[..hash]; }
        var q = href.IndexOf('?');
        if (q >= 0) href = href[..q];
        if (href.Length == 0) return new(LinkKind.Anchor, null, fragment);

        var full = Path.GetFullPath(Unescape(href), baseDirectory);
        return FromPath(full, fragment);
    }

    private static ResolvedLink FromPath(string path, string? fragment) =>
        new(IsMarkdown(path) ? LinkKind.Markdown : LinkKind.LocalFile, Path.GetFullPath(path),
            string.IsNullOrEmpty(fragment) ? null : fragment);

    private static string Unescape(string s)
    {
        try { return Uri.UnescapeDataString(s); } catch { return s; }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]+:")]
    private static partial Regex SchemeRegex();
}
