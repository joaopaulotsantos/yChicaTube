using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views.Animations;

namespace yChicaTube;

[Activity(
    Theme = "@style/Maui.SplashTheme", 
    MainLauncher = true,
    Exported = true,
    Banner = "@drawable/banner",
    Icon = "@mipmap/appicon",
    RoundIcon = "@mipmap/appicon_round",
    ScreenOrientation = ScreenOrientation.Unspecified,
    LaunchMode = LaunchMode.SingleTop, 
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    new[] { Android.Content.Intent.ActionMain },
    Categories = new[] { Android.Content.Intent.CategoryLeanbackLauncher })]
public class MainActivity : MauiAppCompatActivity
{
    private FrameLayout? _nativeSplashView;
    private static MainActivity? _instance;
    private bool _isNativeSplashDismissed = false;

    public static void DismissNativeSplash()
    {
        _instance?.DismissNativeSplashInternal();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Services.StartupProfiler.Milestone("MainActivity.OnCreate Start");
        base.OnCreate(savedInstanceState);
        try
        {
            if (Services.DeviceModeService.IsTv)
            {
                RequestedOrientation = ScreenOrientation.Landscape;
            }

            // Iniciar o Splash Animado Nativo imediatamente (0ms de atraso)
            ShowNativeAnimatedSplash();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity.OnCreate] {ex.Message}");
        }
        Services.StartupProfiler.Milestone("MainActivity.OnCreate Finish");
    }

    private void ShowNativeAnimatedSplash()
    {
        try
        {
            _instance = this;
            _isNativeSplashDismissed = false;

            _nativeSplashView = new FrameLayout(this)
            {
                LayoutParameters = new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent)
            };

            // Fundo com Gradiente Mágico
            var gradient = new GradientDrawable(
                GradientDrawable.Orientation.TopBottom,
                new int[] {
                    Android.Graphics.Color.ParseColor("#064E3B"),
                    Android.Graphics.Color.ParseColor("#09131C"),
                    Android.Graphics.Color.ParseColor("#0F172A"),
                    Android.Graphics.Color.ParseColor("#1E1B4B")
                });
            _nativeSplashView.Background = gradient;

            var density = Resources?.DisplayMetrics?.Density ?? 1.0f;

            // Partículas / Estrelas Flutuantes
            var starEmojis = new[] { "✨", "⭐", "🌟", "🎈", "🌈", "💫" };
            var starCoords = new[]
            {
                new { X = -120f, Y = -160f, Size = 28f },
                new { X = 130f, Y = -130f, Size = 34f },
                new { X = -140f, Y = 130f, Size = 30f },
                new { X = 120f, Y = 160f, Size = 32f },
                new { X = 0f, Y = -200f, Size = 30f },
                new { X = -100f, Y = 180f, Size = 26f }
            };

            for (int i = 0; i < starCoords.Length; i++)
            {
                var coord = starCoords[i];
                var starTv = new TextView(this)
                {
                    Text = starEmojis[i % starEmojis.Length],
                    TextSize = coord.Size,
                    TranslationX = coord.X * density,
                    TranslationY = coord.Y * density,
                    Alpha = 0f
                };
                var starParams = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    ViewGroup.LayoutParams.WrapContent)
                {
                    Gravity = GravityFlags.Center
                };
                _nativeSplashView.AddView(starTv, starParams);

                int delay = i * 80;
                starTv.PostDelayed(() =>
                {
                    try
                    {
                        starTv.Animate()
                            ?.Alpha(0.9f)
                            ?.ScaleX(1.15f)
                            ?.ScaleY(1.15f)
                            ?.RotationBy(360f)
                            ?.TranslationYBy(-15f * density)
                            ?.SetDuration(1400);
                    }
                    catch { }
                }, delay);
            }

            // Conteúdo Central
            var centerLayout = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical
            };
            centerLayout.SetGravity(GravityFlags.Center);
            var centerParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Center
            };

            // Container do Mascote (Glow + Badge)
            var mascotContainer = new FrameLayout(this);
            var mascotContainerParams = new LinearLayout.LayoutParams(
                (int)(130 * density),
                (int)(130 * density))
            {
                Gravity = GravityFlags.CenterHorizontal
            };

            // Glow
            var glowView = new Android.Views.View(this);
            var glowShape = new GradientDrawable();
            glowShape.SetShape(ShapeType.Oval);
            glowShape.SetColor(Android.Graphics.Color.ParseColor("#10B981"));
            glowView.Background = glowShape;
            glowView.Alpha = 0.25f;
            var glowParams = new FrameLayout.LayoutParams(
                (int)(130 * density),
                (int)(130 * density))
            {
                Gravity = GravityFlags.Center
            };
            mascotContainer.AddView(glowView, glowParams);

            // Badge do Mascote 🐥
            var mascotBadge = new TextView(this)
            {
                Text = "🐥",
                TextSize = 52,
                Gravity = GravityFlags.Center
            };
            var badgeShape = new GradientDrawable();
            badgeShape.SetShape(ShapeType.Oval);
            badgeShape.SetColor(Android.Graphics.Color.ParseColor("#064E3B"));
            badgeShape.SetStroke((int)(3.5f * density), Android.Graphics.Color.ParseColor("#10B981"));
            mascotBadge.Background = badgeShape;
            var badgeParams = new FrameLayout.LayoutParams(
                (int)(105 * density),
                (int)(105 * density))
            {
                Gravity = GravityFlags.Center
            };
            mascotContainer.AddView(mascotBadge, badgeParams);
            centerLayout.AddView(mascotContainer, mascotContainerParams);

            // Título "yChicaTube"
            var titleLayout = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal
            };
            titleLayout.SetGravity(GravityFlags.CenterHorizontal);
            var titleParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = (int)(16 * density),
                Gravity = GravityFlags.CenterHorizontal
            };

            var titlePart1 = new TextView(this)
            {
                Text = "yChica",
                TextSize = 30,
                Typeface = Typeface.DefaultBold
            };
            titlePart1.SetTextColor(Android.Graphics.Color.ParseColor("#34D399"));

            var titlePart2 = new TextView(this)
            {
                Text = "Tube",
                TextSize = 30,
                Typeface = Typeface.DefaultBold
            };
            titlePart2.SetTextColor(Android.Graphics.Color.ParseColor("#FFFFFF"));

            titleLayout.AddView(titlePart1);
            titleLayout.AddView(titlePart2);
            centerLayout.AddView(titleLayout, titleParams);

            // Subtítulo
            var subtitle = new TextView(this)
            {
                Text = "Histórias mágicas e divertidas ✨",
                TextSize = 14,
                Gravity = GravityFlags.CenterHorizontal
            };
            subtitle.SetTextColor(Android.Graphics.Color.ParseColor("#94A3B8"));
            var subtitleParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = (int)(6 * density),
                Gravity = GravityFlags.CenterHorizontal
            };
            centerLayout.AddView(subtitle, subtitleParams);

            // Indicador de Carregamento
            var progressBar = new Android.Widget.ProgressBar(this)
            {
                Indeterminate = true
            };
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                progressBar.IndeterminateTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#10B981"));
            }
            var progressParams = new LinearLayout.LayoutParams(
                (int)(28 * density),
                (int)(28 * density))
            {
                TopMargin = (int)(18 * density),
                Gravity = GravityFlags.CenterHorizontal
            };
            centerLayout.AddView(progressBar, progressParams);

            _nativeSplashView.AddView(centerLayout, centerParams);

            // Animação imediata do Mascote (Salto elástico + wobble)
            mascotBadge.ScaleX = 0.2f;
            mascotBadge.ScaleY = 0.2f;
            mascotBadge.Alpha = 0f;
            titleLayout.Alpha = 0f;
            titleLayout.TranslationY = 20 * density;
            subtitle.Alpha = 0f;
            progressBar.Alpha = 0f;

            // Injetar na janela Android imediatamente
            AddContentView(_nativeSplashView, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

            // Disparar animações nativas instantâneas
            mascotBadge.Animate()
                ?.Alpha(1.0f)
                ?.ScaleX(1.15f)
                ?.ScaleY(1.15f)
                ?.SetDuration(450)
                ?.SetInterpolator(new OvershootInterpolator(2.2f))
                ?.WithEndAction(new Java.Lang.Runnable(() =>
                {
                    try
                    {
                        // Dança / Wobble
                        mascotBadge.Animate()
                            ?.RotationBy(-12f)
                            ?.SetDuration(120)
                            ?.WithEndAction(new Java.Lang.Runnable(() =>
                            {
                                mascotBadge.Animate()
                                    ?.RotationBy(24f)
                                    ?.SetDuration(140)
                                    ?.WithEndAction(new Java.Lang.Runnable(() =>
                                    {
                                        mascotBadge.Animate()
                                            ?.Rotation(0f)
                                            ?.ScaleX(1.0f)
                                            ?.ScaleY(1.0f)
                                            ?.SetDuration(120);
                                    }));
                            }));
                    }
                    catch { }
                }));

            glowView.Animate()
                ?.ScaleX(1.3f)
                ?.ScaleY(1.3f)
                ?.Alpha(0.5f)
                ?.SetDuration(600);

            titleLayout.Animate()
                ?.Alpha(1.0f)
                ?.TranslationY(0f)
                ?.SetDuration(400)
                ?.SetStartDelay(150);

            subtitle.Animate()
                ?.Alpha(1.0f)
                ?.SetDuration(400)
                ?.SetStartDelay(250);

            progressBar.Animate()
                ?.Alpha(1.0f)
                ?.SetDuration(400)
                ?.SetStartDelay(300);

            _nativeSplashView.Click += (s, e) => DismissNativeSplashInternal();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NativeSplash] {ex.Message}");
        }
    }

    private void DismissNativeSplashInternal()
    {
        if (_isNativeSplashDismissed || _nativeSplashView == null) return;
        _isNativeSplashDismissed = true;

        try
        {
            _nativeSplashView.Animate()
                ?.Alpha(0f)
                ?.ScaleX(1.04f)
                ?.ScaleY(1.04f)
                ?.SetDuration(400)
                ?.WithEndAction(new Java.Lang.Runnable(() =>
                {
                    try
                    {
                        if (_nativeSplashView?.Parent is ViewGroup parent)
                        {
                            parent.RemoveView(_nativeSplashView);
                        }
                        _nativeSplashView = null;
                    }
                    catch { }
                }));
        }
        catch { }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyImmersiveAndTheme();
        }
    }

    public static bool IsLegitimateExitRequested { get; set; } = false;

    public static event System.Func<Keycode, bool>? KeyPressed;
    public const int VoiceInputRequestCode = 1010;
    public static event System.Action<string>? VoiceResultReceived;

    private bool _isInitialized = false;

    protected override void OnResume()
    {
        base.OnResume();
        _isInitialized = true;
    }

    protected override void OnUserLeaveHint()
    {
        base.OnUserLeaveHint();
        if (_isInitialized && Services.DeviceModeService.IsTv && !IsLegitimateExitRequested)
        {
            BringToFront();
        }
    }

    protected override void OnStop()
    {
        base.OnStop();
        if (_isInitialized && Services.DeviceModeService.IsTv && !IsLegitimateExitRequested)
        {
            BringToFront();
        }
    }

    private void BringToFront()
    {
        try
        {
            var intent = new Android.Content.Intent(this, typeof(MainActivity));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTop | Android.Content.ActivityFlags.SingleTop);
            
            // PendingIntent executa com permissões do sistema para contornar restrições de background do Android 10+
            var pending = Android.App.PendingIntent.GetActivity(
                this, 
                0, 
                intent, 
                Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);
            
            pending?.Send();
            StartActivity(intent);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity.BringToFront] {ex.Message}");
        }
    }

    public static bool StartVoiceRecognition(string prompt, string? languageCode = null)
    {
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity is null) return false;

            var intent = new Android.Content.Intent(Android.Speech.RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguageModel, Android.Speech.RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPrompt, prompt);
            if (!string.IsNullOrWhiteSpace(languageCode))
            {
                intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguage, languageCode);
            }

            activity.StartActivityForResult(intent, VoiceInputRequestCode);
            return true;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceRecognitionError] {ex.Message}");
            return false;
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VoiceInputRequestCode && resultCode == Result.Ok && data is not null)
        {
            var matches = data.GetStringArrayListExtra(Android.Speech.RecognizerIntent.ExtraResults);
            if (matches is not null && matches.Count > 0)
            {
                var text = matches[0];
                if (!string.IsNullOrWhiteSpace(text))
                {
                    VoiceResultReceived?.Invoke(text);
                }
            }
        }
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e != null && e.Action == KeyEventActions.Down)
        {
            if (KeyPressed != null)
            {
                foreach (System.Func<Keycode, bool> handler in KeyPressed.GetInvocationList())
                {
                    try
                    {
                        if (handler(e.KeyCode))
                        {
                            return true;
                        }
                    }
                    catch { }
                }
            }

            // Na Android TV, impede que a tecla Voltar feche a aplicação no ecrã principal
            if (e.KeyCode == Keycode.Back && Services.DeviceModeService.IsTv && !IsLegitimateExitRequested)
            {
                return true;
            }
        }
        return base.DispatchKeyEvent(e);
    }

    private void ApplyImmersiveAndTheme()
    {
        try
        {
            if (DeviceInfo.Idiom == DeviceIdiom.TV)
            {
                return;
            }

            if (Window is not null)
            {
                Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#FFF9F2"));
                Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#FFF9F2"));

                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    if (Window.InsetsController is not null)
                    {
                        Window.InsetsController.Hide(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
                        Window.InsetsController.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                        Window.InsetsController.SetSystemBarsAppearance(
                            (int)WindowInsetsControllerAppearance.LightStatusBars | (int)WindowInsetsControllerAppearance.LightNavigationBars,
                            (int)WindowInsetsControllerAppearance.LightStatusBars | (int)WindowInsetsControllerAppearance.LightNavigationBars);
                    }
                }
                else if (Window.DecorView is not null)
                {
#pragma warning disable CS0618
                    Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                        SystemUiFlags.Fullscreen |
                        SystemUiFlags.HideNavigation |
                        SystemUiFlags.ImmersiveSticky |
                        SystemUiFlags.LightStatusBar |
                        SystemUiFlags.LightNavigationBar);
#pragma warning restore CS0618
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity.ApplyImmersiveAndTheme] {ex.Message}");
        }
    }
}
