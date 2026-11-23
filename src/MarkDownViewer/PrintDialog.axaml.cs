using Avalonia.Controls;
using Avalonia.Interactivity;
using MarkDownViewer.Core;

namespace MarkDownViewer;

public partial class PrintDialog : Window
{
    public PrintDialog() : this(1) { }

    public PrintDialog(int treeCount)
    {
        InitializeComponent();
        TreeOption.Content = treeCount > 1
            ? $"Gesamten Baum: dieses Dokument und alle {treeCount - 1} verlinkten Dokumente nach unten"
            : "Gesamten Baum (keine weiteren verlinkten Dokumente)";
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnPrint(object? sender, RoutedEventArgs e) =>
        Close(TreeOption.IsChecked == true ? ViewMode.PrintTree : ViewMode.PrintSingle);
}
