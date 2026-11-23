using System.Text.Json;

namespace MarkDownViewer;

public sealed class AppSettings
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarkDownViewer", "settings.json");

    /// <summary>"light", "dark" oder null (System).</summary>
    public string? Theme { get; set; }
    public List<string> Recent { get; set; } = [];

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));
        Recent.Insert(0, path);
        if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* Einstellungen sind optional */ }
    }
}
