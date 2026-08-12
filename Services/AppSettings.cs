using System.IO;
using System.Text.Json;

namespace AppUpdater.Services;

public class AppSettings
{
    public bool ScheduledDownloadEnabled { get; set; }
    public int DownloadStartHour { get; set; } = 0;
    public int DownloadEndHour { get; set; } = 23;
    public bool ShutdownAfterUpdate { get; set; }

    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public bool IsInDownloadWindow()
    {
        if (!ScheduledDownloadEnabled) return true;
        int hour = DateTime.Now.Hour;
        if (DownloadStartHour <= DownloadEndHour)
            return hour >= DownloadStartHour && hour <= DownloadEndHour;
        return hour >= DownloadStartHour || hour <= DownloadEndHour;
    }

    public string DownloadWindowText =>
        $"{DownloadStartHour:00}:00 – {DownloadEndHour:00}:00";
}
