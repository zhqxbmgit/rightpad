namespace Rightpad.Receiver;

internal sealed class RuntimeSettingsStore
{
    private RuntimeSettings current;
    public RuntimeSettingsStore(RuntimeSettings? initial = null)
    {
        current = initial ?? RuntimeSettings.Default;
        current.ValidateCore();
    }
    public RuntimeSettings Current => Volatile.Read(ref current);
    public void Publish(RuntimeSettings settings)
    {
        settings.ValidateCore();
        Interlocked.Exchange(ref current, settings);
    }
}
