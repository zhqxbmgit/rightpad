namespace Rightpad.Receiver;

internal sealed class RuntimeSettingsStore
{
    private RuntimeSettings current;
    public LiveSensitivity Sensitivity { get; }
    public RuntimeSettingsStore(RuntimeSettings? initial = null)
    {
        current = initial ?? RuntimeSettings.Default;
        current.ValidateCore();
        Sensitivity = new(current.SensitivityX, current.SensitivityY);
    }
    public RuntimeSettings Current => Volatile.Read(ref current);
    public void Publish(RuntimeSettings settings)
    {
        settings.ValidateCore();
        Interlocked.Exchange(ref current, settings);
        Sensitivity.Publish(settings.SensitivityX, settings.SensitivityY);
    }
}
