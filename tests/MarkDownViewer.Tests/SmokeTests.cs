global using Xunit;
using Markdig;

namespace MarkDownViewer.Tests;

public class SmokeTests
{
    [Fact]
    public void Markdig_renders_heading() =>
        Assert.Contains("<h1", Markdown.ToHtml("# Hallo"));
}
