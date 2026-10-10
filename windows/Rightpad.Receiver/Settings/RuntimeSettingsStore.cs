namespace Rightpad.Receiver;

internal sealed class RuntimeSettingsStore
{
    private RuntimeSettings current;
    private Cz1Parameters requestedCz1;
    public Cz1Parameters RequestedCz1 => Volatile.Read(ref requestedCz1);
    public event Action? Cz1Published;
    public LiveSensitivity Sensitivity { get; }
    public event Action? Published;
    public RuntimeSettingsStore(RuntimeSettings? initial = null)
    {
        current = initial ?? RuntimeSettings.Default;
        current.ValidateCore();
        requestedCz1 = current.Cz1;
        Sensitivity = new(current.SensitivityX, current.SensitivityY);
    }
    public RuntimeSettings Current => Volatile.Read(ref current);
    public void Publish(RuntimeSettings settings)
    {
        settings.ValidateCore();
        var previous = Interlocked.Exchange(ref current, settings);
        Sensitivity.Publish(settings.SensitivityX, settings.SensitivityY);
        // An unrelated Save must not undo a memory-only C Apply.
        if (previous.Cz1 != settings.Cz1) ApplyCz1(settings.Cz1);
        Published?.Invoke();
    }
    public void ApplyCz1(Cz1Parameters parameters)
    {
        parameters.Validate();
        Interlocked.Exchange(ref requestedCz1, parameters);
        Cz1Published?.Invoke();
    }
}
