using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using KeyRadar.Rules;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace KeyRadar;

public sealed partial class ForegroundOverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080L;
    private const long NoActivateStyle = 0x08000000L;
    private bool _positioned;

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    public ForegroundOverlayWindow()
    {
        InitializeComponent();
        HotkeyItems.ItemsSource = Hotkeys;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        ApplyNoActivateStyle();
    }

    public void ShowFor(ApplicationVariantRule rules, int processId)
    {
        ApplicationNameText.Text = rules.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name);
        Hotkeys.Clear();

        foreach (var hotkey in rules.Hotkeys)
        {
            Hotkeys.Add(HotkeyRowViewModel.Create(
                hotkey.Gesture.ToString(),
                hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                hotkey.Scope,
                hotkey.Confidence,
                processId));
        }

        var height = Math.Clamp(94 + (Hotkeys.Count * 49), 180, 420);
        AppWindow.Resize(new SizeInt32(380, height));
        PositionOnPrimaryWorkArea();
        AppWindow.Show(false);
    }

    public void HideOverlay() => AppWindow.Hide();

    private void PositionOnPrimaryWorkArea()
    {
        if (_positioned)
        {
            return;
        }

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        AppWindow.Move(new PointInt32(workArea.X + workArea.Width - 404, workArea.Y + 24));
        _positioned = true;
    }

    private void ApplyNoActivateStyle()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var currentStyle = GetWindowLongPtr(windowHandle, ExtendedStyleIndex).ToInt64();
        _ = SetWindowLongPtr(
            windowHandle,
            ExtendedStyleIndex,
            new nint(currentStyle | ToolWindowStyle | NoActivateStyle));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint value);
}
