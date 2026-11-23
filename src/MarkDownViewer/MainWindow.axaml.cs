using Avalonia.Controls;
using Avalonia.Input;
using Markdig;

namespace MarkDownViewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += (_, _) => Show("# MarkDown Viewer\n\nMarkdown-Datei hierher ziehen.");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
        if (path is not null && File.Exists(path))
            Show(File.ReadAllText(path));
    }

    private void Show(string markdown) =>
        Web.NavigateToString($"<!doctype html><meta charset=utf-8><body>{Markdown.ToHtml(markdown)}</body>");
}
