namespace MarkDownViewer.Core;

public sealed class DocNode
{
    public required string Path { get; init; }
    public required string Title { get; init; }
    public int Depth { get; init; }
    public List<DocNode> Children { get; } = [];
}

public static class DocumentTree
{
    /// <summary>
    /// Baut den Dokumentenbaum ab <paramref name="rootPath"/>. Breitensuche: Jedes Dokument hängt unter dem
    /// flachsten Dokument, das es verlinkt. Rück- und Mehrfachverweise (Zyklen) erzeugen keine Dubletten.
    /// </summary>
    public static DocNode Build(string rootPath, MarkdownEngine engine, int maxNodes = 2000, int maxDepth = 50)
    {
        rootPath = Path.GetFullPath(rootPath);
        var claimed = new HashSet<string>(LinkResolver.PathComparer) { rootPath };
        var root = new DocNode { Path = rootPath, Title = engine.GetTitle(rootPath), Depth = 0 };
        var queue = new Queue<DocNode>();
        queue.Enqueue(root);
        var count = 1;
        while (queue.Count > 0 && count < maxNodes)
        {
            var node = queue.Dequeue();
            if (node.Depth >= maxDepth) continue;
            foreach (var child in engine.GetLinkedMarkdownFiles(node.Path))
            {
                if (count >= maxNodes) break;
                if (!claimed.Add(child)) continue;
                var c = new DocNode { Path = child, Title = engine.GetTitle(child), Depth = node.Depth + 1 };
                node.Children.Add(c);
                queue.Enqueue(c);
                count++;
            }
        }
        return root;
    }

    /// <summary>Alle Dokumente in Lesereihenfolge (Tiefendurchlauf des Baums), jedes genau einmal.</summary>
    public static IReadOnlyList<DocNode> Flatten(DocNode root)
    {
        var list = new List<DocNode>();
        void Walk(DocNode n) { list.Add(n); foreach (var c in n.Children) Walk(c); }
        Walk(root);
        return list;
    }
}
