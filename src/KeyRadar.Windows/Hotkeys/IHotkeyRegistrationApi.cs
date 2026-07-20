using KeyRadar.Shortcuts;

namespace KeyRadar.Windows.Hotkeys;

public interface IHotkeyRegistrationApi
{
    HotkeyRegistrationAttempt TryRegister(int identifier, ShortcutGesture gesture);

    void Unregister(int identifier);
}
