using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AppUpdater.Services;

public class UpdateInfo
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public string Source { get; set; } = "";
}

public enum UpdateStatus { Idle, Checking, Downloading, Installing, Completed, Error }

public class UpdateProgress
{
    public UpdateStatus Status { get; set; } = UpdateStatus.Idle;
    public string CurrentPackage { get; set; } = "";
    public string Message { get; set; } = "";
    public int TotalPackages { get; set; }
    public int CompletedPackages { get; set; }
}

public class WingetService
{
    public async Task<List<UpdateInfo>> GetAvailableUpdatesAsync()
    {
        var list = new List<UpdateInfo>();

        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = "upgrade --accept-source-agreements",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null) return list;

        var lines = new List<string>();
        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line == null) break;
            lines.Add(line);
        }

        await process.WaitForExitAsync();

        return ParseTableOutput(lines);
    }

    private static List<UpdateInfo> ParseTableOutput(List<string> lines)
    {
        var list = new List<UpdateInfo>();

        // Filter out progress/spinner lines, find header and data section
        string? header = null;
        var dataLines = new List<string>();
        bool headerFound = false;
        bool separatorPassed = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Skip progress/spinner lines (contain block chars or are spinner chars)
            if (ContainsProgressChars(line)) continue;

            // Skip summary lines
            if (line.Contains("upgrade") && (line.Contains("available") || line.Contains("cannot"))) continue;

            // Separator line
            if (line.All(c => c == '-'))
            {
                separatorPassed = true;
                continue;
            }

            // Check for header
            if (!headerFound && line.Contains("Name") && line.Contains("Id") && line.Contains("Version"))
            {
                header = line;
                headerFound = true;
                continue;
            }

            // Data lines after separator
            if (separatorPassed && headerFound)
            {
                dataLines.Add(line);
            }
        }

        if (header == null || dataLines.Count == 0) return list;

        // Determine column positions from header
        int nameEnd = header.IndexOf("Id", StringComparison.Ordinal);
        int idEnd = header.IndexOf("Version", StringComparison.Ordinal);
        int verEnd = header.IndexOf("Available", StringComparison.Ordinal);
        int availEnd = header.LastIndexOf("Source", StringComparison.Ordinal);

        if (nameEnd < 0 || idEnd < 0 || verEnd < 0 || availEnd < 0) return list;

        foreach (var line in dataLines)
        {
            if (line.Length < availEnd) continue;

            try
            {
                var name = line[..nameEnd].Trim();
                var id = line.Substring(nameEnd, idEnd - nameEnd).Trim();
                var version = line.Substring(idEnd, verEnd - idEnd).Trim();
                var available = line.Substring(verEnd, availEnd - verEnd).Trim();
                var source = line[availEnd..].Trim();

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                {
                    list.Add(new UpdateInfo
                    {
                        Name = name,
                        Id = id,
                        CurrentVersion = version,
                        AvailableVersion = available,
                        Source = source
                    });
                }
            }
            catch { }
        }

        return list;
    }

    private static bool ContainsProgressChars(string line)
    {
        // Progress bars contain block characters or are single spinner characters
        if (line.Contains('█') || line.Contains('▒') || line.Contains('▓')) return true;

        // Spinner lines are short and consist of only special chars and spaces
        var trimmed = line.Trim();
        if (trimmed.Length <= 3 && (trimmed == "-" || trimmed == "\\" || trimmed == "/" || trimmed == "|"))
            return true;

        // Lines that are just spinner + whitespace
        if (trimmed.Length <= 1 && (trimmed == "-" || trimmed == "\\" || trimmed == "/" || trimmed == "|"))
            return true;

        return false;
    }

    public async Task RunUpdatesAsync(IProgress<UpdateProgress>? progress, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = "upgrade --all --accept-package-agreements --accept-source-agreements --disable-interactivity",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null) return;

        int totalFound = 0;
        int completed = 0;
        string currentPkg = "";

        while (!process!.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct);
            if (line == null) break;

            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (ContainsProgressChars(trimmed)) continue;

            var match = Regex.Match(trimmed, @"Found\s+(\d+)\s+package", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                totalFound = int.Parse(match.Groups[1].Value);
                continue;
            }

            if (trimmed.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase))
            {
                currentPkg = trimmed["Downloading".Length..].Trim();
                var idx = currentPkg.LastIndexOf("  ");
                if (idx > 0) currentPkg = currentPkg[..idx].Trim();
                currentPkg = Regex.Replace(currentPkg, @"\s+Version:.*", "").Trim();

                progress?.Report(new UpdateProgress
                {
                    Status = UpdateStatus.Downloading,
                    CurrentPackage = currentPkg,
                    TotalPackages = totalFound,
                    CompletedPackages = completed
                });
            }
            else if (trimmed.StartsWith("Installing", StringComparison.OrdinalIgnoreCase))
            {
                currentPkg = trimmed["Installing".Length..].Trim();
                progress?.Report(new UpdateProgress
                {
                    Status = UpdateStatus.Installing,
                    CurrentPackage = currentPkg,
                    TotalPackages = totalFound,
                    CompletedPackages = completed
                });
            }
            else if (trimmed.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase))
            {
                completed++;
                progress?.Report(new UpdateProgress
                {
                    Status = UpdateStatus.Completed,
                    CurrentPackage = currentPkg,
                    TotalPackages = totalFound,
                    CompletedPackages = completed
                });
            }
            else if (trimmed.Contains("No installed package found", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new UpdateProgress
                {
                    Status = UpdateStatus.Error,
                    Message = trimmed,
                    TotalPackages = 0,
                    CompletedPackages = 0
                });
            }
        }

        await process.WaitForExitAsync(ct);

        if (!ct.IsCancellationRequested)
        {
            progress?.Report(new UpdateProgress
            {
                Status = UpdateStatus.Completed,
                CurrentPackage = "",
                Message = "All updates processed",
                TotalPackages = totalFound,
                CompletedPackages = completed
            });
        }
    }
}
