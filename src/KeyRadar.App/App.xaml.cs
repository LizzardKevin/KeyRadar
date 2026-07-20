using KeyRadar.Rules;
using KeyRadar.Windows.Foreground;
using KeyRadar.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KeyRadar;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private ForegroundOverlayWindow? _overlayWindow;
    private DispatcherQueueTimer? _foregroundTimer;
    private GlobalShortcutObserver? _shortcutObserver;
    private int _lastForegroundProcessId;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _overlayWindow = new ForegroundOverlayWindow();
        _mainWindow = new MainWindow();
        _mainWindow.Closed += MainWindow_Closed;
        _mainWindow.Activate();

        _shortcutObserver = new GlobalShortcutObserver();
        _shortcutObserver.GestureObserved += ShortcutObserver_GestureObserved;
        try
        {
            _shortcutObserver.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _shortcutObserver.Dispose();
            _shortcutObserver = null;
        }

        _foregroundTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _foregroundTimer.Interval = TimeSpan.FromMilliseconds(250);
        _foregroundTimer.IsRepeating = true;
        _foregroundTimer.Tick += ForegroundTimer_Tick;
        _foregroundTimer.Start();
    }

    private void ShortcutObserver_GestureObserved(object? sender, ShortcutGestureObservedEventArgs args) =>
        _mainWindow?.ShowObservedGesture(args.Gesture);

    private void ForegroundTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var foreground = ForegroundApplicationReader.Read();
        if (foreground is null)
        {
            HideOverlay();
            return;
        }

        var rules = ApplicationRuleMatcher.FindByExecutableName(
            BuiltInRuleCatalog.Load(),
            foreground.ExecutableName);
        var shouldShow = ForegroundOverlayPolicy.ShouldShow(
            Environment.ProcessId,
            foreground.ProcessId,
            rules is { Shortcuts.Count: > 0 });

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
        if (_shortcutObserver is not null)
        {
            _shortcutObserver.GestureObserved -= ShortcutObserver_GestureObserved;
            _shortcutObserver.Dispose();
            _shortcutObserver = null;
        }
        _overlayWindow = null;
        _mainWindow = null;
        Exit();
    }
}
