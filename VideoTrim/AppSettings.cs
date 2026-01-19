using System;
using System.IO;
using System.Text.Json;

namespace VideoTrim;

internal sealed class AppSettings
{
    public string InputFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public string ProcessedFolder { get; set; } = string.Empty;
    public int MinSeconds { get; set; } = 15;
    public int MaxSeconds { get; set; } = 20;
    public ProcessingMode Mode { get; set; } = ProcessingMode.SplitOnly;
    public int SegmentsPerVideo { get; set; } = 3;
    public int VideosPerInput { get; set; } = 3;
    public bool AutoDetectSimilar { get; set; } = true;
    public double Similarity { get; set; } = 0.92;
    public bool PauseForManualDelete { get; set; } = true;
    public bool FastSplit { get; set; } = true;
    public bool Crossfade { get; set; } = false;

    // Licensing & trial
    public string LicenseKey { get; set; } = string.Empty;
    public bool IsLicensed { get; set; } = false;
    public int TrialUsedVideos { get; set; } = 0;
    public string[] TrialFileIds { get; set; } = Array.Empty<string>();

    public static string GetSettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SplitThatShit");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var path = GetSettingsPath();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }
}
