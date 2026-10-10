namespace yChicaTube.Services;

public enum BiometricAuthResult
{
    Success,
    Failed,
    FallbackToPin,
    NotAvailable
}

public interface IBiometricAuthService
{
    Task<bool> IsBiometricAvailableAsync();
    Task<BiometricAuthResult> AuthenticateAsync(string title, string subtitle, string negativeButtonText);
}

public class DefaultBiometricAuthService : IBiometricAuthService
{
    public Task<bool> IsBiometricAvailableAsync() => Task.FromResult(false);
    public Task<BiometricAuthResult> AuthenticateAsync(string title, string subtitle, string negativeButtonText) 
        => Task.FromResult(BiometricAuthResult.NotAvailable);
}
