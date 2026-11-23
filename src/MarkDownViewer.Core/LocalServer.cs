using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarkDownViewer.Core;

/// <summary>
/// Lokaler, nur an 127.0.0.1 gebundener Server. Liefert Programm-Assets und Dateien aus (nur lesend)
/// und rendert Markdown zu HTML. Zugriff nur mit zufälligem Token im Pfad; strikte CSP gegen Skripte aus Markdown.
/// </summary>
public sealed class LocalServer : IAsyncDisposable
{
    private static readonly FileExtensionContentTypeProvider Mime = new();
    private static readonly string[] ActiveContent = [".svg", ".svgz", ".html", ".htm", ".xhtml", ".xml"];

    private readonly WebApplication _app;
    private readonly MarkdownEngine _engine;

    public string Token { get; }
    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}/{Token}/";
    public string AssetBase => $"/{Token}/a/";

    private LocalServer(WebApplication app, MarkdownEngine engine, string token)
    {
        _app = app; _engine = engine; Token = token;
    }

    public static async Task<LocalServer> StartAsync(MarkdownEngine? engine = null)
    {
        engine ??= new MarkdownEngine();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var server = new LocalServer(app, engine, token);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server.Port = new Uri(address).Port;
        return server;
    }

    public string FileUrl(string path) => BaseUrl + FileUrls.ToUrlPath(path);

    public string DocUrl(string path, ViewMode mode = ViewMode.Normal, string? theme = null, double scrollY = 0, string? fragment = null)
    {
        var q = new List<string>();
        if (mode != ViewMode.Normal) q.Add("mode=" + (mode == ViewMode.PrintTree ? "tree" : "single"));
        if (theme is not null) q.Add("theme=" + theme);
        if (scrollY >= 1) q.Add("y=" + Math.Round(scrollY).ToString(CultureInfo.InvariantCulture));
        return FileUrl(path)
               + (q.Count > 0 ? "?" + string.Join('&', q) : "")
               + (string.IsNullOrEmpty(fragment) ? "" : "#" + Uri.EscapeDataString(fragment));
    }

    /// <summary>Erkennt, ob eine URL auf eine Datei dieses Servers zeigt.</summary>
    public bool TryParse(Uri uri, out string path, out ViewMode mode, out string? fragment)
    {
        path = ""; mode = ViewMode.Normal; fragment = null;
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port != Port) return false;
        var prefix = $"/{Token}/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (!FileUrls.TryFromUrlPath(uri.AbsolutePath[prefix.Length..], out path)) return false;
        if (uri.Query.Contains("mode=tree")) mode = ViewMode.PrintTree;
        else if (uri.Query.Contains("mode=single")) mode = ViewMode.PrintSingle;
        if (uri.Fragment.Length > 1) fragment = Uri.UnescapeDataString(uri.Fragment[1..]);
        return true;
    }

    private async Task HandleAsync(HttpContext ctx)
    {
        var res = ctx.Response;
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.Headers["Referrer-Policy"] = "no-referrer";

        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
        { res.StatusCode = 405; return; }
        if (ctx.Request.Host.Host != "127.0.0.1")
        { res.StatusCode = 403; return; }

        var p = ctx.Request.Path.ToUriComponent();
        var prefix = $"/{Token}/";
        if (!p.StartsWith(prefix, StringComparison.Ordinal))
        { res.StatusCode = 404; return; }
        var rest = p[prefix.Length..];

        if (rest.StartsWith("a/", StringComparison.Ordinal)) { await ServeAsset(ctx, Uri.UnescapeDataString(rest[2..])); return; }
        if (FileUrls.TryFromUrlPath(rest, out var file)) { await ServeFile(ctx, file); return; }
        res.StatusCode = 404;
    }

    private static async Task ServeAsset(HttpContext ctx, string name)
    {
        var stream = name.Contains('/') ? null : typeof(LocalServer).Assembly.GetManifestResourceStream("web/" + name);
        if (stream is null) { ctx.Response.StatusCode = 404; return; }
        await using (stream)
        {
            ctx.Response.ContentType = ContentTypeFor(name);
            ctx.Response.Headers.CacheControl = "private, max-age=3600";
            ctx.Response.ContentLength = stream.Length;
            if (HttpMethods.IsHead(ctx.Request.Method)) return;
            await stream.CopyToAsync(ctx.Response.Body);
        }
    }

    private async Task ServeFile(HttpContext ctx, string file)
    {
        var res = ctx.Response;
        if (!File.Exists(file)) { res.StatusCode = 404; res.ContentType = "text/plain; charset=utf-8"; await res.WriteAsync("Datei nicht gefunden"); return; }

        if (LinkResolver.IsMarkdown(file))
        {
            var q = ctx.Request.Query;
            var mode = q["mode"] == "tree" ? ViewMode.PrintTree : q["mode"] == "single" ? ViewMode.PrintSingle : ViewMode.Normal;
            var theme = q["theme"] == "dark" ? "dark" : "light";
            double.TryParse(q["y"], NumberStyles.Float, CultureInfo.InvariantCulture, out var y);
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            string html;
            try { html = _engine.RenderPage(file, new RenderOptions(mode, theme, y, AssetBase, nonce, FileUrl)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { res.StatusCode = 500; await res.WriteAsync("Datei nicht lesbar: " + ex.Message); return; }
            res.ContentType = "text/html; charset=utf-8";
            res.Headers.CacheControl = "no-store";
            res.Headers.ContentSecurityPolicy = MarkdownEngine.ContentSecurityPolicy(nonce);
            if (!HttpMethods.IsHead(ctx.Request.Method)) await res.WriteAsync(html);
            return;
        }

        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ActiveContent.Contains(ext))
            res.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; style-src 'unsafe-inline'; img-src data:";
        res.Headers.CacheControl = "no-store";
        await Results.File(file, ContentTypeFor(file), enableRangeProcessing: true).ExecuteAsync(ctx);
    }

    private static string ContentTypeFor(string name) =>
        Mime.TryGetContentType(name, out var t) ? t : "application/octet-stream";

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
