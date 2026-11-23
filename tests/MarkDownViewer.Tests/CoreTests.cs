using MarkDownViewer.Core;

namespace MarkDownViewer.Tests;

public class FileUrlTests
{
    [Fact]
    public void Roundtrip_with_spaces_umlauts_and_hash()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mein Ordner", "Käse #1 & mehr.md"));
        var url = FileUrls.ToUrlPath(path);
        Assert.DoesNotContain(' ', url);
        Assert.DoesNotContain('#', url);
        Assert.True(FileUrls.TryFromUrlPath(url, out var back));
        Assert.Equal(path, back);
    }

    [Fact]
    public void Rejects_traversal_segments() =>
        Assert.False(FileUrls.TryFromUrlPath("f/a/../b", out _));
}

public class LinkResolverTests
{
    private static readonly string Dir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "docs"));

    [Theory]
    [InlineData("#abschnitt", LinkKind.Anchor)]
    [InlineData("kapitel/a.md", LinkKind.Markdown)]
    [InlineData("kapitel/a.md#x", LinkKind.Markdown)]
    [InlineData("bild.png", LinkKind.LocalFile)]
    [InlineData("handbuch.pdf", LinkKind.LocalFile)]
    [InlineData("https://example.org/a.md", LinkKind.External)]
    [InlineData("mailto:a@b.de", LinkKind.External)]
    public void Classifies(string href, LinkKind kind) =>
        Assert.Equal(kind, LinkResolver.Resolve(href, Dir).Kind);

    [Fact]
    public void Resolves_relative_parent_and_decodes()
    {
        var r = LinkResolver.Resolve("../x/Mein%20Kapitel.md#teil-2", Path.Combine(Dir, "sub"));
        Assert.Equal(Path.GetFullPath(Path.Combine(Dir, "x", "Mein Kapitel.md")), r.Path);
        Assert.Equal("teil-2", r.Fragment);
    }
}

public class NavigationHistoryTests
{
    [Fact]
    public void Back_and_forward_restore_scroll_positions()
    {
        var h = new NavigationHistory();
        h.Navigate(new HistoryEntry("/a.md"));
        h.UpdateScroll(120);
        h.Navigate(new HistoryEntry("/b.md"));
        h.UpdateScroll(300);

        var back = h.Back();
        Assert.Equal("/a.md", back!.Path);
        Assert.Equal(120, back.ScrollY);
        Assert.True(h.CanGoForward);

        var fwd = h.Forward();
        Assert.Equal("/b.md", fwd!.Path);
        Assert.Equal(300, fwd.ScrollY);
    }

    [Fact]
    public void New_navigation_clears_forward_stack()
    {
        var h = new NavigationHistory();
        h.Navigate(new HistoryEntry("/a.md"));
        h.Navigate(new HistoryEntry("/b.md"));
        h.Back();
        h.Navigate(new HistoryEntry("/c.md"));
        Assert.False(h.CanGoForward);
        Assert.Equal("/a.md", h.Back()!.Path);
        Assert.Null(h.Back());
    }
}

public class DocumentTreeTests
{
    [Fact]
    public void Builds_tree_from_current_file_and_survives_cycles()
    {
        using var d = new TestDir();
        var index = d.Write("index.md", "# Index\n[A](a.md)\n[B](sub/b.md)\n[Kaputt](nix.md)\n[Web](https://x.org)");
        d.Write("a.md", "# Alpha\n[zurück](index.md)\n[B](sub/b.md)");
        d.Write("sub/b.md", "# Beta\n[A](../a.md)");

        var engine = new MarkdownEngine();
        var root = DocumentTree.Build(index, engine);
        Assert.Equal("Index", root.Title);
        Assert.Equal(["Alpha", "Beta"], root.Children.Select(c => c.Title));

        var flat = DocumentTree.Flatten(root);
        Assert.Equal(["Index", "Alpha", "Beta"], flat.Select(n => n.Title));

        // Wurzel mitten im Baum: Baum entsteht ab der aktuellen Datei
        var mid = DocumentTree.Build(Path.Combine(d.Root, "a.md"), engine);
        Assert.Equal("Alpha", mid.Title);
        Assert.Equal(["Alpha", "Index", "Beta"], DocumentTree.Flatten(mid).Select(n => n.Title));
    }
}

public class TreeSearchTests
{
    [Fact]
    public void Finds_case_insensitive_with_line_numbers()
    {
        using var d = new TestDir();
        var f = d.Write("x.md", "eins\nZwei Äpfel\ndrei");
        var hits = TreeSearch.Search([f], "äpfel");
        var hit = Assert.Single(hits);
        Assert.Equal(2, hit.Line);
    }
}

public class EngineTests
{
    [Fact]
    public void Renders_mermaid_math_and_marks_broken_links()
    {
        using var d = new TestDir();
        var f = d.Write("a.md", "# T\n\n```mermaid\ngraph TD; A-->B\n```\n\n$x^2$\n\n[weg](fehlt.md) [da](a.md#t)");
        var html = new MarkdownEngine().RenderPage(f, new RenderOptions(ViewMode.Normal, "light", 0, "/t/a/", "N", p => "u" + p));
        Assert.Contains("class=\"mermaid\"", html);
        Assert.Contains("class=\"math\"", html);
        Assert.Contains("broken-link", html);
        Assert.Contains("<h1 id=\"t\"", html);
        Assert.Contains("nonce=\"N\"", html);
    }

    [Fact]
    public void Tree_print_includes_every_doc_once_and_rewrites_links()
    {
        using var d = new TestDir();
        var f = d.Write("index.md", "# Index\n[A](a.md)\n\n![](img/x.png)");
        d.Write("a.md", "# Alpha\n[zurück](index.md#index)");
        var html = new MarkdownEngine().RenderPage(f, new RenderOptions(ViewMode.PrintTree, "light", 0, "/t/a/", "N", p => "http://h/" + Path.GetFileName(p)));
        Assert.Contains("id=\"d0\"", html);
        Assert.Contains("id=\"d1\"", html);
        Assert.DoesNotContain("id=\"d2\"", html);
        Assert.Contains("href=\"#d1\"", html);
        Assert.Contains("href=\"#d0-index\"", html);
        Assert.Contains("id=\"d0-index\"", html);
        Assert.Contains("src=\"http://h/x.png\"", html);
    }
}

public class RootRelativeTests
{
    [Fact]
    public void Absolute_paths_in_links_and_images_become_file_urls()
    {
        using var d = new TestDir();
        var target = d.Write("ziel.md", "# Ziel");
        var img = d.Write("b/bild.png", "x");
        // Wurzel-relativ ("/pfad/datei.md"); unter Windows ohne Laufwerksbuchstaben (gleiches Laufwerk)
        static string RootRel(string path)
        {
            var p = path.Replace('\\', '/');
            return p.Length > 1 && p[1] == ':' ? p[2..] : p;
        }
        var f = d.Write("a.md", $"[abs]({RootRel(target)})\n\n![]({RootRel(img)})\n\n![rel](b/bild.png)");
        var html = new MarkdownEngine().RenderPage(f, new RenderOptions(ViewMode.Normal, "light", 0, "/t/a/", "N", p => "http://h/" + Path.GetFileName(p)));
        Assert.Contains("href=\"http://h/ziel.md\"", html);
        Assert.Contains("src=\"http://h/bild.png\"", html);
        Assert.Contains("src=\"b/bild.png\"", html);
    }
}
