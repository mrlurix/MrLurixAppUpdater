using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using AppUpdater.Services;

namespace AppUpdater.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly WingetService _wingetService = new();
    private readonly SelfUpdateService _selfUpdateService;
    private readonly AppSettings _settings = AppSettings.Load();
    private DispatcherTimer? _scheduleTimer;
    private bool _shutdownPending;
    private bool _settingsOpen;

    public ObservableCollection<UpdateInfo> Updates { get; } = new();

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _currentPackage = "";
    public string CurrentPackage
    {
        get => _currentPackage;
        set { _currentPackage = value; OnPropertyChanged(); }
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    private bool _isChecking;
    public bool IsChecking
    {
        get => _isChecking;
        set { _isChecking = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
    }

    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        set { _isUpdating = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !IsChecking && !IsUpdating;

    private bool _hasUpdates;
    public bool HasUpdates
    {
        get => _hasUpdates;
        set { _hasUpdates = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoUpdates)); }
    }

    public bool HasNoUpdates => !HasUpdates;

    private string _lastCheckTime = "Never";
    public string LastCheckTime
    {
        get => _lastCheckTime;
        set { _lastCheckTime = value; OnPropertyChanged(); }
    }

    private string _updateCountText = "";
    public string UpdateCountText
    {
        get => _updateCountText;
        set { _updateCountText = value; OnPropertyChanged(); }
    }

    private bool _isProgressIndeterminate;
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set { _isProgressIndeterminate = value; OnPropertyChanged(); }
    }

    private bool _isProgressVisible;
    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        set { _isProgressVisible = value; OnPropertyChanged(); }
    }

    private string _statusIcon = "✓";
    public string StatusIcon
    {
        get => _statusIcon;
        set { _statusIcon = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#C0A0A0";
    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public ICommand CheckForUpdatesCommand { get; }
    public ICommand RunUpdatesCommand { get; }
    public ICommand CheckAppUpdateCommand { get; }

    private bool _appUpdateAvailable;
    public bool AppUpdateAvailable
    {
        get => _appUpdateAvailable;
        set { _appUpdateAvailable = value; OnPropertyChanged(); }
    }

    private string _appUpdateText = "";
    public string AppUpdateText
    {
        get => _appUpdateText;
        set { _appUpdateText = value; OnPropertyChanged(); }
    }

    private SelfUpdateResult? _appUpdateResult;

    public ICommand ToggleSettingsCommand { get; }
    public ICommand CancelShutdownCommand { get; }

    public List<int> Hours { get; } = Enumerable.Range(0, 24).ToList();

    public bool IsSettingsOpen
    {
        get => _settingsOpen;
        set { _settingsOpen = value; OnPropertyChanged(); }
    }

    public bool ScheduledDownloadEnabled
    {
        get => _settings.ScheduledDownloadEnabled;
        set { _settings.ScheduledDownloadEnabled = value; _settings.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(DownloadWindowHint)); }
    }

    public int DownloadStartHour
    {
        get => _settings.DownloadStartHour;
        set { _settings.DownloadStartHour = value; _settings.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(DownloadWindowHint)); }
    }

    public int DownloadEndHour
    {
        get => _settings.DownloadEndHour;
        set { _settings.DownloadEndHour = value; _settings.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(DownloadWindowHint)); }
    }

    public bool ShutdownAfterUpdate
    {
        get => _settings.ShutdownAfterUpdate;
        set { _settings.ShutdownAfterUpdate = value; _settings.Save(); OnPropertyChanged(); }
    }

    public bool CanCancelShutdown => _shutdownPending;

    public string DownloadWindowHint => ScheduledDownloadEnabled
        ? $"Downloads allowed {DownloadStartHour:00}:00 – {DownloadEndHour:00}:00"
        : "Scheduled download is off";

    public MainViewModel()
    {
        var updateUrl = SelfUpdateService.GetConfiguredUpdateUrl() ?? "";
        _selfUpdateService = new SelfUpdateService(updateUrl);

        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        RunUpdatesCommand = new AsyncRelayCommand(RunUpdatesAsync, _ => !IsUpdating && HasUpdates);
        CheckAppUpdateCommand = new AsyncRelayCommand(CheckAppUpdateAsync);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        CancelShutdownCommand = new RelayCommand(_ => CancelShutdown());

        _ = CheckForUpdatesAsync(null);
    }

    public async Task CheckAppUpdateAsync(object? _)
    {
        if (_appUpdateResult != null && _appUpdateResult.UpdateAvailable)
        {
            AppUpdateText = "Downloading update...";
            AppUpdateAvailable = false;

            var tempPath = Path.Combine(Path.GetTempPath(), $"AppUpdater_{Guid.NewGuid():N}.exe");
            var ok = await _selfUpdateService.DownloadUpdateAsync(_appUpdateResult.DownloadUrl, tempPath);

            if (!ok)
            {
                AppUpdateText = "Download failed — check connection";
                _appUpdateResult = null;
                return;
            }

            SelfUpdateService.ApplyUpdate(tempPath);
            System.Windows.Application.Current.Shutdown();
            return;
        }

        AppUpdateText = "Checking...";
        AppUpdateAvailable = false;
        _appUpdateResult = null;

        var result = await _selfUpdateService.CheckForUpdateAsync();
        _appUpdateResult = result;

        if (result.UpdateAvailable)
        {
            AppUpdateText = $"v{result.LatestVersion} available — click to update";
            AppUpdateAvailable = true;
        }
        else
        {
            AppUpdateText = $"App v{result.CurrentVersion}";
            AppUpdateAvailable = false;
        }
    }

    public async Task CheckForUpdatesAsync(object? _)
    {
        IsChecking = true;
        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        StatusText = "Checking for updates...";
        StatusIcon = "⟳";
        StatusColor = "#FF1744";
        Updates.Clear();

        try
        {
            var updates = await _wingetService.GetAvailableUpdatesAsync();

            foreach (var update in updates)
                Updates.Add(update);

            HasUpdates = updates.Count > 0;
            UpdateCountText = updates.Count > 0
                ? $"{updates.Count} update{(updates.Count != 1 ? "s" : "")} available"
                : "All apps are up to date";

            if (updates.Count > 0)
            {
                StatusText = $"{updates.Count} update{(updates.Count != 1 ? "s" : "")} found";
                StatusIcon = "●";
                StatusColor = "#FF9100";
            }
            else
            {
                StatusText = "All apps are up to date";
                StatusIcon = "✓";
                StatusColor = "#E040FB";
            }

            LastCheckTime = DateTime.Now.ToString("MMM dd, yyyy  HH:mm");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            StatusIcon = "✕";
            StatusColor = "#FF5252";
        }
        finally
        {
            IsChecking = false;
            IsProgressIndeterminate = false;
            IsProgressVisible = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task RunUpdatesAsync(object? _)
    {
        if (!_settings.IsInDownloadWindow())
        {
            StatusText = $"Downloads scheduled for {DownloadStartHour:00}:00 – {DownloadEndHour:00}:00. Waiting...";
            StatusIcon = "◇";
            StatusColor = "#FF9100";
            IsProgressVisible = false;
            IsProgressIndeterminate = false;
            StartScheduleTimer();
            return;
        }

        IsUpdating = true;
        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        StatusText = "Starting updates...";
        StatusIcon = "⟳";
        StatusColor = "#FF1744";
        CurrentPackage = "";

        var progress = new Progress<UpdateProgress>(p =>
        {
            CurrentPackage = p.CurrentPackage;

            if (p.TotalPackages > 0)
            {
                IsProgressIndeterminate = false;
                ProgressValue = (double)p.CompletedPackages / p.TotalPackages * 100;
            }

            switch (p.Status)
            {
                case UpdateStatus.Downloading:
                    StatusText = $"Downloading {p.CurrentPackage}...";
                    break;
                case UpdateStatus.Installing:
                    StatusText = $"Installing {p.CurrentPackage}...";
                    break;
                case UpdateStatus.Completed:
                    if (p.CompletedPackages >= p.TotalPackages && p.TotalPackages > 0)
                    {
                        StatusText = "All updates completed!";
                        StatusIcon = "✓";
                        StatusColor = "#E040FB";
                        IsProgressVisible = false;
                    }
                    break;
                case UpdateStatus.Error:
                    StatusText = p.Message;
                    StatusIcon = "✕";
                    StatusColor = "#FF5252";
                    break;
            }
        });

        try
        {
            using var cts = new CancellationTokenSource();
            await _wingetService.RunUpdatesAsync(progress, cts.Token);

            if (!cts.IsCancellationRequested)
            {
                StatusText = "All updates completed successfully!";
                StatusIcon = "✓";
                StatusColor = "#E040FB";
                IsProgressVisible = false;
                CurrentPackage = "";
                if (ShutdownAfterUpdate)
                {
                    ScheduleShutdown();
                }
                else
                {
                    _ = CheckForUpdatesAsync(null);
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Updates cancelled";
            StatusIcon = "●";
            StatusColor = "#FF9100";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            StatusIcon = "✕";
            StatusColor = "#FF5252";
        }
        finally
        {
            IsUpdating = false;
            IsProgressIndeterminate = false;
            IsProgressVisible = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void StartScheduleTimer()
    {
        if (_scheduleTimer != null) return;
        _scheduleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _scheduleTimer.Tick += async (_, _) =>
        {
            if (_settings.IsInDownloadWindow())
            {
                _scheduleTimer.Stop();
                _scheduleTimer = null;
                await RunUpdatesAsync(null);
            }
        };
        _scheduleTimer.Start();
    }

    private void ScheduleShutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 60")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            _shutdownPending = true;
            OnPropertyChanged(nameof(CanCancelShutdown));
            StatusText = "Updates done. System will shut down in 60 s.";
            StatusIcon = "⏻";
            StatusColor = "#B388FF";
        }
        catch { }
    }

    private void CancelShutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/a")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { }
        _shutdownPending = false;
        OnPropertyChanged(nameof(CanCancelShutdown));
        StatusText = "Shutdown cancelled";
        StatusIcon = "✓";
        StatusColor = "#E040FB";
    }
}
