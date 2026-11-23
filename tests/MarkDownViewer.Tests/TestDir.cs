namespace MarkDownViewer.Tests;

public sealed class TestDir : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("mdv-").FullName;

    public string Write(string rel, string content)
    {
        var p = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
}
