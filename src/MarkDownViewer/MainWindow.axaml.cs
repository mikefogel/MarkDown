using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using MarkDownViewer.Core;

namespace MarkDownViewer;

public partial class MainWindow : Window
{
    private readonly MarkdownEngine _engine = new();
    private readonly NavigationHistory _history = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly string? _startupPath;

    private LocalServer? _server;
    private string _theme;
    private string? _allowedUrl;
    private double _zoom = 1.0;
    private bool _pendingPrint;
    private string? _pendingFind;
    private bool _filling;
    private IReadOnlyList<DocNode> _treeDocs = [];
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _reloadCts;
    private int _sidebarRefresh;

    public MainWindow() : this(null) { }

    partial void StartDebugHooks();

    public MainWindow(string? startupPath)
    {
        InitializeComponent();
        _startupPath = startupPath;

        _theme = _settings.Theme ?? (ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light");
        ApplyAppTheme();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);

        Web.NavigationStarted += OnNavigationStarted;
        Web.NavigationCompleted += (_, _) => Dispatcher.UIThread.Post(async () => await OnNavigationCompletedAsync());
        Web.NewWindowRequested += OnNewWindowRequested;

        RefreshRecent();
        Opened += async (_, _) =>
        {
            _server = await LocalServer.StartAsync(_engine);
            StartDebugHooks();
            if (_startupPath is not null && File.Exists(_startupPath))
                await OpenFileAsync(_startupPath);
        };
        Closed += async (_, _) =>
        {
            _watcher?.Dispose();
            if (_server is not null) await _server.DisposeAsync();
        };
    }

    // ---------- Öffnen & Navigation ----------

