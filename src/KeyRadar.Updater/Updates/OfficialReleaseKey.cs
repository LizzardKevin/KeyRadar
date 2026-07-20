namespace KeyRadar.Updater.Updates;

public static class OfficialReleaseKey
{
    public const string Base64 = "pHtoARBv7ubIc8MU7nlMTU1ZAWeDs6nYA5TV1byu7kY=";

    public static byte[] GetBytes() => Convert.FromBase64String(Base64);
}
