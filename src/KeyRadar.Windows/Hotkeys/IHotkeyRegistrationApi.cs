using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Hotkeys;

public interface IHotkeyRegistrationApi
{
    HotkeyRegistrationResult TryRegister(int identifier, HotkeyGesture gesture);

    void Unregister(int identifier);
}
