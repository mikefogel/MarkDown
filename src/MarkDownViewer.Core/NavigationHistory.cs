namespace MarkDownViewer.Core;

public enum ViewMode { Normal, PrintSingle, PrintTree }

public sealed record HistoryEntry(string Path, ViewMode Mode = ViewMode.Normal, string? Fragment = null, double ScrollY = 0);

/// <summary>Browser-artiger Verlauf mit gemerkter Scroll-Position je Eintrag.</summary>
public sealed class NavigationHistory
{
    private readonly List<HistoryEntry> _back = [];
    private readonly List<HistoryEntry> _forward = [];

    public HistoryEntry? Current { get; private set; }
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void Navigate(HistoryEntry entry)
    {
        if (Current is not null) _back.Add(Current);
        _forward.Clear();
        Current = entry;
    }

    public HistoryEntry? Back()
    {
        if (!CanGoBack) return null;
        if (Current is not null) _forward.Add(Current);
        Current = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        return Current;
    }

    public HistoryEntry? Forward()
    {
        if (!CanGoForward) return null;
        if (Current is not null) _back.Add(Current);
        Current = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        return Current;
    }

    public void UpdateScroll(double y)
    {
        if (Current is not null) Current = Current with { ScrollY = y };
    }
}
