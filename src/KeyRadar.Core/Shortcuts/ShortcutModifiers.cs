namespace KeyRadar.Shortcuts;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1 << 0,
    Shift = 1 << 1,
    Windows = 1 << 2,
    Alt = 1 << 3,
}
