#if ANDROID
using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Java.Lang;
using yChicaTube.Services;

namespace yChicaTube.Platforms.Android;

public class AndroidBiometricAuthService : IBiometricAuthService
{
    public Task<bool> IsBiometricAvailableAsync()
    {
        try
        {
            if (DeviceInfo.Idiom == DeviceIdiom.TV)
            {
                return Task.FromResult(false);
            }

            var activity = Platform.CurrentActivity;
            if (activity is null) return Task.FromResult(false);

            var pm = activity.PackageManager;
            if (pm is null || !pm.HasSystemFeature(global::Android.Content.PM.PackageManager.FeatureFingerprint))
            {
                return Task.FromResult(false);
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                var bm = (BiometricManager?)activity.GetSystemService(Context.BiometricService);
                if (bm is not null)
                {
                    var canAuth = bm.CanAuthenticate();
                    return Task.FromResult(canAuth == 0); // BiometricManager.BIOMETRIC_SUCCESS = 0
                }
            }
            else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
#pragma warning disable CS0618
                var fpm = (global::Android.Hardware.Fingerprints.FingerprintManager?)activity.GetSystemService(Context.FingerprintService);
                if (fpm is not null && fpm.IsHardwareDetected && fpm.HasEnrolledFingerprints)
                {
                    return Task.FromResult(true);
                }
                var km = (KeyguardManager?)activity.GetSystemService(Context.KeyguardService);
                if (km is not null && km.IsDeviceSecure)
                {
                    return Task.FromResult(true);
                }
#pragma warning restore CS0618
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Biometrics.IsAvailable] {ex.Message}");
        }

        return Task.FromResult(false);
    }

    public Task<BiometricAuthResult> AuthenticateAsync(string title, string subtitle, string negativeButtonText)
    {
        if (DeviceInfo.Idiom == DeviceIdiom.TV)
        {
            return Task.FromResult(BiometricAuthResult.NotAvailable);
        }

        var tcs = new TaskCompletionSource<BiometricAuthResult>();
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            return Task.FromResult(BiometricAuthResult.NotAvailable);
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
        {
            try
            {
                var callback = new BiometricAuthenticationCallback(tcs);
                var cancellationSignal = new CancellationSignal();

                var builder = new BiometricPrompt.Builder(activity)
                    .SetTitle(title)
                    .SetSubtitle(subtitle)
                    .SetNegativeButton(negativeButtonText, activity.MainExecutor, new DialogClickListener(tcs));

                var prompt = builder.Build();
                prompt.Authenticate(cancellationSignal, activity.MainExecutor, callback);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Biometrics.Authenticate] {ex.Message}");
                tcs.TrySetResult(BiometricAuthResult.FallbackToPin);
            }
        }
        else
        {
            tcs.TrySetResult(BiometricAuthResult.FallbackToPin);
        }

        return tcs.Task;
    }

    private class DialogClickListener : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        private readonly TaskCompletionSource<BiometricAuthResult> _tcs;
        public DialogClickListener(TaskCompletionSource<BiometricAuthResult> tcs) => _tcs = tcs;
        public void OnClick(IDialogInterface? dialog, int which)
        {
            _tcs.TrySetResult(BiometricAuthResult.FallbackToPin);
        }
    }

    private class BiometricAuthenticationCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<BiometricAuthResult> _tcs;

        public BiometricAuthenticationCallback(TaskCompletionSource<BiometricAuthResult> tcs)
        {
            _tcs = tcs;
        }

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
        {
            base.OnAuthenticationSucceeded(result);
            _tcs.TrySetResult(BiometricAuthResult.Success);
        }

        public override void OnAuthenticationFailed()
        {
            base.OnAuthenticationFailed();
        }

        public override void OnAuthenticationError(BiometricErrorCode errorCode, ICharSequence? errString)
        {
            base.OnAuthenticationError(errorCode, errString);
            _tcs.TrySetResult(BiometricAuthResult.FallbackToPin);
        }
    }
}
#endif