    private async Task OpenFileAsync(string path, string? fragment = null)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return;
        if (!LinkResolver.IsMarkdown(path)) { ExternalOpener.Open(path); return; }
        await NavigateToAsync(new HistoryEntry(path, ViewMode.Normal, fragment));
    }

    private async Task NavigateToAsync(HistoryEntry entry)
    {
        if (_server is null) return;
        await CaptureScrollAsync();
        _history.Navigate(entry);
        Load(_history.Current!);
    }

    private async void OnBack(object? sender, RoutedEventArgs e) => await GoAsync(back: true);
    private async void OnForward(object? sender, RoutedEventArgs e) => await GoAsync(back: false);

    private async Task GoAsync(bool back)
    {
        if (_server is null) return;
        if (!(back ? _history.CanGoBack : _history.CanGoForward)) return;
        await CaptureScrollAsync();
        var entry = back ? _history.Back() : _history.Forward();
        if (entry is not null) Load(entry);
    }

    private void Load(HistoryEntry entry)
    {
        if (_server is null) return;
        if (!File.Exists(entry.Path))
        {
            PathText.Text = "Datei nicht gefunden: " + entry.Path;
            return;
        }

        var url = _server.DocUrl(entry.Path, entry.Mode, _theme, entry.ScrollY, entry.Fragment);
        _allowedUrl = url;
        StartScreen.IsVisible = false;
        Web.IsVisible = true;
        Web.Navigate(new Uri(url));

        PathText.Text = entry.Path + (entry.Mode switch
        {
            ViewMode.PrintSingle => "   —   Druckansicht",
            ViewMode.PrintTree => "   —   Druckansicht (ganzer Baum)",
            _ => ""
        });
        Title = Path.GetFileName(entry.Path) + " – MarkDown Viewer";
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;

        WatchFile(entry.Path);
        if (entry.Mode == ViewMode.Normal)
        {
            _settings.AddRecent(entry.Path);
            RefreshRecent();
            _ = RefreshSidebarAsync(entry.Path);
        }
    }

    private async Task CaptureScrollAsync()
    {
        if (!Web.IsVisible || _history.Current is null) return;
        try
        {
            var raw = await Web.InvokeScript("String(window.scrollY)");
            if (double.TryParse(raw?.Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                _history.UpdateScroll(y);
        }
        catch { /* Seite evtl. noch nicht geladen */ }
    }

    // ---------- WebView-Ereignisse ----------

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        var uri = e.Request;
        if (uri is null || _server is null) return;
        if (uri.Scheme is "about" or "data" or "blob") return;
        if (_allowedUrl is not null && uri.AbsoluteUri == new Uri(_allowedUrl).AbsoluteUri) return;

        if (_server.TryParse(uri, out var path, out var mode, out var fragment))
        {
            var cur = _history.Current;
            // Anker im selben Dokument oder Neuladen: im Seitenkontext belassen
            if (cur is not null && cur.Mode == mode &&
                string.Equals(cur.Path, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return;
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () => await RouteLocalAsync(path, fragment));
            return;
        }

        e.Cancel = true;
        if (uri.Host == "127.0.0.1" && uri.Port == _server.Port) return; // eigener Server, aber kein gültiges Ziel
        if (uri.Scheme == "file")
        {
            // Wird eine Datei auf das WebView gezogen, navigiert es zu file:// – wir öffnen sie im Viewer.
            var local = uri.LocalPath;
            var frag = uri.Fragment.Length > 1 ? Uri.UnescapeDataString(uri.Fragment[1..]) : null;
            Dispatcher.UIThread.Post(async () => await RouteLocalAsync(local, frag));
        }
        else if (uri.Scheme is "http" or "https" or "mailto" or "tel")
        {
            ExternalOpener.Open(uri.AbsoluteUri);
        }
    }

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        var uri = e.Request;
        if (uri is null) return;
        if (_server is not null && _server.TryParse(uri, out var path, out _, out var frag))
            Dispatcher.UIThread.Post(async () => await RouteLocalAsync(path, frag));
        else if (uri.Scheme is "http" or "https" or "mailto")
            ExternalOpener.Open(uri.AbsoluteUri);
    }

    private async Task RouteLocalAsync(string path, string? fragment)
    {
        if (Directory.Exists(path)) { ExternalOpener.Open(path); return; }
        if (!File.Exists(path)) return;
        if (LinkResolver.IsMarkdown(path)) await NavigateToAsync(new HistoryEntry(path, ViewMode.Normal, fragment));
        else ExternalOpener.Open(path);
    }

    private async Task OnNavigationCompletedAsync()
    {
        await ApplyZoomAsync();
        if (_pendingPrint)
        {
            _pendingPrint = false;
            for (var i = 0; i < 120; i++)
            {
                try
                {
                    var ready = await Web.InvokeScript("String(window.__ready === true)");
                    if (ready?.Contains("true") == true) break;
                }
                catch { }
                await Task.Delay(250);
            }
            Web.ShowPrintUI();
        }
        if (_pendingFind is not null)
        {
            var q = _pendingFind;
            _pendingFind = null;
            await Task.Delay(300);
            await FindAsync(q, backwards: false);
        }
    }

    // ---------- Drag & Drop ----------

    private void OnDragOver(object? sender, DragEventArgs e) => e.DragEffects = DragDropEffects.Copy;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
        if (path is null) return;
        if (Directory.Exists(path)) return;
        await OpenFileAsync(path);
    }

    // ---------- Sidebar ----------

    private async Task RefreshSidebarAsync(string path)
    {
        var token = ++_sidebarRefresh;
        DocumentInfo info;
        DocNode root;
        try { (info, root) = await Task.Run(() => (_engine.Analyze(path), DocumentTree.Build(path, _engine))); }
        catch { return; }
        if (token != _sidebarRefresh) return;

        _filling = true;
        try
        {
            OutlineTree.ItemsSource = BuildOutline(info.Outline);
            _treeDocs = DocumentTree.Flatten(root);
            DocTree.ItemsSource = new[] { BuildTreeItem(root) };
            if (DocTree.Items.FirstOrDefault() is TreeViewItem rootItem) rootItem.IsExpanded = true;
        }
        finally { _filling = false; }
        Title = info.Title + " – MarkDown Viewer";
    }

    private static List<TreeViewItem> BuildOutline(IReadOnlyList<OutlineItem> items)
    {
        var roots = new List<TreeViewItem>();
        var stack = new Stack<(int Level, TreeViewItem Item)>();
        foreach (var o in items)
        {
            var item = new TreeViewItem { Header = o.Text, Tag = o.Id, IsExpanded = true };
            while (stack.Count > 0 && stack.Peek().Level >= o.Level) stack.Pop();
            if (stack.Count == 0) roots.Add(item);
            else ((List<TreeViewItem>)stack.Peek().Item.ItemsSource!).Add(item);
            item.ItemsSource = new List<TreeViewItem>();
            stack.Push((o.Level, item));
        }
        return roots;
    }

    private static TreeViewItem BuildTreeItem(DocNode node)
    {
        var item = new TreeViewItem
        {
            Header = node.Title,
            Tag = node.Path,
            IsExpanded = node.Depth < 2
        };
        ToolTip.SetTip(item, node.Path);
        if (node.Children.Count > 0) item.ItemsSource = node.Children.Select(BuildTreeItem).ToList();
        return item;
    }

    private async void OnOutlineSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_filling || OutlineTree.SelectedItem is not TreeViewItem { Tag: string id } || id.Length == 0) return;
        if (_history.Current?.Mode != ViewMode.Normal) return;
        await Web.InvokeScript($"(function(){{var e=document.getElementById({JsonSerializer.Serialize(id)});if(e)e.scrollIntoView();}})()");
    }

    private async void OnDocTreeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_filling || DocTree.SelectedItem is not TreeViewItem { Tag: string path }) return;
        if (string.Equals(_history.Current?.Path, path, StringComparison.Ordinal) && _history.Current?.Mode == ViewMode.Normal) return;
        await OpenFileAsync(path);
    }

    private void OnToggleSidebar(object? sender, RoutedEventArgs e)
    {
        var show = !Sidebar.IsVisible;
        Sidebar.IsVisible = show;
        Splitter.IsVisible = show;
        BodyGrid.ColumnDefinitions[0].Width = show ? new GridLength(300) : new GridLength(0);
    }

    // ---------- Suche ----------

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync(backwards: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    private async void OnFindNext(object? sender, RoutedEventArgs e) => await SearchAsync(false);
    private async void OnFindPrev(object? sender, RoutedEventArgs e) => await SearchAsync(true);

    private async Task SearchAsync(bool backwards)
    {
        var q = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return;
        if (TreeSearchBox.IsChecked == true)
        {
            TreeQueryBox.Text = q;
            await TreeSearchAsync(q);
            Sidebar.SelectedItem = SearchTab;
            if (!Sidebar.IsVisible) OnToggleSidebar(null, new RoutedEventArgs());
        }
        else
        {
            await FindAsync(q, backwards);
        }
    }

    private async void OnTreeQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await TreeSearchAsync(TreeQueryBox.Text?.Trim());
    }

    private async Task TreeSearchAsync(string? q)
    {
        SearchResults.ItemsSource = null;
        if (string.IsNullOrEmpty(q)) { TreeSearchStatus.Text = ""; return; }
        var files = _treeDocs.Select(d => d.Path).ToList();
        if (files.Count == 0 && _history.Current is not null) files.Add(_history.Current.Path);
        if (files.Count == 0) { TreeSearchStatus.Text = "Kein Dokument geöffnet."; return; }
        var hits = await Task.Run(() => TreeSearch.Search(files, q));
        SearchResults.ItemsSource = hits.Select(h => new ListBoxItem
        {
            Content = $"{Path.GetFileName(h.Path)}:{h.Line}  {Truncate(h.Snippet, 90)}",
            Tag = h.Path
        }).ToList();
        TreeSearchStatus.Text = hits.Count == 0
            ? $"Keine Treffer in {files.Count} Dokument(en)."
            : $"{hits.Count} Treffer in {files.Count} Dokument(en).";
    }

    private async Task FindAsync(string q, bool backwards)
    {
        if (!Web.IsVisible) return;
        await Web.InvokeScript($"window.find({JsonSerializer.Serialize(q)}, false, {(backwards ? "true" : "false")}, true)");
    }

    private async void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is not ListBoxItem { Tag: string path }) return;
        var q = TreeQueryBox.Text?.Trim();
        if (string.IsNullOrEmpty(q)) q = SearchBox.Text?.Trim();
        if (string.Equals(_history.Current?.Path, path, StringComparison.Ordinal) && _history.Current?.Mode == ViewMode.Normal)
        {
            if (!string.IsNullOrEmpty(q)) await FindAsync(q, false);
            return;
        }
        _pendingFind = q;
        await OpenFileAsync(path);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // ---------- Drucken ----------

    private async void OnPrint(object? sender, RoutedEventArgs e) => await PrintAsync();

    private async Task PrintAsync()
    {
        var cur = _history.Current;
        if (cur is null || _server is null) return;
        var choice = await new PrintDialog(Math.Max(1, _treeDocs.Count)).ShowDialog<ViewMode?>(this);
        if (choice is null) return;
        _pendingPrint = true;
        await NavigateToAsync(new HistoryEntry(cur.Path, choice.Value));
    }

    // ---------- Theme, Zoom, Live-Reload ----------

    private void ApplyAppTheme() =>
        Application.Current!.RequestedThemeVariant = _theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private async void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        _theme = _theme == "dark" ? "light" : "dark";
        _settings.Theme = _theme;
        _settings.Save();
        ApplyAppTheme();
        if (_history.Current is not null)
        {
            await CaptureScrollAsync();
            Load(_history.Current);
        }
    }

    private async void OnZoomIn(object? sender, RoutedEventArgs e) => await SetZoomAsync(_zoom + 0.1);
    private async void OnZoomOut(object? sender, RoutedEventArgs e) => await SetZoomAsync(_zoom - 0.1);

    private async Task SetZoomAsync(double z)
    {
        _zoom = Math.Clamp(Math.Round(z, 1), 0.5, 3.0);
        await ApplyZoomAsync();
    }

    private async Task ApplyZoomAsync()
    {
        if (!Web.IsVisible) return;
        try { await Web.InvokeScript($"document.documentElement.style.zoom='{_zoom.ToString("0.0", CultureInfo.InvariantCulture)}'"); }
        catch { }
    }

    private void WatchFile(string path)
    {
        _watcher?.Dispose();
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            // Atomares Speichern (Temp-Datei + Umbenennen) löst Created/Renamed aus – daher alle Ereignisse.
            FileSystemEventHandler h = (_, _) => ScheduleReload(path);
            _watcher.Changed += h;
            _watcher.Created += h;
            _watcher.Renamed += (_, _) => ScheduleReload(path);
        }
        catch { _watcher = null; }
    }

    private void ScheduleReload(string path)
    {
        _reloadCts?.Cancel();
        var cts = _reloadCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(300, cts.Token); } catch (OperationCanceledException) { return; }
            Dispatcher.UIThread.Post(async () =>
            {
                var cur = _history.Current;
                if (cur is null || !string.Equals(cur.Path, path, StringComparison.Ordinal) || !File.Exists(path)) return;
                await CaptureScrollAsync();
                Load(_history.Current!);
            });
        });
    }

    // ---------- Tastatur / Maus / Start ----------

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (alt && e.Key == Key.Left) { e.Handled = true; await GoAsync(true); }
        else if (alt && e.Key == Key.Right) { e.Handled = true; await GoAsync(false); }
        else if (ctrl && e.Key == Key.F) { e.Handled = true; SearchBox.Focus(); SearchBox.SelectAll(); }
        else if (ctrl && e.Key == Key.P) { e.Handled = true; await PrintAsync(); }
        else if (ctrl && (e.Key is Key.OemPlus or Key.Add)) { e.Handled = true; await SetZoomAsync(_zoom + 0.1); }
        else if (ctrl && (e.Key is Key.OemMinus or Key.Subtract)) { e.Handled = true; await SetZoomAsync(_zoom - 0.1); }
        else if (ctrl && (e.Key is Key.D0 or Key.NumPad0)) { e.Handled = true; await SetZoomAsync(1.0); }
        else if (e.Key == Key.F3) { e.Handled = true; await SearchAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); }
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Properties;
        if (p.IsXButton1Pressed) { e.Handled = true; await GoAsync(true); }
        else if (p.IsXButton2Pressed) { e.Handled = true; await GoAsync(false); }
    }

    private void RefreshRecent()
    {
        var existing = _settings.Recent.Where(File.Exists).ToList();
        RecentList.ItemsSource = existing.Select(p => new ListBoxItem { Content = p, Tag = p }).ToList();
        RecentLabel.IsVisible = RecentList.IsVisible = existing.Count > 0;
    }

    private async void OnRecentSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is ListBoxItem { Tag: string path })
        {
            RecentList.SelectedItem = null;
            await OpenFileAsync(path);
        }
    }
}
