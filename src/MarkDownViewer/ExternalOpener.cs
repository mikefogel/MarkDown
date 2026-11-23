using System.Diagnostics;

namespace MarkDownViewer;

/// <summary>Öffnet Dateien und URLs mit dem Standardprogramm des Systems.</summary>
public static class ExternalOpener
{
    public static void Open(string pathOrUrl)
    {
        try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
        catch { /* kein Standardprogramm vorhanden */ }
    }
}
