#if DEBUG
using Avalonia.Controls;
using Avalonia.Threading;
using MarkDownViewer.Core;

namespace MarkDownViewer;

// Nur im Debug-Build: erlaubt automatisierte Tests/Screenshots über eine Befehlsdatei (MDV_CMD_FILE).
public partial class MainWindow
{
    partial void StartDebugHooks()
    {
        var file = Environment.GetEnvironmentVariable("MDV_CMD_FILE");
        if (string.IsNullOrEmpty(file)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += async (_, _) =>
        {
            if (!File.Exists(file)) return;
            string[] lines;
            try { lines = File.ReadAllLines(file); File.Delete(file); } catch { return; }
            foreach (var line in lines.Where(l => l.Trim().Length > 0))
            {
                var sp = line.IndexOf(' ');
                var cmd = sp < 0 ? line : line[..sp];
                var arg = sp < 0 ? "" : line[(sp + 1)..];
                switch (cmd)
                {
                    case "js": File.AppendAllText(file + ".out", "js: " + await Web.InvokeScript(arg) + "\n"); break;
                    case "back": await GoAsync(true); break;
                    case "fwd": await GoAsync(false); break;
                    case "theme": OnToggleTheme(null, new()); break;
                    case "open": await OpenFileAsync(arg); break;
                    case "printmode":
                        await NavigateToAsync(new HistoryEntry(_history.Current!.Path, arg == "tree" ? ViewMode.PrintTree : ViewMode.PrintSingle));
                        break;
                    case "search": SearchBox.Text = arg; TreeSearchBox.IsChecked = false; await SearchAsync(false); break;
                    case "treesearch": SearchBox.Text = arg; TreeSearchBox.IsChecked = true; await SearchAsync(false); break;
                    case "tab": Sidebar.SelectedIndex = arg switch { "outline" => 0, "tree" => 1, _ => 2 }; break;
                    case "status":
                        var c = _history.Current;
                        File.AppendAllText(file + ".out",
                            $"status: path={c?.Path} mode={c?.Mode} back={_history.CanGoBack} fwd={_history.CanGoForward} title={Title}\n");
                        break;
                    case "printnow": _pendingPrint = true; await NavigateToAsync(new HistoryEntry(_history.Current!.Path, arg == "tree" ? ViewMode.PrintTree : ViewMode.PrintSingle)); break;
                    case "printdialog": _ = PrintAsync(); break;
                    case "quit": Close(); break;
                }
                await Task.Delay(600);
            }
        };
        timer.Start();
    }
}
#endif
