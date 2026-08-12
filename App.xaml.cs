using System.Windows;
using AppUpdater.ViewModels;

namespace AppUpdater;

public partial class App : System.Windows.Application
{
    private static readonly string MutexName = "AppUpdater_3B7A0E21-6A29-4C0B-8E2C-4A5C7B6D8F9E";
    private static readonly string SignalEventName = "AppUpdater_Signal_3B7A0E21";
    private Mutex? _mutex;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private MainViewModel? _viewModel;
    private Thread? _signalListener;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _mutex = new Mutex(false, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // Mutex already exists — try to acquire it
                try
                {
                    if (!_mutex.WaitOne(0))
                    {
                        // Another active instance owns it
                        SignalExistingInstance();
                        Current.Shutdown();
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // Previous instance crashed — we own it now, continue
                }
            }
            else
            {
                // New mutex created — acquire ownership
                _mutex.WaitOne(0);
            }
        }
        catch (UnauthorizedAccessException)
        {
            SignalExistingInstance();
            Current.Shutdown();
            return;
        }

        ListenForSignals();

        base.OnStartup(e);

        _viewModel = new MainViewModel();

        var mainWindow = new MainWindow
        {
            DataContext = _viewModel
        };
        Current.MainWindow = mainWindow;
        mainWindow.Show();

        SetupTrayIcon();
    }

    private void SignalExistingInstance()
    {
        try
        {
            using var evt = new EventWaitHandle(false, EventResetMode.AutoReset, SignalEventName);
            evt.Set();
        }
        catch { }
    }

    private void ListenForSignals()
    {
        _signalListener = new Thread(() =>
        {
            try
            {
                using var evt = new EventWaitHandle(false, EventResetMode.AutoReset, SignalEventName);
                while (true)
                {
                    evt.WaitOne();
                    Current.Dispatcher.Invoke(() =>
                    {
                        var w = Current.MainWindow;
                        if (w != null)
                        {
                            w.Show();
                            if (w.WindowState == WindowState.Minimized)
                                w.WindowState = WindowState.Normal;
                            w.Activate();
                            w.Topmost = true;
                            w.Topmost = false;
                        }
                    });
                }
            }
            catch { }
        });
        _signalListener.IsBackground = true;
        _signalListener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void SetupTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = CreateAppIcon(),
            Visible = true,
            Text = "MrLurix App Updater"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show MrLurix App Updater", null, (_, _) => ShowWindow());
        menu.Items.Add("Check for Updates Now", null, async (_, _) =>
        {
            if (_viewModel != null)
                await _viewModel.CheckForUpdatesAsync(null);
            ShowWindow();
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        Current.MainWindow.Closing += OnMainWindowClosing;
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Current.MainWindow.Hide();
    }

    private void ShowWindow()
    {
        var w = Current.MainWindow;
        if (w == null) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized)
            w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true;
        w.Topmost = false;
    }

    private void ExitApp()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        Current.Shutdown();
    }

    private static System.Drawing.Icon CreateAppIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("AppUpdater.Resources.app_logo.png");
            if (stream != null)
            {
                using var src = System.Drawing.Image.FromStream(stream);
                using var bmp = new System.Drawing.Bitmap(64, 64);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddEllipse(0, 0, 64, 64);
                g.SetClip(path);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, 64, 64);
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }
        return System.Drawing.Icon.ExtractAssociatedIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "MrLurixAppUpdater.exe")) ?? System.Drawing.SystemIcons.Application;
    }
}
