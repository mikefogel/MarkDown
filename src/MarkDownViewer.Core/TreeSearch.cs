namespace MarkDownViewer.Core;

public sealed record SearchHit(string Path, int Line, string Snippet);

public static class TreeSearch
{
    public static IReadOnlyList<SearchHit> Search(IEnumerable<string> files, string query, int max = 500)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(query)) return hits;
        foreach (var file in files)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new SearchHit(file, i + 1, lines[i].Trim()));
                    if (hits.Count >= max) return hits;
                }
            }
        }
        return hits;
    }
}
