using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace AppUpdater.Services;

public class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = "";
}

public class SelfUpdateResult
{
    public bool UpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
}

public class SelfUpdateService
{
    private readonly string _manifestUrl;
    private readonly HttpClient _http;

    private static readonly string UpdateSettingsFile = Path.Combine(
        AppContext.BaseDirectory,
        "update-config.json");

    public SelfUpdateService(string manifestUrl)
    {
        _manifestUrl = manifestUrl;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static string GetCurrentVersion()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? ver.ToString(3) : "1.0.0";
    }

    public static string? GetConfiguredUpdateUrl()
    {
        try
        {
            if (File.Exists(UpdateSettingsFile))
            {
                var json = File.ReadAllText(UpdateSettingsFile);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var url = doc.RootElement.GetProperty("updateUrl").GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
        }
        catch { }
        return _defaultUpdateUrl;
    }

    private static readonly string _defaultUpdateUrl = "https://api.github.com/repos/mrlurix/MrLurixAppUpdater/releases/latest";

    public async Task<SelfUpdateResult> CheckForUpdateAsync(string? customUrl = null)
    {
        var result = new SelfUpdateResult
        {
            CurrentVersion = GetCurrentVersion()
        };

        var url = customUrl ?? _manifestUrl;
        if (string.IsNullOrEmpty(url))
        {
            result.UpdateAvailable = false;
            return result;
        }

        try
        {
            _http.DefaultRequestHeaders.UserAgent.TryParseAdd("AppUpdater/1.0");
            var manifest = await _http.GetFromJsonAsync<UpdateManifest>(url).WaitAsync(TimeSpan.FromSeconds(10));

            if (manifest == null || string.IsNullOrEmpty(manifest.Version))
            {
                var github = await TryParseGitHubReleaseAsync(url);
                if (github != null)
                    manifest = github;
            }

            if (manifest == null || string.IsNullOrEmpty(manifest.Version))
            {
                result.UpdateAvailable = false;
                return result;
            }

            result.LatestVersion = manifest.Version;
            result.DownloadUrl = manifest.DownloadUrl;
            result.ReleaseNotes = manifest.ReleaseNotes;

            var current = Version.TryParse(result.CurrentVersion, out var cv) ? cv : new Version(1, 0, 0);
            var latest = Version.TryParse(manifest.Version, out var lv) ? lv : new Version(1, 0, 0);

            result.UpdateAvailable = latest > current;
        }
        catch
        {
            result.UpdateAvailable = false;
        }

        return result;
    }

    private async Task<UpdateManifest?> TryParseGitHubReleaseAsync(string url)
    {
        try
        {
            var json = await _http.GetStringAsync(url).WaitAsync(TimeSpan.FromSeconds(10));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var m = ParseGitHubRelease(item);
                    if (m != null) return m;
                }
                return null;
            }

            return ParseGitHubRelease(root);
        }
        catch
        {
            return null;
        }
    }

    private static UpdateManifest? ParseGitHubRelease(System.Text.Json.JsonElement release)
    {
        if (release.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (!release.TryGetProperty("tag_name", out var tag)) return null;

        var manifest = new UpdateManifest
        {
            Version = tag.GetString()?.TrimStart('v', 'V') ?? ""
        };

        if (release.TryGetProperty("body", out var body))
            manifest.ReleaseNotes = body.GetString() ?? "";

        if (release.TryGetProperty("assets", out var assets) &&
            assets.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("browser_download_url", out var dl) &&
                    dl.GetString() is { Length: > 0 } downloadUrl)
                {
                    manifest.DownloadUrl = downloadUrl;
                    break;
                }
            }
        }

        return string.IsNullOrEmpty(manifest.DownloadUrl) ? null : manifest;
    }

    public async Task<bool> DownloadUpdateAsync(string downloadUrl, string destinationPath)
    {
        try
        {
            var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(destinationPath);
            await stream.CopyToAsync(fileStream);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyUpdate(string newExePath)
    {
        var currentExe = Path.Combine(AppContext.BaseDirectory,
            Assembly.GetEntryAssembly()?.GetName().Name + ".exe");
        var batPath = Path.Combine(Path.GetTempPath(), $"updater_{Guid.NewGuid():N}.bat");

        var lines = new[]
        {
            "@echo off",
            "chcp 65001 >nul",
            "",
            $":wait",
            $"tasklist /fi \"PID eq {Environment.ProcessId}\" 2>nul | find \"{Environment.ProcessId}\" >nul",
            "if %errorlevel% equ 0 (",
            "    timeout /t 1 /nobreak >nul",
            "    goto wait",
            ")",
            "",
            $"copy /y \"{newExePath}\" \"{currentExe}\" >nul",
            $"del /f /q \"{newExePath}\" >nul 2>&1",
            $"start \"\" \"{currentExe}\"",
            $"del /f /q \"%~f0\" >nul 2>&1",
        };

        File.WriteAllLines(batPath, lines);

        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}
