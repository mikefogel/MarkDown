using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkDownViewer.Core;

public sealed record RenderOptions(
    ViewMode Mode,
    string Theme,
    double ScrollY,
    string AssetBase,
    string Nonce,
    Func<string, string> FileUrl);

public sealed record OutlineItem(int Level, string Text, string Id);

public sealed record DocumentInfo(string Title, IReadOnlyList<OutlineItem> Outline);

public sealed partial class MarkdownEngine
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UseEmojiAndSmiley()
        .Build();

    // ---------- Analyse ----------

    public DocumentInfo Analyze(string path)
    {
        var doc = Parse(path);
        var outline = new List<OutlineItem>();
        foreach (var h in doc.Descendants<HeadingBlock>())
            outline.Add(new OutlineItem(h.Level, PlainText(h.Inline), h.GetAttributes().Id ?? ""));
        var title = outline.FirstOrDefault(o => o.Level == 1)?.Text;
        return new DocumentInfo(string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title, outline);
    }

    public string GetTitle(string path)
    {
        try { return Analyze(path).Title; }
        catch { return Path.GetFileNameWithoutExtension(path); }
    }

    /// <summary>Alle existierenden Markdown-Dateien, auf die das Dokument verlinkt (Reihenfolge, ohne Duplikate).</summary>
    public IReadOnlyList<string> GetLinkedMarkdownFiles(string path)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(LinkResolver.PathComparer) { Path.GetFullPath(path) };
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            foreach (var link in Parse(path).Descendants<LinkInline>().Where(l => !l.IsImage))
            {
                var r = LinkResolver.Resolve(link.Url, dir);
                if (r.Kind == LinkKind.Markdown && r.Path is not null && File.Exists(r.Path) && seen.Add(r.Path))
                    result.Add(r.Path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    // ---------- Rendering ----------

    public string RenderPage(string path, RenderOptions o)
    {
        path = Path.GetFullPath(path);
        // Druckansichten sind immer hell (weißer Grund, keine schwarzen Seiten), unabhängig vom gewählten Theme
        if (o.Mode != ViewMode.Normal) o = o with { Theme = "light" };
        string body, title;
        if (o.Mode == ViewMode.PrintTree)
        {
            var root = DocumentTree.Build(path, this);
            var docs = DocumentTree.Flatten(root);
            body = RenderTree(docs, o);
            title = root.Title;
        }
        else
        {
            body = RenderBody(path, o, prefix: "", included: null);
            title = GetTitle(path);
        }
        return Template(body, title, o);
    }

    private string RenderTree(IReadOnlyList<DocNode> docs, RenderOptions o)
    {
        var index = new Dictionary<string, int>(LinkResolver.PathComparer);
        for (var i = 0; i < docs.Count; i++) index[docs[i].Path] = i;
        var rootDir = Path.GetDirectoryName(docs[0].Path)!;

        var sb = new StringBuilder();
        sb.Append("<header class=\"print-cover\"><h1>").Append(Enc(docs[0].Title)).Append("</h1>")
          .Append("<p class=\"print-meta\">").Append(docs.Count).Append(docs.Count == 1 ? " Dokument" : " Dokumente")
          .Append(" · ").Append(DateTime.Now.ToString("d", CultureInfo.GetCultureInfo("de-DE"))).Append("</p><nav class=\"print-toc\"><ol>");
        for (var i = 0; i < docs.Count; i++)
            sb.Append("<li style=\"margin-left:").Append(docs[i].Depth * 1.2).Append("em\"><a href=\"#d").Append(i).Append("\">")
              .Append(Enc(docs[i].Title)).Append("</a></li>");
        sb.Append("</ol></nav></header>");

        for (var i = 0; i < docs.Count; i++)
        {
            string rel;
            try { rel = Path.GetRelativePath(rootDir, docs[i].Path).Replace('\\', '/'); } catch { rel = docs[i].Path; }
            sb.Append("<section class=\"doc-section\" id=\"d").Append(i).Append("\"><div class=\"doc-path\">")
              .Append(Enc(rel)).Append("</div>")
              .Append(RenderBody(docs[i].Path, o, $"d{i}-", index))
              .Append("</section>");
        }
        return sb.ToString();
    }

    private string RenderBody(string path, RenderOptions o, string prefix, Dictionary<string, int>? included)
    {
        var doc = Parse(path);
        var dir = Path.GetDirectoryName(path)!;

        if (prefix.Length > 0)
            foreach (var h in doc.Descendants<HeadingBlock>())
            {
                var attrs = h.GetAttributes();
                if (!string.IsNullOrEmpty(attrs.Id)) attrs.Id = prefix + attrs.Id;
            }

        foreach (var link in doc.Descendants<LinkInline>().Where(l => !l.IsImage))
        {
            var r = LinkResolver.Resolve(link.Url, dir);
            var attrs = link.GetAttributes();
            switch (r.Kind)
            {
                case LinkKind.Anchor:
                    if (prefix.Length > 0 && r.Fragment is not null) link.Url = "#" + prefix + Uri.EscapeDataString(r.Fragment);
                    break;
                case LinkKind.External:
                    attrs.AddProperty("data-target", link.Url ?? "");
                    break;
                default:
                    if (r.Path is null) break;
                    if (!File.Exists(r.Path) && !Directory.Exists(r.Path))
                    {
                        attrs.AddClass("broken-link");
                        attrs.AddProperty("title", "Datei nicht gefunden");
                        attrs.AddProperty("data-target", "nicht gefunden: " + link.Url);
                    }
                    else if (included is not null && r.Kind == LinkKind.Markdown && included.TryGetValue(r.Path, out var idx))
                    {
                        link.Url = $"#d{idx}" + (r.Fragment is null ? "" : "-" + Uri.EscapeDataString(r.Fragment));
                        attrs.AddProperty("data-target", $"siehe Abschnitt {idx + 1}");
                    }
                    else
                    {
                        attrs.AddProperty("data-target", Path.GetRelativePath(dir, r.Path).Replace('\\', '/'));
                        if (included is not null || IsRootRelative(link.Url))
                            link.Url = o.FileUrl(r.Path) + (r.Fragment is null ? "" : "#" + Uri.EscapeDataString(r.Fragment));
                    }
                    break;
            }
        }

        using var sw = new StringWriter();
        var renderer = new HtmlRenderer(sw);
        Pipeline.Setup(renderer);
        renderer.Render(doc);
        var html = sw.ToString();

        html = SrcRegex().Replace(html, m =>
        {
            var src = WebUtility.HtmlDecode(m.Groups[1].Value);
            if (included is null && !IsRootRelative(src)) return m.Value; // relative Pfade löst der Browser selbst auf
            var r = LinkResolver.Resolve(src, dir);
            return r.Path is not null && r.Kind != LinkKind.External ? $"src=\"{o.FileUrl(r.Path)}\"" : m.Value;
        });
        return html;
    }

    private static MarkdownDocument Parse(string path) => Markdown.Parse(File.ReadAllText(path), Pipeline);

    private static string Template(string body, string title, RenderOptions o)
    {
        var mode = o.Mode switch { ViewMode.PrintSingle => "print-single", ViewMode.PrintTree => "print-tree", _ => "normal" };
        string Script(string f) => $"<script nonce=\"{o.Nonce}\" src=\"{o.AssetBase}{f}\"></script>";
        return $$"""
            <!doctype html>
            <html lang="de" data-theme="{{o.Theme}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{Enc(title)}}</title>
            <link rel="stylesheet" href="{{o.AssetBase}}app.css">
            <link rel="stylesheet" href="{{o.AssetBase}}katex.min.css">
            <link rel="stylesheet" href="{{o.AssetBase}}hljs-{{o.Theme}}.css">
            </head>
            <body data-mode="{{mode}}" data-y="{{o.ScrollY.ToString("0", CultureInfo.InvariantCulture)}}">
            <main class="markdown-body">
            {{body}}
            </main>
            {{Script("highlight.min.js")}}
            {{Script("katex.min.js")}}
            {{Script("auto-render.min.js")}}
            {{Script("mermaid.min.js")}}
            {{Script("app.js")}}
            </body>
            </html>
            """;
    }

    public static string ContentSecurityPolicy(string nonce) =>
        $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: http: https:; font-src 'self'; media-src 'self' http: https:; " +
        "connect-src 'none'; frame-src 'none'; object-src 'none'; form-action 'none'; base-uri 'none'";

    private static bool IsRootRelative(string? url) =>
        url is { Length: > 0 } && (url[0] == '/' || url[0] == '\\') && !url.StartsWith("//");

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string PlainText(ContainerInline? inline)
    {
        if (inline is null) return "";
        var sb = new StringBuilder();
        foreach (var i in inline.Descendants())
        {
            if (i is LiteralInline l) sb.Append(l.Content.ToString());
            else if (i is CodeInline c) sb.Append(c.Content);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"src=""((?![A-Za-z][A-Za-z0-9+.\-]+:|//|#)[^""]*)""")]
    private static partial Regex SrcRegex();
}
