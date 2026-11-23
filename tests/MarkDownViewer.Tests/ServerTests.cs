using System.Net;
using MarkDownViewer.Core;

namespace MarkDownViewer.Tests;

public class ServerTests : IAsyncLifetime
{
    private readonly TestDir _dir = new();
    private LocalServer _server = null!;
    private readonly HttpClient _http = new();

    public async Task InitializeAsync() => _server = await LocalServer.StartAsync();
    public async Task DisposeAsync() { _http.Dispose(); await _server.DisposeAsync(); _dir.Dispose(); }

    [Fact]
    public async Task Serves_markdown_with_strict_csp_and_assets_and_files()
    {
        var md = _dir.Write("doc.md", "# Hallo\n<script>alert(1)</script>\n![](bild.svg)");
        _dir.Write("bild.svg", "<svg xmlns='http://www.w3.org/2000/svg'/>");

        var res = await _http.GetAsync(_server.DocUrl(md, theme: "dark"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var csp = string.Join(";", res.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("connect-src 'none'", csp);
        Assert.DoesNotContain("unsafe-inline'; img", csp.Split("style-src")[0]);
        Assert.Contains("data-theme=\"dark\"", await res.Content.ReadAsStringAsync());

        var js = await _http.GetAsync(_server.BaseUrl + "a/app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);

        var svg = await _http.GetAsync(_server.FileUrl(Path.Combine(_dir.Root, "bild.svg")));
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType?.MediaType);
        Assert.Contains("sandbox", string.Join(";", svg.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Rejects_requests_without_valid_token()
    {
        var md = _dir.Write("doc.md", "# x");
        var wrong = _server.BaseUrl.Replace(_server.Token, new string('0', 32)) + FileUrls.ToUrlPath(md);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(wrong)).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, _server.FileUrl(md));
        req.Headers.Host = "evil.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public void Parses_own_urls_and_ignores_foreign_ones()
    {
        var md = _dir.Write("kapitel eins.md", "# x");
        var url = new Uri(_server.DocUrl(md, ViewMode.PrintTree, "dark", 250, "abschnitt"));
        Assert.True(_server.TryParse(url, out var path, out var mode, out var frag));
        Assert.Equal(md, path);
        Assert.Equal(ViewMode.PrintTree, mode);
        Assert.Equal("abschnitt", frag);
        Assert.False(_server.TryParse(new Uri("https://example.org/x.md"), out _, out _, out _));
    }
}
