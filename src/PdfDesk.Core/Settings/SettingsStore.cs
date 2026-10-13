using System.Text.Json;

namespace PdfDesk.Core.Settings;

public sealed class AppSettings
{
    public const int MaxRecentFiles = 10;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 1200;

    public double WindowHeight { get; set; } = 850;

    public bool WindowMaximized { get; set; }

    public string? LastFolder { get; set; }

    public List<string> RecentFiles { get; set; } = [];

    public double DefaultFontSize { get; set; } = 11;

    public string DefaultColor { get; set; } = "#000000";

    public string DateFormat { get; set; } = "dd/MM/yyyy";

    public double ThumbnailWidth { get; set; } = 110;

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }
}

/// <summary>Reads and writes <c>%APPDATA%\PdfDesk\settings.json</c>.</summary>
public sealed class SettingsStore(string? path = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Path { get; } = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfDesk", "settings.json");

    /// <summary>Returns defaults when the file is missing or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be read; using defaults", ex);
            return new AppSettings();
        }
    }

    /// <summary>Writes atomically (temp file, then move) so a crash never leaves a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be saved", ex);
        }
    }
}
