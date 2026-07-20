using System.Globalization;
using KeyRadar.Rules;
using KeyRadar.Windows.Foreground;
using KeyRadar.Windows.Lifecycle;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KeyRadar;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private ForegroundOverlayWindow? _overlayWindow;
    private DispatcherQueueTimer? _foregroundTimer;
    private SingleInstanceGuard? _singleInstance;
    private int _lastForegroundProcessId;

    public App()
    {
        var language = AppPreferences.Load().Language;
        if (language != "system")
        {
            global::Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        InitializeComponent();
    }

    public void RequestShutdown() => _mainWindow?.Close();

    internal nint GetMainWindowHandle() =>
        _mainWindow is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = SingleInstanceGuard.TryAcquire("LizzardKevin.KeyRadar");
        if (_singleInstance is null)
        {
            Exit();
            return;
        }

        RuntimeRuleCatalog.Reload();
        _overlayWindow = new ForegroundOverlayWindow();
        _mainWindow = new MainWindow();
        _mainWindow.Closed += MainWindow_Closed;
        _mainWindow.Activate();
        TryWriteUpdateHealthMarker();

        _foregroundTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _foregroundTimer.Interval = TimeSpan.FromMilliseconds(250);
        _foregroundTimer.IsRepeating = true;
        _foregroundTimer.Tick += ForegroundTimer_Tick;
        _foregroundTimer.Start();
    }

    private void ForegroundTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var foreground = ForegroundApplicationReader.Read();
        if (foreground is null)
        {
            HideOverlay();
            return;
        }

        var rules = ApplicationRuleMatcher.FindByExecutableName(
            RuntimeRuleCatalog.Current,
            foreground.ExecutableName);
        var shouldShow = ForegroundOverlayPolicy.ShouldShow(
            Environment.ProcessId,
            foreground.ProcessId,
            rules is { Hotkeys.Count: > 0 });

        if (!shouldShow || rules is null)
        {
            HideOverlay();
            return;
        }

        if (_lastForegroundProcessId == foreground.ProcessId)
        {
            return;
        }

        _lastForegroundProcessId = foreground.ProcessId;
        _overlayWindow?.ShowFor(rules, foreground.ProcessId);
    }

    private void HideOverlay()
    {
        _lastForegroundProcessId = 0;
        _overlayWindow?.HideOverlay();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_foregroundTimer is not null)
        {
            _foregroundTimer.Stop();
            _foregroundTimer.Tick -= ForegroundTimer_Tick;
        }

        _overlayWindow?.Close();
        _overlayWindow = null;
        _mainWindow = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        Exit();
    }

    private static void TryWriteUpdateHealthMarker()
    {
        var marker = Environment.GetEnvironmentVariable("KEYRADAR_UPDATE_HEALTH_MARKER");
        if (string.IsNullOrWhiteSpace(marker))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(marker);
            var updatesRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyRadar",
                "updates"));
            var prefix = updatesRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "ok");
            Environment.SetEnvironmentVariable("KEYRADAR_UPDATE_HEALTH_MARKER", null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The updater will time out and restore the previous version.
        }
    }

}
