namespace Rightpad.Receiver;

internal sealed class StartupViewModel : ObservableModel
{
    private readonly StartupRegistration registration;
    private bool enabled;
    private string notice = "";

    public bool Enabled => enabled;
    public string Notice { get => notice; private set => Set(ref notice, value); }

    public StartupViewModel(StartupRegistration registration)
    {
        this.registration = registration;
        Apply(registration.Read());
    }

    public void SetEnabled(bool value) => Apply(registration.SetEnabled(value));

    private void Apply(StartupState state)
    {
        enabled = state.Enabled;
        Changed(nameof(Enabled)); // Reassert registry truth after a failed UI toggle.
        Notice = state.Notice;
    }
}
