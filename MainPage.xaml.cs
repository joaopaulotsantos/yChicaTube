using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using yChicaTube.Localization;
using yChicaTube.Services;
using yChicaTube.Services.Remote;
using yChicaTube.Services.Remote.Models;

namespace yChicaTube;

public partial class MainPage : ContentPage
{
    private const string VideosKey = "approved-videos";
    private const string DurationKey = "meal-duration-minutes";
    private const string PinKey = "parent-pin";
    private const string LanguageKey = "search-language-code";
    private const string Language2Key = "search-language-code-2";
    private const string Language3Key = "search-language-code-3";
    private const string BiometricEnabledKey = "use-biometric-auth";
    private const string AllowChildDiscoverKey = "allow-child-discover";
    private const string CurationModeKey = "curation-mode"; // "approved" or "themes"
    private const string AllowedThemesKey = "allowed-themes";
    private const string TermsAcceptedKey = "terms-accepted-v1";
    private const string OnboardingCompletedKey = "has_seen_onboarding_v1";
    private int _tvTermsCol = 0;
    private int _tvOnboardingBtn = 1; // 0 = Anterior, 1 = Seguinte/Começar, 2 = Saltar
    private int _tvPreviewCol = 0; // 0 = Aprovar, 1 = Bloquear, 2 = Enviar TV, 3 = Fechar
    private int _tvInspectRow = 1; // 0 = Fechar topo, 1 = Botão Aprovar, 2 = Lista de vídeos, 3 = OK rodapé
    private int _tvInspectVideoIndex = 0;
    private int _tvInspectCol = 0; // 0 = Testar Áudio, 1 = Bloquear
    private int _currentOnboardingSlide = 1;

    private string _curationMode = "approved";
    private string _activeSelectedTheme = "";
    private readonly ObservableCollection<ApprovedVideo> _videos = new();
    private readonly ObservableCollection<ApprovedVideo> _childDisplayVideos = new();
    private readonly ObservableCollection<ApprovedVideo> _tvContinueVideos = new();
    private readonly ObservableCollection<ApprovedVideo> _tvFavoriteVideos = new();
    private readonly ObservableCollection<TvCategoryItem> _tvCategories = new();
    private readonly ObservableCollection<ParentVideoItem> _parentDisplayVideos = new();
    private readonly ObservableCollection<TvSuggestionItem> _suggestions = new();
    private readonly ObservableCollection<ThemeChipItem> _parentThemes = new();
    private readonly ObservableCollection<ThemeChipItem> _childThemeChips = new();
    private readonly ObservableCollection<ApprovedVideo> _playerSuggestionVideos = new();
    private readonly Dictionary<string, List<ApprovedVideo>> _themeVideosCache = new(StringComparer.OrdinalIgnoreCase);

    // Gestão de Playlists Autorizadas (Batch Approval & Mistura Automática)
    private const string ApprovedPlaylistsKey = "app_approved_playlists_v2";
    private readonly ObservableCollection<ApprovedPlaylist> _approvedPlaylists = new();
    private readonly ObservableCollection<PlaylistCardItem> _discoveredPlaylists = new();
    private readonly ObservableCollection<YoutubeSearchResult> _inspectingPlaylistVideos = new();
    private string _currentInspectingPlaylistName = "";

    // Gestão de Vídeos Ocultados / Bloqueados dos Temas
    private const string BlockedVideosKey = "blocked_videos_v1";
    private readonly ObservableCollection<BlockedVideoItem> _blockedVideos = new();
    private readonly HashSet<string> _blockedVideoIds = new(StringComparer.OrdinalIgnoreCase);
    private ParentVideoItem? _currentPreviewingVideo;

    // Coleções para o Sistema de Controlo Remoto Wi-Fi (TV & Telemóvel)
    private readonly ObservableCollection<TvDevice> _mobilePairedTvs = new();
    private readonly ObservableCollection<TvDiscoveryPacket> _discoveredTvs = new();
    private readonly ObservableCollection<PairedClientInfo> _tvPairedClients = new();
    private IDispatcherTimer? _tvPairingCountdownTimer;
    private List<ApprovedVideo>? _pendingVideosToSendToTv;

    private readonly IDispatcherTimer _timer;
    private DateTime _sessionEndsAt;
    private CancellationTokenSource? _suggestCts;
    private ApprovedVideo? _tvHeroVideo;
    private string _selectedTvCategory = "all";
    private bool _isVoiceForTheme = false;
    private int _pendingMinutesToAdd = 0;

    // TV Remote Navigation State - Home
    private int _tvFocusRow = 1; // 0=Header, 1=Hero, 2=Continue, 3=Favorites, 4=Categories, 5=Empty
    private int _tvHeaderColIndex = 0; // 0.._tvCategories.Count-1 (Category pills), _tvCategories.Count (Adults Button)
    private int _tvContinueColIndex = 0;
    private int _tvFavoritesColIndex = 0;
    private int _tvCategoriesColIndex = 0;

    // TV Remote Navigation State - PIN Prompt
    private int _tvPinRow = 0; // 0..3 (keypad 1-9, back, 0, submit), 4 (biometric), 5 (cancel/enter), 6 (top cancel)
    private int _tvPinCol = 1; // 0..2

    // TV Remote Navigation State - Adults Panel (0..10: Left Column, 20..21: Right Column)
    private int _tvParentRow = 2; // 0: Header, 1: Discover/Tabs, 2..: Content | 20: Videos, 21: Load More
    private int _tvParentCol = 0;
    private int _tvParentSuggestionIndex = 0;
    private int _tvParentThemeIndex = 0;
    private int _tvParentVideoIndex = 0;

    // Mobile Remote Auth Watcher
    private bool _isCheckingTvAuth = false;
    private bool _isPromptingRemoteAuth = false;
    private readonly Dictionary<string, DateTime> _lastTvAuthPromptTimes = new();

    // TV Remote Navigation State - Time Expired Screen
    private int _tvExpiredRow = 0; // 0: Add time (+5, +15, +30), 1: Adult Area, Quit
    private int _tvExpiredCol = 1; // Default no botão +15m (recomendado)

    // TV Remote Navigation State - Player
    private int _tvPlayerColIndex = 1; // 0: Back, 1: Play/Pause, 2: Suggestions Toggle
    private int _tvPlayerSuggestionIndex = 0;
    private bool _tvPlayerInSuggestions = false;

    // TV Remote Navigation State - Blocked Videos in Settings
    private int _tvBlockedVideoIndex = 0;

#if ANDROID
    private readonly Services.IBiometricAuthService _biometricService = new Platforms.Android.AndroidBiometricAuthService();
#else
    private readonly Services.IBiometricAuthService _biometricService = new Services.DefaultBiometricAuthService();
#endif

    public MainPage()
    {
        StartupProfiler.Milestone("MainPage Constructor Start");
        InitializeComponent();
        SetLandscape(false);

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += OnTimerTick;

        StartupProfiler.Milestone("LoadVideos Start");
        LoadVideos();
        StartupProfiler.Milestone("LoadVideos Finish");

        VideoList.ItemsSource = _childDisplayVideos;
        TvContinueList.ItemsSource = _tvContinueVideos;
        TvFavoritesList.ItemsSource = _tvFavoriteVideos;
        TvCategoriesList.ItemsSource = _tvCategories;
        ParentUnifiedVideoList.ItemsSource = _parentDisplayVideos;
        SuggestionsList.ItemsSource = _suggestions;
        ParentThemesList.ItemsSource = _parentThemes;
        ChildThemesList.ItemsSource = _childThemeChips;
        PlayerSuggestionsList.ItemsSource = _playerSuggestionVideos;
        if (BlockedVideosList is not null) BlockedVideosList.ItemsSource = _blockedVideos;
        if (PlaylistInspectVideosList is not null) PlaylistInspectVideosList.ItemsSource = _inspectingPlaylistVideos;
        LoadBlockedVideos();

        // Binds do sistema de controlo remoto Wi-Fi
        if (DiscoveredTvsModalList is not null) DiscoveredTvsModalList.ItemsSource = _discoveredTvs;
        if (MobilePairedTvsList is not null) MobilePairedTvsList.ItemsSource = _mobilePairedTvs;
        if (TvPairedClientsList is not null) TvPairedClientsList.ItemsSource = _tvPairedClients;

        LanguagePicker.ItemsSource = YoutubeSearchService.AvailableLanguages.Select(l => l.DisplayName).ToList();
        if (LanguagePicker2 is not null)
            LanguagePicker2.ItemsSource = YoutubeSearchService.AvailableSecondaryLanguages.Select(l => l.DisplayName).ToList();
        if (LanguagePicker3 is not null)
            LanguagePicker3.ItemsSource = YoutubeSearchService.AvailableSecondaryLanguages.Select(l => l.DisplayName).ToList();
        
        // Deteta automaticamente o idioma do sistema caso ainda não esteja guardado
        var defaultSearchLang = GetDefaultSearchLanguageCode();
        var savedLang1 = Preferences.Default.Get(LanguageKey, defaultSearchLang);
        var savedLang2 = Preferences.Default.Get(Language2Key, "none");
        var savedLang3 = Preferences.Default.Get(Language3Key, "none");
        AppStrings.CurrentLanguage = savedLang1;

        var lang1Index = YoutubeSearchService.AvailableLanguages.FindIndex(l => l.Code.Equals(savedLang1, StringComparison.OrdinalIgnoreCase));
        LanguagePicker.SelectedIndex = lang1Index >= 0 ? lang1Index : 0;

        if (LanguagePicker2 is not null)
        {
            var lang2Index = YoutubeSearchService.AvailableSecondaryLanguages.FindIndex(l => l.Code.Equals(savedLang2, StringComparison.OrdinalIgnoreCase));
            LanguagePicker2.SelectedIndex = lang2Index >= 0 ? lang2Index : 0;
        }

        if (LanguagePicker3 is not null)
        {
            var lang3Index = YoutubeSearchService.AvailableSecondaryLanguages.FindIndex(l => l.Code.Equals(savedLang3, StringComparison.OrdinalIgnoreCase));
            LanguagePicker3.SelectedIndex = lang3Index >= 0 ? lang3Index : 0;
        }

        // Inicializa as configurações e toggles visuais
        _isBiometricEnabled = Preferences.Default.Get(BiometricEnabledKey, true);
        _isChildDiscoverAllowed = Preferences.Default.Get(AllowChildDiscoverKey, false);
        _curationMode = Preferences.Default.Get(CurationModeKey, "approved");

        UpdateBiometricToggleUI();
        UpdateChildDiscoverToggleUI();
        LoadAllowedThemes();
        UpdateCurationModeUI();
        PreloadThemesInBackground();

        StartupProfiler.Milestone("InitTvCategories");
        InitTvCategories();
        LoadDefaultSuggestions();
        LoadParentDisplayVideos();
        _mealDurationMinutes = (int)Preferences.Default.Get(DurationKey, 15d);
        UpdateMealDurationUI();

        // Inicializa o subsistema de controlo remoto / servidor Wi-Fi
        InitTvRemoteSystem();

        // Tarefas não-críticas em background assíncrono para arranque ultrarrápido
        Task.Run(async () =>
        {
            await Task.Delay(200);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CheckBiometricAvailabilityAsync();
            });
        });

        AdaptLayoutForScreen();
        SetupTvFocusHandlers();
#if ANDROID
        MainActivity.KeyPressed += OnTvRemoteKeyPressed;
        MainActivity.VoiceResultReceived += OnVoiceInputReceived;
#endif
        StartupProfiler.Milestone("MainPage Constructor Finish");
    }

    private async void CheckBiometricAvailabilityAsync()
    {
        try
        {
            if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
            {
                if (BiometricOptionView is not null) BiometricOptionView.IsVisible = false;
                return;
            }

            var isAvailable = await _biometricService.IsBiometricAvailableAsync();
            if (BiometricOptionView is not null)
            {
                BiometricOptionView.IsVisible = isAvailable;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Biometrics.Check] {ex.Message}");
        }
    }

    private async void OnParentsClicked(object sender, EventArgs e)
    {
        var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
        if (isTv)
        {
            var pairedPhones = TvPairingManager.Instance.GetPairedClients();
            if (pairedPhones.Count > 0)
            {
                // Dispara o pedido de autorização imediata para os telemóveis emparelhados
                TvServerService.Instance.RequestAuthFromPhone(TimeSpan.FromMinutes(2));

                // Mostra ecrã a aguardar autorização do telemóvel dos pais (com PIN disponível)
                if (TvRemoteAuthPromptOverlay is not null)
                {
                    if (BtnTvAuthUsePinFallback is not null) BtnTvAuthUsePinFallback.IsVisible = true;
                    if (TvEmergencyBanner is not null) TvEmergencyBanner.IsVisible = false;
                    TvRemoteAuthPromptOverlay.IsVisible = true;
                    _tvRemoteAuthCol = 0; // Foca no Usar PIN como alternativa rápida
                    UpdateTvRemoteAuthVisualFocus();
                    return;
                }
            }

            ShowPinPrompt();
            return;
        }

        // Modo Telemóvel: Autentica com Biometria do telemóvel
        var isBiometricEnabled = Preferences.Default.Get(BiometricEnabledKey, true);
        if (isBiometricEnabled && await _biometricService.IsBiometricAvailableAsync())
        {
            var result = await _biometricService.AuthenticateAsync(
                AppStrings.Get("BiometricTitle"),
                AppStrings.Get("BiometricSubtitle"),
                AppStrings.Get("BiometricNegativeButton")
            );

            if (result == Services.BiometricAuthResult.Success)
            {
                OpenParentAreaDirectly();
                return;
            }
            
            // Se o utilizador cancelou, falhou ou escolheu usar PIN, abre o PIN prompt
            ShowPinPrompt();
            return;
        }

        ShowPinPrompt();
    }

    private async void ShowPinPrompt()
    {
        PinEntryBox.Text = string.Empty;
        PinErrorLabel.IsVisible = false;

        var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
        PinEntryBox.IsReadOnly = isTv;
        var canUseBiometrics = !isTv && Preferences.Default.Get(BiometricEnabledKey, true) && await _biometricService.IsBiometricAvailableAsync();
        TriggerBiometricBtn.IsVisible = canUseBiometrics;

        PinPromptOverlay.IsVisible = true;
        _tvPinRow = 0;
        _tvPinCol = 1;
        UpdateTvPinVisualFocus();
        if (!isTv)
        {
            PinEntryBox.Focus();
        }
    }

    private void OpenParentAreaDirectly()
    {
        PinPromptOverlay.IsVisible = false;
        PinEntryBox.Unfocus();
        PinEntryBox.Text = string.Empty;
        PinErrorLabel.IsVisible = false;
        if (string.IsNullOrWhiteSpace(SearchEntry.Text))
        {
            LoadParentDisplayVideos();
        }
        AdaptLayoutForScreen(Width, Height);
        SelectAdultTab("stories");
        ParentOverlay.IsVisible = true;
        _tvParentRow = 2;
        _tvParentCol = 0;
        _tvParentSuggestionIndex = 0;
        _tvParentVideoIndex = 0;
        UpdateTvParentVisualFocus();
    }

    private async void OnTriggerBiometricClicked(object? sender, EventArgs e)
    {
        var result = await _biometricService.AuthenticateAsync(
            AppStrings.Get("BiometricTitle"),
            AppStrings.Get("BiometricSubtitle"),
            AppStrings.Get("BiometricNegativeButton")
        );

        if (result == Services.BiometricAuthResult.Success)
        {
            if (_pendingMinutesToAdd > 0)
            {
                var mins = _pendingMinutesToAdd;
                _pendingMinutesToAdd = 0;
                PinPromptOverlay.IsVisible = false;
                PinEntryBox.Unfocus();
                PinEntryBox.Text = string.Empty;
                PinErrorLabel.IsVisible = false;
                ApplyMinutesExtension(mins);
            }
            else
            {
                OpenParentAreaDirectly();
            }
        }
    }

    private void UpdateBiometricToggleUI()
    {
        if (BiometricToggle is null || BiometricThumb is null) return;
        BiometricToggle.BackgroundColor = _isBiometricEnabled ? Color.FromArgb("#10B981") : Color.FromArgb("#CBD5E1");
        BiometricThumb.HorizontalOptions = _isBiometricEnabled ? LayoutOptions.End : LayoutOptions.Start;

        if (BiometricStateLabel is not null)
        {
            BiometricStateLabel.Text = _isBiometricEnabled ? AppStrings.Get("StatusOn") : AppStrings.Get("StatusOff");
            BiometricStateLabel.TextColor = _isBiometricEnabled ? Color.FromArgb("#10B981") : Color.FromArgb("#64748B");
        }
    }

    private void OnBiometricToggleTapped(object? sender, EventArgs e)
    {
        _isBiometricEnabled = !_isBiometricEnabled;
        Preferences.Default.Set(BiometricEnabledKey, _isBiometricEnabled);
        UpdateBiometricToggleUI();
    }

    private void OnCancelPinPromptClicked(object sender, EventArgs e)
    {
        _pendingMinutesToAdd = 0;
        PinPromptOverlay.IsVisible = false;
        PinEntryBox.Text = string.Empty;
        PinErrorLabel.IsVisible = false;
    }

    private void OnPinKeypadDigitClicked(object? sender, EventArgs e)
    {
        var digit = (sender as Button)?.CommandParameter?.ToString();
        if (!string.IsNullOrEmpty(digit) && (PinEntryBox.Text?.Length ?? 0) < 4)
        {
            PinEntryBox.Text = (PinEntryBox.Text ?? string.Empty) + digit;
        }
    }

    private void OnPinKeypadBackspaceClicked(object? sender, EventArgs e)
    {
        var text = PinEntryBox.Text ?? string.Empty;
        if (text.Length > 0)
        {
            PinEntryBox.Text = text[..^1];
        }
    }

    private void OnPinEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        PinErrorLabel.IsVisible = false;
        if (e.NewTextValue?.Length == 4)
        {
            OnSubmitPinClicked(sender, EventArgs.Empty);
        }
    }

    private void OnSubmitPinClicked(object? sender, EventArgs e)
    {
        var pin = PinEntryBox.Text?.Trim();
        var correctPin = Preferences.Default.Get(PinKey, "1234");
        if (pin == correctPin)
        {
            if (_pendingMinutesToAdd > 0)
            {
                var mins = _pendingMinutesToAdd;
                _pendingMinutesToAdd = 0;
                PinPromptOverlay.IsVisible = false;
                PinEntryBox.Unfocus();
                PinEntryBox.Text = string.Empty;
                PinErrorLabel.IsVisible = false;
                ApplyMinutesExtension(mins);
            }
            else
            {
                OpenParentAreaDirectly();
            }
        }
        else if (pin?.Length == 4 || sender is Button)
        {
            PinErrorLabel.IsVisible = true;
            PinEntryBox.Text = string.Empty;
            PinEntryBox.Focus();
        }
    }

    private static string GetDefaultSearchLanguageCode()
    {
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name.ToLowerInvariant();
        var twoLetter = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (name.StartsWith("pt-br") || name == "pt_br") return "pt-BR";
        if (twoLetter == "es") return "es";
        if (twoLetter == "en") return "en";
        if (twoLetter == "pt") return "pt-PT";
        return "en";
    }

    private void LoadParentDisplayVideos()
    {
        _currentSearchQuery = string.Empty;
        _currentSearchPage = 1;
        if (LoadMoreSearchContainer is not null)
        {
            LoadMoreSearchContainer.IsVisible = false;
        }

        _parentDisplayVideos.Clear();
        foreach (var v in _videos)
        {
            _parentDisplayVideos.Add(new ParentVideoItem
            {
                Id = v.Id,
                Title = v.Title,
                IsApproved = true
            });
        }
        ListSectionTitle.Text = AppStrings.Get("AuthorizedStoriesCount", _videos.Count);
        EmptyParentListLabel.IsVisible = _parentDisplayVideos.Count == 0;
        EmptyParentListLabel.Text = AppStrings.Get("EmptyParentListHint");
    }

    private string GetSelectedLanguageCode()
    {
        var index = LanguagePicker?.SelectedIndex ?? -1;
        if (index >= 0 && index < YoutubeSearchService.AvailableLanguages.Count)
            return YoutubeSearchService.AvailableLanguages[index].Code;
        return Preferences.Default.Get(LanguageKey, GetDefaultSearchLanguageCode());
    }

    private string GetSelectedLanguage2Code()
    {
        var index = LanguagePicker2?.SelectedIndex ?? -1;
        if (index >= 0 && index < YoutubeSearchService.AvailableSecondaryLanguages.Count)
            return YoutubeSearchService.AvailableSecondaryLanguages[index].Code;
        return Preferences.Default.Get(Language2Key, "none");
    }

    private string GetSelectedLanguage3Code()
    {
        var index = LanguagePicker3?.SelectedIndex ?? -1;
        if (index >= 0 && index < YoutubeSearchService.AvailableSecondaryLanguages.Count)
            return YoutubeSearchService.AvailableSecondaryLanguages[index].Code;
        return Preferences.Default.Get(Language3Key, "none");
    }

    private List<string> GetSelectedLanguageCodes()
    {
        var list = new List<string> { GetSelectedLanguageCode() };
        var l2 = GetSelectedLanguage2Code();
        if (!string.IsNullOrWhiteSpace(l2) && !l2.Equals("none", StringComparison.OrdinalIgnoreCase) && !list.Contains(l2))
        {
            list.Add(l2);
        }
        var l3 = GetSelectedLanguage3Code();
        if (!string.IsNullOrWhiteSpace(l3) && !l3.Equals("none", StringComparison.OrdinalIgnoreCase) && !list.Contains(l3))
        {
            list.Add(l3);
        }
        return list;
    }

    private void OnLanguagePickerChanged(object? sender, EventArgs e)
    {
        var code = GetSelectedLanguageCode();
        Preferences.Default.Set(LanguageKey, code);
        AppStrings.CurrentLanguage = code;
        RefreshAfterLanguagePreferencesChanged();
    }

    private void OnLanguagePicker2Changed(object? sender, EventArgs e)
    {
        var code = GetSelectedLanguage2Code();
        Preferences.Default.Set(Language2Key, code);
        RefreshAfterLanguagePreferencesChanged();
    }

    private void OnLanguagePicker3Changed(object? sender, EventArgs e)
    {
        var code = GetSelectedLanguage3Code();
        Preferences.Default.Set(Language3Key, code);
        RefreshAfterLanguagePreferencesChanged();
    }

    private void RefreshAfterLanguagePreferencesChanged()
    {
        LoadDefaultSuggestions();
        LoadParentDisplayVideos();
        LoadAllowedThemes();
        InitTvCategories();
        UpdateTimeExpiredUI();
        if (_curationMode == "themes")
        {
            _ = LoadThemeVideosForChild(_activeSelectedTheme);
        }
        UpdateTvUI();
    }

    private void LoadDefaultSuggestions()
    {
        _suggestions.Clear();
        var list = YoutubeSearchService.GetSuggestionsAsync(string.Empty, GetSelectedLanguageCodes()).GetAwaiter().GetResult();
        foreach (var s in list)
        {
            _suggestions.Add(new TvSuggestionItem(s));
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = e.NewTextValue?.Trim() ?? string.Empty;
        ClearSearchBtn.IsVisible = !string.IsNullOrEmpty(text);

        if (string.IsNullOrEmpty(text))
        {
            LoadParentDisplayVideos();
            return;
        }

        _suggestCts?.Cancel();
        _suggestCts = new CancellationTokenSource();
        var token = _suggestCts.Token;
        var langCodes = GetSelectedLanguageCodes();

        Task.Delay(300, token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;

            var list = await YoutubeSearchService.GetSuggestionsAsync(text, langCodes);
            if (!token.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _suggestions.Clear();
                    foreach (var item in list)
                    {
                        _suggestions.Add(new TvSuggestionItem(item));
                    }
                });
            }
        }, TaskScheduler.Default);
    }

    private void OnClearSearchClicked(object? sender, EventArgs e)
    {
        SearchEntry.Text = string.Empty;
        SearchEntry.Unfocus();
        ClearSearchBtn.IsVisible = false;
        LoadDefaultSuggestions();
        LoadParentDisplayVideos();
    }

    private void OnVoiceSearchClicked(object? sender, EventArgs e)
    {
        _isVoiceForTheme = false;
#if ANDROID
        var currentLang = GetSelectedLanguageCode();
        MainActivity.StartVoiceRecognition(AppStrings.Get("VoiceSearchPrompt") ?? "Fale o que deseja pesquisar...", currentLang);
#endif
    }

    private void OnVoiceThemeClicked(object? sender, EventArgs e)
    {
        _isVoiceForTheme = true;
#if ANDROID
        var currentLang = GetSelectedLanguageCode();
        MainActivity.StartVoiceRecognition(AppStrings.Get("VoiceSearchPrompt") ?? "Fale o tema desejado...", currentLang);
#endif
    }

    private void OnClearNewThemeClicked(object? sender, EventArgs e)
    {
        if (NewThemeEntry is not null)
        {
            NewThemeEntry.Text = string.Empty;
            NewThemeEntry.Unfocus();
        }
        if (ClearThemeTextBtn is not null)
        {
            ClearThemeTextBtn.IsVisible = false;
        }
    }

    private void OnNewThemeTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ClearThemeTextBtn is not null)
        {
            ClearThemeTextBtn.IsVisible = !string.IsNullOrWhiteSpace(e.NewTextValue);
        }
    }

    private void OnVoiceInputReceived(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ParentOverlay.IsVisible)
            {
                if (_isVoiceForTheme)
                {
                    _isVoiceForTheme = false;
                    if (NewThemeEntry is not null)
                    {
                        NewThemeEntry.Text = text;
                    }
                    if (ClearThemeTextBtn is not null)
                    {
                        ClearThemeTextBtn.IsVisible = true;
                    }
                    OnAddNewThemeClicked(null, EventArgs.Empty);
                }
                else
                {
                    SearchEntry.Text = text;
                    ClearSearchBtn.IsVisible = true;
                    OnSearchVideosClicked(null, EventArgs.Empty);
                }
            }
        });
    }

    private void OnSuggestionTapped(object? sender, TappedEventArgs e)
    {
        string? suggestion = null;
        if (e.Parameter is TvSuggestionItem tsi) suggestion = tsi.Text;
        else if (e.Parameter is string s) suggestion = s;
        else if (sender is BindableObject bo && bo.BindingContext is TvSuggestionItem ctxTsi) suggestion = ctxTsi.Text;
        else if (sender is BindableObject bo2 && bo2.BindingContext is string ctxS) suggestion = ctxS;

        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            SearchEntry.Text = suggestion;
            ClearSearchBtn.IsVisible = true;
            OnSearchVideosClicked(this, EventArgs.Empty);
        }
    }

    private bool IsScreenLocked()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity is not null)
        {
            var am = (Android.App.ActivityManager?)activity.GetSystemService(Android.Content.Context.ActivityService);
            if (am is not null && Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M)
            {
                return am.LockTaskModeState != Android.App.LockTaskMode.None;
            }
        }
#endif
        return false;
    }

    private async void OnCloseParentsClicked(object sender, EventArgs e)
    {
#if ANDROID
        if (DeviceInfo.Idiom != DeviceIdiom.TV && !DeviceModeService.IsTv && !IsScreenLocked())
        {
            var lockChoice = await DisplayAlert(
                AppStrings.Get("DialogLockScreenTitle"),
                AppStrings.Get("DialogLockScreenMsg"),
                AppStrings.Get("BtnYesLock"),
                AppStrings.Get("BtnNoJustClose")
            );

            if (lockChoice)
            {
                var activity = Platform.CurrentActivity;
                try
                {
                    activity?.StartLockTask();
                    ParentOverlay.IsVisible = false;
                    await DisplayAlert(
                        AppStrings.Get("DialogLockSuccessTitle"),
                        AppStrings.Get("DialogLockSuccessMsg"),
                        AppStrings.Get("BtnUnderstood")
                    );
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LockTaskError] {ex.Message}");
                }
            }
        }
#endif
        ParentOverlay.IsVisible = false;
    }

    private string _currentSearchQuery = string.Empty;
    private int _currentSearchPage = 1;

    private async void OnSearchVideosClicked(object? sender, EventArgs e)
    {
        var query = SearchEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            LoadParentDisplayVideos();
            return;
        }

        // Se o utilizador colou um link direto do YouTube
        if (YoutubePlayerHtml.TryGetVideoId(query, out var directId))
        {
            _currentSearchQuery = string.Empty;
            if (LoadMoreSearchContainer is not null) LoadMoreSearchContainer.IsVisible = false;

            _parentDisplayVideos.Clear();
            _parentDisplayVideos.Add(new ParentVideoItem
            {
                Id = directId,
                Title = AppStrings.Get("DirectLinkTitle"),
                IsApproved = _videos.Any(v => v.Id == directId)
            });
            ListSectionTitle.Text = AppStrings.Get("DirectLinkRecognized");
            EmptyParentListLabel.IsVisible = false;
            return;
        }

        try
        {
            SearchIndicator.IsVisible = true;
            SearchIndicator.IsRunning = true;
            EmptyParentListLabel.IsVisible = false;
            if (LoadMoreSearchContainer is not null) LoadMoreSearchContainer.IsVisible = false;

            _currentSearchQuery = query;
            _currentSearchPage = 1;

            var results = await YoutubeSearchService.SearchPageAsync(query, GetSelectedLanguageCodes(), page: 1, maxResults: 30);

            _parentDisplayVideos.Clear();
            foreach (var r in results)
            {
                if (_blockedVideoIds.Contains(r.Id)) continue;
                _parentDisplayVideos.Add(new ParentVideoItem
                {
                    Id = r.Id,
                    Title = r.Title,
                    IsApproved = _videos.Any(v => v.Id == r.Id)
                });
            }

            ListSectionTitle.Text = AppStrings.Get("SearchResultsCount", query, _parentDisplayVideos.Count);
            if (_parentDisplayVideos.Count == 0)
            {
                var activeLangs = GetSelectedLanguageCodes();
                var langNames = string.Join(", ", activeLangs.Select(c => YoutubeSearchService.GetLanguage(c).DisplayName));
                EmptyParentListLabel.Text = AppStrings.Get("NoResultsForSelectedLanguage", query, langNames);
                EmptyParentListLabel.IsVisible = true;
                if (LoadMoreSearchContainer is not null) LoadMoreSearchContainer.IsVisible = false;
            }
            else
            {
                EmptyParentListLabel.IsVisible = false;
                if (LoadMoreSearchContainer is not null)
                {
                    LoadMoreSearchContainer.IsVisible = true;
                    LoadMoreSearchBtn.Text = AppStrings.Get("BtnLoadMoreStories");
                    LoadMoreSearchBtn.IsEnabled = true;
                }
            }
        }
        catch (Exception ex)
        {
            EmptyParentListLabel.Text = AppStrings.Get("SearchErrorMessage");
            EmptyParentListLabel.IsVisible = true;
            System.Diagnostics.Debug.WriteLine($"[SearchError] {ex.Message}");
        }
        finally
        {
            SearchIndicator.IsRunning = false;
            SearchIndicator.IsVisible = false;
        }
    }

    private async void OnLoadMoreSearchResultsClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentSearchQuery)) return;

        try
        {
            LoadMoreSearchBtn.IsEnabled = false;
            LoadMoreSearchBtn.Text = AppStrings.Get("LoadingMoreStories");
            LoadMoreSearchIndicator.IsVisible = true;
            LoadMoreSearchIndicator.IsRunning = true;

            _currentSearchPage++;
            var nextResults = await YoutubeSearchService.SearchPageAsync(_currentSearchQuery, GetSelectedLanguageCodes(), page: _currentSearchPage, maxResults: 25);

            var existingIds = _parentDisplayVideos.Select(p => p.Id).ToHashSet();
            var addedCount = 0;

            foreach (var r in nextResults)
            {
                if (_blockedVideoIds.Contains(r.Id)) continue;
                if (existingIds.Add(r.Id))
                {
                    _parentDisplayVideos.Add(new ParentVideoItem
                    {
                        Id = r.Id,
                        Title = r.Title,
                        IsApproved = _videos.Any(v => v.Id == r.Id)
                    });
                    addedCount++;
                }
            }

            ListSectionTitle.Text = AppStrings.Get("SearchResultsCount", _currentSearchQuery, _parentDisplayVideos.Count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadMoreError] {ex.Message}");
        }
        finally
        {
            LoadMoreSearchIndicator.IsRunning = false;
            LoadMoreSearchIndicator.IsVisible = false;
            LoadMoreSearchBtn.Text = AppStrings.Get("BtnLoadMoreStories");
            LoadMoreSearchBtn.IsEnabled = true;
        }
    }

    private bool _isChildDiscoverAllowed = false;
    private bool _isBiometricEnabled = true;

    private void UpdateChildDiscoverToggleUI()
    {
        if (AllowChildDiscoverToggle is null || AllowChildDiscoverThumb is null) return;
        AllowChildDiscoverToggle.BackgroundColor = _isChildDiscoverAllowed ? Color.FromArgb("#10B981") : Color.FromArgb("#CBD5E1");
        AllowChildDiscoverThumb.HorizontalOptions = _isChildDiscoverAllowed ? LayoutOptions.End : LayoutOptions.Start;

        if (AllowChildDiscoverStateLabel is not null)
        {
            AllowChildDiscoverStateLabel.Text = _isChildDiscoverAllowed ? AppStrings.Get("StatusOn") : AppStrings.Get("StatusOff");
            AllowChildDiscoverStateLabel.TextColor = _isChildDiscoverAllowed ? Color.FromArgb("#10B981") : Color.FromArgb("#64748B");
        }

        UpdateChildDiscoverButtonVisibility();
    }

    private void OnAllowChildDiscoverToggleTapped(object? sender, EventArgs e)
    {
        _isChildDiscoverAllowed = !_isChildDiscoverAllowed;
        Preferences.Default.Set(AllowChildDiscoverKey, _isChildDiscoverAllowed);
        UpdateChildDiscoverToggleUI();
    }

    private void UpdateChildDiscoverButtonVisibility()
    {
        if (DiscoverMoreContainer is null) return;
        DiscoverMoreContainer.IsVisible = _isChildDiscoverAllowed && _videos.Count > 0 && !_isSessionExpired;
    }

    private async void OnDiscoverMoreClicked(object? sender, EventArgs e)
    {
        if (_videos.Count == 0 || _isSessionExpired) return;

        try
        {
            DiscoverIndicator.IsVisible = true;
            DiscoverIndicator.IsRunning = true;
            DiscoverMoreBtn.IsEnabled = false;

            await Task.Delay(300);

            // Baralha de forma mágica APENAS os vídeos previamente aprovados pelos adultos
            var shuffled = _videos.OrderBy(_ => Random.Shared.Next()).ToList();
            _childDisplayVideos.Clear();
            foreach (var video in shuffled)
            {
                _childDisplayVideos.Add(video);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiscoverError] {ex.Message}");
        }
        finally
        {
            DiscoverIndicator.IsRunning = false;
            DiscoverIndicator.IsVisible = false;
            DiscoverMoreBtn.IsEnabled = true;
        }
    }

    private static string ExtractCleanTheme(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "desenhos animados";

        var parts = title.Split(new[] { '|', '-', '–', '—', ':', '(', '[', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var firstPart = parts.Length > 0 ? parts[0].Trim() : title.Trim();

        var cleanWords = firstPart.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Equals("hd", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("4k", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("completo", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("completa", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("oficial", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("episodio", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("episodios", StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals("temporada", StringComparison.OrdinalIgnoreCase))
            .Take(4);

        var result = string.Join(" ", cleanWords).Trim();
        return string.IsNullOrWhiteSpace(result) ? firstPart : result;
    }

    private void OnToggleApprovalClicked(object? sender, EventArgs e)
    {
        ParentVideoItem? item = null;
        if (sender is Button btn && btn.CommandParameter is ParentVideoItem pvi)
        {
            item = pvi;
        }
        else if (sender is BindableObject bo && bo.BindingContext is ParentVideoItem ctxItem)
        {
            item = ctxItem;
        }
        else if (e is TappedEventArgs tea && tea.Parameter is ParentVideoItem tapItem)
        {
            item = tapItem;
        }

        if (item is null) return;

        if (item.IsApproved)
        {
            // Já estava aprovado -> Remover da biblioteca
            var match = _videos.FirstOrDefault(v => v.Id == item.Id);
            if (match != null)
            {
                _videos.Remove(match);
            }
            var childMatch = _childDisplayVideos.FirstOrDefault(v => v.Id == item.Id && !v.IsDiscovered);
            if (childMatch != null)
            {
                _childDisplayVideos.Remove(childMatch);
            }
            item.IsApproved = false;

            // Se estivermos na vista da biblioteca (sem pesquisa ativa), remove da lista visível
            if (string.IsNullOrWhiteSpace(SearchEntry?.Text))
            {
                _parentDisplayVideos.Remove(item);
                ListSectionTitle.Text = AppStrings.Get("AuthorizedStoriesCount", _videos.Count);
                EmptyParentListLabel.IsVisible = _parentDisplayVideos.Count == 0;
            }
        }
        else
        {
            // Não estava aprovado -> Adicionar à biblioteca
            if (!_videos.Any(v => v.Id == item.Id))
            {
                var approved = new ApprovedVideo(item.Id, item.Title);
                _videos.Add(approved);
                _childDisplayVideos.Add(approved);
            }
            item.IsApproved = true;
        }

        SaveVideos();
        UpdateEmptyState();
        UpdateChildDiscoverButtonVisibility();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        AdaptLayoutForScreen(width, height);
    }

    private void AdaptLayoutForScreen(double width = 0, double height = 0)
    {
        var w = width > 0 ? width : Width;
        if (w <= 0) return;

        if (VideoListLayout is not null)
        {
            // Deteção inteligente e separação estrita de TV vs Mobile
            var span = DeviceModeService.GetOptimalGridSpan(w);
            VideoListLayout.Span = span;
        }

        if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
        {
            if (HeaderView is not null) HeaderView.IsVisible = false;
            if (LibraryView is not null) LibraryView.IsVisible = false;
            if (TvHomeContainer is not null) TvHomeContainer.IsVisible = !_isSessionExpired;
            if (BiometricOptionView is not null) BiometricOptionView.IsVisible = false;
            if (ScreenLockContainer is not null) ScreenLockContainer.IsVisible = false;
            if (VoiceSearchBtn is not null) VoiceSearchBtn.IsVisible = false;
            if (VoiceThemeBtn is not null) VoiceThemeBtn.IsVisible = false;
            UpdateTvUI();
        }
        else
        {
            if (TvHomeContainer is not null) TvHomeContainer.IsVisible = false;
            if (HeaderView is not null) HeaderView.IsVisible = true;
            if (LibraryView is not null) LibraryView.IsVisible = !_isSessionExpired;
            if (TvRemoteHintBanner is not null) TvRemoteHintBanner.IsVisible = false;
            if (ScreenLockContainer is not null) ScreenLockContainer.IsVisible = true;
            if (VoiceSearchBtn is not null) VoiceSearchBtn.IsVisible = true;
            if (VoiceThemeBtn is not null) VoiceThemeBtn.IsVisible = true;
        }

        // Adaptação dinâmica e centrada da Área de Adultos para telemóveis e ecrãs largos
        if (AdultTabStoriesView is not null && ParentLeftColumn is not null && ParentRightColumn is not null)
        {
            if (w < 850 && !DeviceModeService.IsTv)
            {
                // Modo Mobile / Ecrã Estreito: Layout Vertical Centrado no meio do ecrã
                AdultTabStoriesView.ColumnDefinitions.Clear();
                AdultTabStoriesView.RowDefinitions.Clear();
                AdultTabStoriesView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                AdultTabStoriesView.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                AdultTabStoriesView.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                Grid.SetColumn(ParentLeftColumn, 0);
                Grid.SetRow(ParentLeftColumn, 0);

                Grid.SetColumn(ParentRightColumn, 0);
                Grid.SetRow(ParentRightColumn, 1);

                AdultTabStoriesView.MaximumWidthRequest = 620;
                AdultTabStoriesView.HorizontalOptions = LayoutOptions.Center;
            }
            else
            {
                // Modo TV / Tablet / Ecrã Panorâmico: 2 Colunas Lado a Lado Centradas
                AdultTabStoriesView.ColumnDefinitions.Clear();
                AdultTabStoriesView.RowDefinitions.Clear();
                AdultTabStoriesView.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(420)));
                AdultTabStoriesView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                AdultTabStoriesView.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                Grid.SetColumn(ParentLeftColumn, 0);
                Grid.SetRow(ParentLeftColumn, 0);

                Grid.SetColumn(ParentRightColumn, 1);
                Grid.SetRow(ParentRightColumn, 0);

                AdultTabStoriesView.MaximumWidthRequest = 1200;
                AdultTabStoriesView.HorizontalOptions = LayoutOptions.Center;
            }
        }
    }


    private async void OnSavePinClicked(object sender, EventArgs e)
    {
        var pin = NewPinEntry.Text?.Trim();
        if (pin?.Length != 4 || !pin.All(char.IsDigit))
        {
            await DisplayAlert(AppStrings.Get("PinInvalidTitle"), AppStrings.Get("PinInvalidMessage"), AppStrings.Get("BtnOk"));
            return;
        }
        Preferences.Default.Set(PinKey, pin);
        NewPinEntry.Text = string.Empty;
        await DisplayAlert(AppStrings.Get("PinUpdatedTitle"), AppStrings.Get("PinUpdatedMessage"), AppStrings.Get("BtnOk"));
    }

    private bool _isLandscape = true;

    private void SetLandscape(bool landscape)
    {
        _isLandscape = landscape;
#if ANDROID
        try
        {
            if (DeviceInfo.Idiom == DeviceIdiom.TV) return;

            var activity = Platform.CurrentActivity;
            if (activity is not null)
            {
                activity.RequestedOrientation = landscape
                    ? Android.Content.PM.ScreenOrientation.SensorLandscape
                    : Android.Content.PM.ScreenOrientation.Portrait;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetLandscape] {ex.Message}");
        }
#endif
    }

    private void SetFullScreen(bool fullscreen)
    {
#if ANDROID
        try
        {
            if (DeviceInfo.Idiom == DeviceIdiom.TV) return;

            var activity = Platform.CurrentActivity;
            if (activity?.Window is not null)
            {
                var window = activity.Window;
                if (fullscreen)
                {
                    window.SetStatusBarColor(Android.Graphics.Color.Black);
                    window.SetNavigationBarColor(Android.Graphics.Color.Black);
                    if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
                    {
                        window.InsetsController?.Hide(Android.Views.WindowInsets.Type.StatusBars() | Android.Views.WindowInsets.Type.NavigationBars());
                    }
                    else
                    {
#pragma warning disable CS0618
                        window.DecorView.SystemUiVisibility = (Android.Views.StatusBarVisibility)(
                            Android.Views.SystemUiFlags.Fullscreen |
                            Android.Views.SystemUiFlags.HideNavigation |
                            Android.Views.SystemUiFlags.ImmersiveSticky);
#pragma warning restore CS0618
                    }
                }
                else
                {
                    window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#FFF9F2"));
                    window.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#FFF9F2"));
                    if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
                    {
                        window.InsetsController?.Show(Android.Views.WindowInsets.Type.StatusBars() | Android.Views.WindowInsets.Type.NavigationBars());
                    }
                    else
                    {
#pragma warning disable CS0618
                        window.DecorView.SystemUiVisibility = Android.Views.StatusBarVisibility.Visible;
#pragma warning restore CS0618
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetFullScreen] {ex.Message}");
        }
#endif
    }

    private CancellationTokenSource? _hideControlsCts;

    private void ScheduleAutoHideControls()
    {
        _hideControlsCts?.Cancel();
        _hideControlsCts = new CancellationTokenSource();
        var token = _hideControlsCts.Token;
        Task.Delay(4000, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        if (PlayerView.IsVisible)
                        {
                            if (PlayerTopBar is not null && PlayerTopBar.IsVisible)
                            {
                                await PlayerTopBar.FadeTo(0, 250);
                                PlayerTopBar.IsVisible = false;
                            }
                            if (ControlsBar is not null && ControlsBar.IsVisible)
                            {
                                await ControlsBar.FadeTo(0, 250);
                                ControlsBar.IsVisible = false;
                            }
                        }
                    }
                    catch { }
                });
            }
        }, TaskScheduler.Default);
    }

    private async void ShowControls()
    {
        _hideControlsCts?.Cancel();
        try
        {
            if (PlayerTopBar is not null && !PlayerTopBar.IsVisible)
            {
                PlayerTopBar.Opacity = 0;
                PlayerTopBar.IsVisible = true;
                _ = PlayerTopBar.FadeTo(1, 250);
            }
            if (ControlsBar is not null && !ControlsBar.IsVisible)
            {
                ControlsBar.Opacity = 0;
                ControlsBar.IsVisible = true;
                await ControlsBar.FadeTo(1, 250);
            }
        }
        catch { }
        ScheduleAutoHideControls();
    }

    private void OnPlayerScreenTapped(object? sender, TappedEventArgs e)
    {
        if (ControlsBar.IsVisible || (PlayerTopBar is not null && PlayerTopBar.IsVisible))
        {
            OnHideControlsClicked(this, EventArgs.Empty);
        }
        else
        {
            ShowControls();
        }
    }

    private void OnPlayerSwiped(object? sender, SwipedEventArgs e)
    {
        ShowControls();
    }

    private async void OnHideControlsClicked(object sender, EventArgs e)
    {
        _hideControlsCts?.Cancel();
        try
        {
            if (PlayerTopBar is not null && PlayerTopBar.IsVisible)
            {
                _ = PlayerTopBar.FadeTo(0, 200);
                PlayerTopBar.IsVisible = false;
            }
            if (ControlsBar is not null && ControlsBar.IsVisible)
            {
                await ControlsBar.FadeTo(0, 200);
                ControlsBar.IsVisible = false;
            }
        }
        catch { }
    }

    private void SetupTvFocusHandlers()
    {
        if (DeviceModeService.IsTv)
        {
            UpdateTvVisualFocus();
        }
    }

    private void InitTvCategories()
    {
        _tvCategories.Clear();
        _tvCategories.Add(new TvCategoryItem("all", AppStrings.Get("TvCategoryAll").Replace("✨", "").Trim(), "✨"));
        
        if (_parentThemes.Count > 0)
        {
            foreach (var theme in _parentThemes)
            {
                var icon = "🐱";
                var tLow = theme.Name.ToLowerInvariant();
                if (tLow.Contains("panda")) icon = "🐼";
                else if (tLow.Contains("peppa") || tLow.Contains("porquinha")) icon = "🐷";
                else if (tLow.Contains("pata") || tLow.Contains("canina") || tLow.Contains("patrulha")) icon = "🐶";
                else if (tLow.Contains("música") || tLow.Contains("musica") || tLow.Contains("cancoes") || tLow.Contains("canções") || tLow.Contains("maria") || tLow.Contains("cantigas")) icon = "🎵";
                else if (tLow.Contains("urso") || tLow.Contains("masha")) icon = "🐻";
                else if (tLow.Contains("carro") || tLow.Contains("wings") || tLow.Contains("super")) icon = "🚀";
                else if (tLow.Contains("ruca") || tLow.Contains("pocoyo")) icon = "⭐";
                else if (tLow.Contains("bob") || tLow.Contains("construtor")) icon = "🔨";
                else if (tLow.Contains("ovelha") || tLow.Contains("choné") || tLow.Contains("chone")) icon = "🐑";
                else if (tLow.Contains("galinha") || tLow.Contains("pintadinha")) icon = "🐔";
                else if (tLow.Contains("bita") || tLow.Contains("monica") || tLow.Contains("mônica")) icon = "🎈";
                else if (tLow.Contains("zenon") || tLow.Contains("granja")) icon = "🐮";
                _tvCategories.Add(new TvCategoryItem(theme.Name, theme.Name, icon));
            }
        }
        else
        {
            _tvCategories.Add(new TvCategoryItem("animals", AppStrings.Get("TvCategoryAnimals").Replace("🦁", "").Trim(), "🦁"));
            _tvCategories.Add(new TvCategoryItem("space", AppStrings.Get("TvCategorySpace").Replace("🚀", "").Trim(), "🚀"));
            _tvCategories.Add(new TvCategoryItem("learn", AppStrings.Get("TvCategoryLearn").Replace("🧠", "").Trim(), "🧠"));
            _tvCategories.Add(new TvCategoryItem("music", AppStrings.Get("TvCategoryMusic").Replace("🎵", "").Trim(), "🎵"));
            _tvCategories.Add(new TvCategoryItem("creativity", AppStrings.Get("TvCategoryCreativity").Replace("🎨", "").Trim(), "🎨"));
            _tvCategories.Add(new TvCategoryItem("fun", AppStrings.Get("TvCategoryFun").Replace("😂", "").Trim(), "😂"));
        }

        PopulateTvHeaderCategories();
    }

    private void PopulateTvHeaderCategories()
    {
        if (TvHeaderCategories is null) return;
        TvHeaderCategories.Children.Clear();

        foreach (var cat in _tvCategories)
        {
            var isSelected = cat.Id.Equals(_selectedTvCategory, StringComparison.OrdinalIgnoreCase);
            var border = new Border
            {
                BackgroundColor = isSelected ? Color.FromArgb("#10B981") : Color.FromArgb("#1E293B"),
                Stroke = isSelected ? Color.FromArgb("#34D399") : Color.FromArgb("#334155"),
                StrokeThickness = isSelected ? 2.5 : 1.5,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
                Padding = new Thickness(14, 6),
                VerticalOptions = LayoutOptions.Center
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => FilterTvCategory(cat.Id);
            border.GestureRecognizers.Add(tap);

            var stack = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
            stack.Children.Add(new Label { Text = cat.Icon, FontSize = 16, VerticalOptions = LayoutOptions.Center });
            stack.Children.Add(new Label 
            { 
                Text = cat.Name, 
                FontSize = 14, 
                FontAttributes = FontAttributes.Bold, 
                TextColor = isSelected ? Colors.White : Color.FromArgb("#94A3B8"), 
                VerticalOptions = LayoutOptions.Center 
            });

            border.Content = stack;
            TvHeaderCategories.Children.Add(border);
        }
    }

    private void FilterTvCategory(string categoryId)
    {
        _selectedTvCategory = categoryId;
        PopulateTvHeaderCategories();
        if (_curationMode == "themes")
        {
            var allThemesChipName = AppStrings.Get("ThemeChipAll");
            var targetTheme = categoryId.Equals("all", StringComparison.OrdinalIgnoreCase) ? allThemesChipName : categoryId;
            _ = LoadThemeVideosForChild(targetTheme);
        }
        else
        {
            UpdateTvUI();
        }
        UpdateTvVisualFocus();
    }

    private void UpdateTvUI()
    {
        if (TvHomeContainer is null) return;

        var sourceVideos = _curationMode == "themes" ? _childDisplayVideos.ToList() : _videos.ToList();

        if (sourceVideos.Count == 0)
        {
            if (_curationMode == "themes" && _parentThemes.Count > 0)
            {
                _ = LoadThemeVideosForChild(_activeSelectedTheme);
            }

            if (TvHeroCard is not null) TvHeroCard.IsVisible = false;
            if (TvContinueRail is not null) TvContinueRail.IsVisible = false;
            if (TvFavoritesRail is not null) TvFavoritesRail.IsVisible = false;
            if (TvCategoriesRail is not null) TvCategoriesRail.IsVisible = false;
            if (TvEmptyStateView is not null) TvEmptyStateView.IsVisible = true;
            _tvFocusRow = 5;
            UpdateTvVisualFocus();
            return;
        }

        if (TvEmptyStateView is not null) TvEmptyStateView.IsVisible = false;

        // Filtra vídeos para TV
        List<ApprovedVideo> filtered;
        if (_curationMode == "themes" || string.Equals(_selectedTvCategory, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = sourceVideos;
        }
        else
        {
            filtered = FilterVideosByTheme(sourceVideos, _selectedTvCategory);
            if (filtered.Count == 0) filtered = sourceVideos; // Fallback amigável
        }

        _tvContinueVideos.Clear();
        foreach (var v in filtered)
        {
            _tvContinueVideos.Add(v.Clone());
        }

        _tvFavoriteVideos.Clear();
        foreach (var v in filtered.Reverse<ApprovedVideo>())
        {
            _tvFavoriteVideos.Add(v.Clone());
        }

        _tvHeroVideo = _tvContinueVideos.FirstOrDefault() ?? sourceVideos.FirstOrDefault();
        if (_tvHeroVideo != null && TvHeroCard is not null)
        {
            TvHeroCard.IsVisible = true;
            if (TvHeroImage is not null) TvHeroImage.Source = _tvHeroVideo.ThumbnailUrl;
            if (TvHeroTitleLabel is not null) TvHeroTitleLabel.Text = _tvHeroVideo.Title;
            if (TvHeroSubtitleLabel is not null) TvHeroSubtitleLabel.Text = _tvHeroVideo.BadgeText;
        }

        if (TvContinueRail is not null) TvContinueRail.IsVisible = _tvContinueVideos.Count > 0;
        if (TvFavoritesRail is not null) TvFavoritesRail.IsVisible = _tvFavoriteVideos.Count > 0;
        if (TvCategoriesRail is not null) TvCategoriesRail.IsVisible = true;

        UpdateTvVisualFocus();
    }

    private static List<ApprovedVideo> FilterVideosByTheme(IEnumerable<ApprovedVideo> list, string theme)
    {
        var matchesTheme = list.Where(v => v.Title.Contains(theme, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matchesTheme.Count > 0) return matchesTheme;

        var terms = theme switch
        {
            "animals" => new[] { "animal", "ovelha", "cao", "cão", "gato", "leao", "leão", "vaca", "porco", "panda", "peixe", "bichos", "patrulha" },
            "space" => new[] { "espaco", "espaço", "planeta", "estrela", "foguetao", "foguetão", "lua", "sol", "universo", "astronauta" },
            "learn" => new[] { "aprender", "numero", "número", "cor", "cores", "forma", "letra", "abc", "alfabeto", "escola", "contar" },
            "music" => new[] { "musica", "música", "cancao", "canção", "cantiga", "cancoes", "canções", "roda", "rima", "danca", "dança", "som" },
            "creativity" => new[] { "pintar", "desenho", "plasticina", "arte", "construir", "colorir", "fazer", "brinquedo" },
            "fun" => new[] { "diversao", "diversão", "rir", "brincar", "piada", "aventura", "ruca", "pocoyo", "noddy", "chone" },
            _ => Array.Empty<string>()
        };

        if (terms.Length == 0) return list.ToList();

        var matches = list.Where(v => terms.Any(t => v.Title.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        return matches.Count > 0 ? matches : list.ToList();
    }

    private void OnTvHeroTapped(object? sender, EventArgs e)
    {
        if (_tvHeroVideo != null)
        {
            StartSession(_tvHeroVideo);
        }
    }

    private void OnTvVideoSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is ApprovedVideo video)
        {
            if (sender is CollectionView cv) cv.SelectedItem = null;
            StartSession(video);
        }
    }

    private void OnTvCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is ApprovedVideo video || (sender is BindableObject bo && bo.BindingContext is ApprovedVideo vCtx && (video = vCtx) is not null))
        {
            StartSession(video);
        }
    }

    private void OnTvCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is TvCategoryItem cat)
        {
            if (sender is CollectionView cv) cv.SelectedItem = null;
            FilterTvCategory(cat.Id);
        }
    }

    private void OnTvCategoryCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is TvCategoryItem cat || (sender is BindableObject bo && bo.BindingContext is TvCategoryItem cCtx && (cat = cCtx) is not null))
        {
            FilterTvCategory(cat.Id);
        }
    }

    private bool IsTvRowAvailable(int row)
    {
        return row switch
        {
            0 => true, // Top Header & Categories & Adults
            1 => TvHeroCard is not null && TvHeroCard.IsVisible && _tvHeroVideo != null,
            2 => TvContinueRail is not null && TvContinueRail.IsVisible && _tvContinueVideos.Count > 0,
            3 => TvFavoritesRail is not null && TvFavoritesRail.IsVisible && _tvFavoriteVideos.Count > 0,
            4 => TvCategoriesRail is not null && TvCategoriesRail.IsVisible && _tvCategories.Count > 0,
            5 => TvEmptyStateView is not null && TvEmptyStateView.IsVisible,
            _ => false
        };
    }

    private void UpdateTvVisualFocus()
    {
        if (!DeviceModeService.IsTv && DeviceInfo.Idiom != DeviceIdiom.TV) return;

        // 0. Linha 0: Header (Pílulas de Categoria + Botão de Adultos)
        if (TvHeaderCategories is not null)
        {
            for (int i = 0; i < TvHeaderCategories.Children.Count; i++)
            {
                if (TvHeaderCategories.Children[i] is Border catBorder)
                {
                    var isCatFocused = (_tvFocusRow == 0 && _tvHeaderColIndex == i);
                    SetVisualTvFocus(catBorder, isCatFocused);
                }
            }
        }
        var isAdultsFocused = (_tvFocusRow == 0 && _tvHeaderColIndex >= _tvCategories.Count);
        SetVisualTvFocus(TvAdultsBtnBorder, isAdultsFocused);

        // 1. Linha 1: Hero Spotlight
        SetVisualTvFocus(TvHeroWatchBtn, _tvFocusRow == 1);
        if (TvHeroCard is not null)
        {
            if (_tvFocusRow == 1)
            {
                TvHeroCard.Stroke = Color.FromArgb("#FACC15");
                TvHeroCard.StrokeThickness = 4.0;
                TvHeroCard.ScaleTo(1.02, 100, Easing.CubicOut);
            }
            else
            {
                TvHeroCard.Stroke = Color.FromArgb("#334155");
                TvHeroCard.StrokeThickness = 2.0;
                TvHeroCard.ScaleTo(1.0, 100, Easing.CubicIn);
            }
        }

        // 5. Linha 5: Botão Vazio
        SetVisualTvFocus(TvEmptyAdultsBtn, _tvFocusRow == 5);

        // 2. Linha 2: Continuar a Ver
        for (int i = 0; i < _tvContinueVideos.Count; i++)
        {
            _tvContinueVideos[i].IsTvFocused = (_tvFocusRow == 2 && i == _tvContinueColIndex);
        }

        // 3. Linha 3: Favoritos
        for (int i = 0; i < _tvFavoriteVideos.Count; i++)
        {
            _tvFavoriteVideos[i].IsTvFocused = (_tvFocusRow == 3 && i == _tvFavoritesColIndex);
        }

        // 4. Linha 4: Categorias
        for (int i = 0; i < _tvCategories.Count; i++)
        {
            _tvCategories[i].IsTvFocused = (_tvFocusRow == 4 && i == _tvCategoriesColIndex);
        }

        // Auto-scroll da vista da TV para manter a linha ativa em foco
        if (TvScrollView is not null)
        {
            if (_tvFocusRow <= 1)
            {
                TvScrollView.ScrollToAsync(0, 0, true);
            }
            else if (_tvFocusRow == 2)
            {
                var yPos = (TvContinueRail is not null && TvContinueRail.Y > 0) ? TvContinueRail.Y : 320;
                TvScrollView.ScrollToAsync(0, yPos, true);
            }
            else if (_tvFocusRow == 3)
            {
                var yPos = (TvFavoritesRail is not null && TvFavoritesRail.Y > 0) ? TvFavoritesRail.Y : 600;
                TvScrollView.ScrollToAsync(0, yPos, true);
            }
            else if (_tvFocusRow == 4)
            {
                var yPos = (TvCategoriesRail is not null && TvCategoriesRail.Y > 0) ? TvCategoriesRail.Y : 880;
                TvScrollView.ScrollToAsync(0, yPos, true);
            }
        }

        // Scroll horizontal nas listas de TV para manter o cartão focado centrado
        if (_tvFocusRow == 2 && TvContinueList is not null && _tvContinueVideos.Count > 0)
        {
            var idx = Math.Clamp(_tvContinueColIndex, 0, _tvContinueVideos.Count - 1);
            TvContinueList.ScrollTo(idx, position: ScrollToPosition.Center, animate: true);
        }
        else if (_tvFocusRow == 3 && TvFavoritesList is not null && _tvFavoriteVideos.Count > 0)
        {
            var idx = Math.Clamp(_tvFavoritesColIndex, 0, _tvFavoriteVideos.Count - 1);
            TvFavoritesList.ScrollTo(idx, position: ScrollToPosition.Center, animate: true);
        }
        else if (_tvFocusRow == 4 && TvCategoriesList is not null && _tvCategories.Count > 0)
        {
            var idx = Math.Clamp(_tvCategoriesColIndex, 0, _tvCategories.Count - 1);
            TvCategoriesList.ScrollTo(idx, position: ScrollToPosition.Center, animate: true);
        }
    }

    private void UpdateTvPinVisualFocus()
    {
        if (!DeviceModeService.IsTv && DeviceInfo.Idiom != DeviceIdiom.TV) return;

        SetVisualTvFocus(PinBtn1, _tvPinRow == 0 && _tvPinCol == 0);
        SetVisualTvFocus(PinBtn2, _tvPinRow == 0 && _tvPinCol == 1);
        SetVisualTvFocus(PinBtn3, _tvPinRow == 0 && _tvPinCol == 2);

        SetVisualTvFocus(PinBtn4, _tvPinRow == 1 && _tvPinCol == 0);
        SetVisualTvFocus(PinBtn5, _tvPinRow == 1 && _tvPinCol == 1);
        SetVisualTvFocus(PinBtn6, _tvPinRow == 1 && _tvPinCol == 2);

        SetVisualTvFocus(PinBtn7, _tvPinRow == 2 && _tvPinCol == 0);
        SetVisualTvFocus(PinBtn8, _tvPinRow == 2 && _tvPinCol == 1);
        SetVisualTvFocus(PinBtn9, _tvPinRow == 2 && _tvPinCol == 2);

        SetVisualTvFocus(PinBtnBack, _tvPinRow == 3 && _tvPinCol == 0);
        SetVisualTvFocus(PinBtn0, _tvPinRow == 3 && _tvPinCol == 1);
        SetVisualTvFocus(PinBtnSubmit, _tvPinRow == 3 && _tvPinCol == 2);

        SetVisualTvFocus(TriggerBiometricBtn, _tvPinRow == 4);
        SetVisualTvFocus(PinPromptCancelBottomBtn, _tvPinRow == 5 && _tvPinCol == 0);
        SetVisualTvFocus(PinPromptEnterBtn, _tvPinRow == 5 && _tvPinCol == 1);
        SetVisualTvFocus(PinCancelBtn, _tvPinRow == 6);
    }

    private void UpdateTvExpiredVisualFocus()
    {
        if (!DeviceModeService.IsTv && DeviceInfo.Idiom != DeviceIdiom.TV) return;

        SetVisualTvFocus(BtnExpiredAdd5Min, _tvExpiredRow == 0 && _tvExpiredCol == 0);
        SetVisualTvFocus(BtnExpiredAdd15Min, _tvExpiredRow == 0 && _tvExpiredCol == 1);
        SetVisualTvFocus(BtnExpiredAdd30Min, _tvExpiredRow == 0 && _tvExpiredCol == 2);

        SetVisualTvFocus(BtnExpiredOpenParents, _tvExpiredRow == 1 && _tvExpiredCol == 0);
        SetVisualTvFocus(BtnExpiredQuitApp, _tvExpiredRow == 1 && _tvExpiredCol == 1);
    }

    private void UpdateTvParentVisualFocus()
    {
        if (!DeviceModeService.IsTv && DeviceInfo.Idiom != DeviceIdiom.TV) return;

        // 1. Linha 0: Barra de Topo [Voltar] [+15m] [Sair] [Fechar]
        SetVisualTvFocus(ParentBackBtn, _tvParentRow == 0 && _tvParentCol == 0);
        SetVisualTvFocus(ParentAddMinutesTopBtn, _tvParentRow == 0 && _tvParentCol == 1);
        SetVisualTvFocus(ParentQuitTopBtn, _tvParentRow == 0 && _tvParentCol == 2);
        SetVisualTvFocus(ParentCloseBtn, _tvParentRow == 0 && _tvParentCol == 3);

        // 2. Linha 1: Separadores Principais (Tabs) [Histórias] [Tempo] [TVs & Sala] [Definições]
        SetVisualTvFocus(TabBtnStories, _tvParentRow == 1 && _tvParentCol == 0);
        SetVisualTvFocus(TabBtnTime, _tvParentRow == 1 && _tvParentCol == 1);
        SetVisualTvFocus(TabBtnTvs, _tvParentRow == 1 && _tvParentCol == 2);
        SetVisualTvFocus(TabBtnSettings, _tvParentRow == 1 && _tvParentCol == 3);

        // 3. Conteúdo Específico por Separador
        if (_currentAdultTab == "stories")
        {
            // Linha 2: Modos de Curadoria (1. Vídeos Aprovados / 2. Temas Permitidos)
            SetVisualTvFocus(ModeApprovedCard, _tvParentRow == 2 && _tvParentCol == 0);
            SetVisualTvFocus(ModeThemesCard, _tvParentRow == 2 && _tvParentCol == 1);

            if (_curationMode == "approved")
            {
                // Linha 3: Barra de Pesquisa
                SetVisualTvFocus(SearchEntryBorder, _tvParentRow == 3 && _tvParentCol == 0);
                var isClearVisible = ClearSearchBtn is not null && ClearSearchBtn.IsVisible;
                SetVisualTvFocus(ClearSearchBtn, _tvParentRow == 3 && _tvParentCol == 1 && isClearVisible);
                SetVisualTvFocus(VoiceSearchBtn, _tvParentRow == 3 && (_tvParentCol == 2 || (!isClearVisible && _tvParentCol == 1)));

                // Linha 4: Sugestões Rápidas
                for (int i = 0; i < _suggestions.Count; i++)
                {
                    _suggestions[i].IsTvFocused = (_tvParentRow == 4 && i == _tvParentSuggestionIndex);
                }
                if (_tvParentRow == 4 && SuggestionsList is not null && _suggestions.Count > 0)
                {
                    var sIdx = Math.Clamp(_tvParentSuggestionIndex, 0, _suggestions.Count - 1);
                    SuggestionsList.ScrollTo(sIdx, position: ScrollToPosition.Center, animate: true);
                }

                // Linha 5: Botão Grande Pesquisar YouTube
                SetVisualTvFocus(BtnSearchYoutubeBig, _tvParentRow == 5);

                // Coluna da Direita (Rows 20..21)
                for (int i = 0; i < _parentDisplayVideos.Count; i++)
                {
                    _parentDisplayVideos[i].IsTvFocused = (_tvParentRow == 20 && i == _tvParentVideoIndex);
                }
                if (_tvParentRow == 20 && ParentUnifiedVideoList is not null && _parentDisplayVideos.Count > 0)
                {
                    var vIdx = Math.Clamp(_tvParentVideoIndex, 0, _parentDisplayVideos.Count - 1);
                    ParentUnifiedVideoList.ScrollTo(vIdx, position: ScrollToPosition.Center, animate: true);
                }

                SetVisualTvFocus(LoadMoreSearchBtn, _tvParentRow == 21);
            }
            else // _curationMode == "themes"
            {
                // Linha 3: Novo Tema Entry & Adicionar
                SetVisualTvFocus(NewThemeEntryBorder, _tvParentRow == 3 && _tvParentCol == 0);
                SetVisualTvFocus(BtnAddTheme, _tvParentRow == 3 && _tvParentCol == 1);

                // Linha 4: Playlists Autorizadas
                for (int i = 0; i < _parentThemes.Count; i++)
                {
                    _parentThemes[i].IsTvFocused = (_tvParentRow == 4 && i == _tvParentThemeIndex);
                }
                if (_tvParentRow == 4 && ParentThemesList is not null && _parentThemes.Count > 0)
                {
                    var tIdx = Math.Clamp(_tvParentThemeIndex, 0, _parentThemes.Count - 1);
                    ParentThemesList.ScrollTo(tIdx, position: ScrollToPosition.Center, animate: true);
                }

                // Linha 5: Restaurar Temas Padrão
                SetVisualTvFocus(BtnRestoreDefaultThemes, _tvParentRow == 5);
            }
        }
        else if (_currentAdultTab == "time")
        {
            // Linha 2: Menos / Mais
            SetVisualTvFocus(TvDurationMinusBtn, _tvParentRow == 2 && _tvParentCol == 0);
            SetVisualTvFocus(TvDurationPlusBtn, _tvParentRow == 2 && _tvParentCol == 1);

            // Linha 3: Reiniciar / Iniciar Temporizador
            SetVisualTvFocus(ResetSessionBtn, _tvParentRow == 3);
        }
        else if (_currentAdultTab == "tvs")
        {
            // Linha 2: Gerar Novo Código / Pesquisar
            SetVisualTvFocus(TvRegenerateCodeBtn, _tvParentRow == 2);
            SetVisualTvFocus(ScanTvsNetworkBtn, _tvParentRow == 2);

            // Linha 3: Desconectar todos
            SetVisualTvFocus(TvDisconnectAllBtn, _tvParentRow == 3);
        }
        else if (_currentAdultTab == "settings")
        {
            // Linha 2: 1º Idioma (Principal)
            SetVisualTvFocus(LanguagePickerBorder, _tvParentRow == 2);

            // Linha 3: 2º Idioma (Secundário)
            SetVisualTvFocus(LanguagePickerBorder2, _tvParentRow == 3);

            // Linha 4: 3º Idioma (Opcional)
            SetVisualTvFocus(LanguagePickerBorder3, _tvParentRow == 4);

            // Linha 5: Gestão de Vídeos Bloqueados / Ocultados (se existirem)
            for (int i = 0; i < _blockedVideos.Count; i++)
            {
                _blockedVideos[i].IsTvFocused = (_tvParentRow == 5 && i == _tvBlockedVideoIndex);
            }
            if (_tvParentRow == 5 && BlockedVideosList is not null && _blockedVideos.Count > 0)
            {
                var bIdx = Math.Clamp(_tvBlockedVideoIndex, 0, _blockedVideos.Count - 1);
                BlockedVideosList.ScrollTo(bIdx, position: ScrollToPosition.Center, animate: true);
            }

            // Linha 6: Novo PIN e Guardar
            SetVisualTvFocus(NewPinBorder, _tvParentRow == 6 && _tvParentCol == 0);
            SetVisualTvFocus(SavePinBtn, _tvParentRow == 6 && _tvParentCol == 1);

            // Linha 7: Descoberta Infantil
            SetVisualTvFocus(AllowChildDiscoverBorder, _tvParentRow == 7);

            // Linha 8: Apresentação & Tutorial da Aplicação
            SetVisualTvFocus(BtnSettingsViewTutorial, _tvParentRow == 8);

            // Linha 9: Sair da Aplicação
            SetVisualTvFocus(QuitAppBtn, _tvParentRow == 9);

            // Linha 10: Termos e Contacto / Feedback
            SetVisualTvFocus(BtnSettingsViewTerms, _tvParentRow == 10 && _tvParentCol == 0);
            SetVisualTvFocus(BtnSettingsContactEmail, _tvParentRow == 10 && _tvParentCol == 1);
        }

        // Auto-scroll dinâmico para a linha focada
        if (_tvParentRow <= 12 && ParentScrollView is not null)
        {
            if (_tvParentRow <= 1)
            {
                ParentScrollView.ScrollToAsync(0, 0, true);
            }
            else if (_currentAdultTab == "stories")
            {
                if (_curationMode == "approved")
                {
                    if (_tvParentRow == 2) ParentScrollView.ScrollToAsync(ModeApprovedCard, ScrollToPosition.Center, true);
                    else if (_tvParentRow == 3) ParentScrollView.ScrollToAsync(SearchEntryBorder, ScrollToPosition.Center, true);
                    else if (_tvParentRow == 4) ParentScrollView.ScrollToAsync(SuggestionsList, ScrollToPosition.Center, true);
                    else if (_tvParentRow == 5) ParentScrollView.ScrollToAsync(BtnSearchYoutubeBig, ScrollToPosition.Center, true);
                }
                else
                {
                    if (_tvParentRow == 2) ParentScrollView.ScrollToAsync(ModeApprovedCard, ScrollToPosition.Center, true);
                    else if (_tvParentRow == 3) ParentScrollView.ScrollToAsync(NewThemeEntryBorder, ScrollToPosition.Center, true);
                    else if (_tvParentRow == 4) ParentScrollView.ScrollToAsync(ParentThemesList, ScrollToPosition.Center, true);
                    else if (_tvParentRow == 5) ParentScrollView.ScrollToAsync(BtnRestoreDefaultThemes, ScrollToPosition.Center, true);
                }
            }
            else if (_currentAdultTab == "time")
            {
                if (_tvParentRow == 2) ParentScrollView.ScrollToAsync(TvDurationMinusBtn, ScrollToPosition.Center, true);
                else if (_tvParentRow == 3) ParentScrollView.ScrollToAsync(ResetSessionBtn, ScrollToPosition.Center, true);
            }
            else if (_currentAdultTab == "tvs")
            {
                if (_tvParentRow == 2) ParentScrollView.ScrollToAsync(TvRegenerateCodeBtn, ScrollToPosition.Center, true);
                else if (_tvParentRow == 3) ParentScrollView.ScrollToAsync(TvDisconnectAllBtn, ScrollToPosition.Center, true);
            }
            else if (_currentAdultTab == "settings")
            {
                if (_tvParentRow == 2) ParentScrollView.ScrollToAsync(LanguagePickerBorder, ScrollToPosition.Center, true);
                else if (_tvParentRow == 3) ParentScrollView.ScrollToAsync(LanguagePickerBorder2, ScrollToPosition.Center, true);
                else if (_tvParentRow == 4) ParentScrollView.ScrollToAsync(LanguagePickerBorder3, ScrollToPosition.Center, true);
                else if (_tvParentRow == 5) ParentScrollView.ScrollToAsync(BlockedVideosCard, ScrollToPosition.Center, true);
                else if (_tvParentRow == 6) ParentScrollView.ScrollToAsync(NewPinBorder, ScrollToPosition.Center, true);
                else if (_tvParentRow == 7) ParentScrollView.ScrollToAsync(AllowChildDiscoverBorder, ScrollToPosition.Center, true);
                else if (_tvParentRow == 8) ParentScrollView.ScrollToAsync(BtnSettingsViewTutorial, ScrollToPosition.Center, true);
                else if (_tvParentRow == 9) ParentScrollView.ScrollToAsync(QuitAppBtn, ScrollToPosition.Center, true);
                else if (_tvParentRow == 10) ParentScrollView.ScrollToAsync(BtnSettingsViewTerms, ScrollToPosition.Center, true);
            }
        }
    }

    private void UpdateTvPlayerVisualFocus()
    {
        if (!DeviceModeService.IsTv && DeviceInfo.Idiom != DeviceIdiom.TV) return;

        SetVisualTvFocus(PlayerBackBtn, !_tvPlayerInSuggestions && _tvPlayerColIndex == 0);
        SetVisualTvFocus(PlayPauseBtn, !_tvPlayerInSuggestions && _tvPlayerColIndex == 1);
        SetVisualTvFocus(PlayerSuggestionsBtn, !_tvPlayerInSuggestions && _tvPlayerColIndex == 2);

        for (int i = 0; i < _playerSuggestionVideos.Count; i++)
        {
            _playerSuggestionVideos[i].IsTvFocused = (_tvPlayerInSuggestions && i == _tvPlayerSuggestionIndex);
        }

        if (_tvPlayerInSuggestions && PlayerSuggestionsList is not null && _playerSuggestionVideos.Count > 0)
        {
            var idx = Math.Clamp(_tvPlayerSuggestionIndex, 0, _playerSuggestionVideos.Count - 1);
            PlayerSuggestionsList.ScrollTo(idx, position: ScrollToPosition.Center, animate: true);
        }
    }

    private void SetVisualTvFocus(VisualElement? elem, bool isFocused)
    {
        if (elem is null) return;
        try
        {
            if (isFocused)
            {
                elem.ScaleTo(1.08, 100, Easing.CubicOut);
                if (elem is Border border)
                {
                    border.Stroke = Color.FromArgb("#FACC15");
                    border.StrokeThickness = 4.0;
                }
                else if (elem is Button btn)
                {
                    btn.BorderColor = Color.FromArgb("#FACC15");
                    btn.BorderWidth = 4.0;
                }
            }
            else
            {
                elem.ScaleTo(1.0, 100, Easing.CubicIn);
                if (elem is Border border)
                {
                    if (border == ModeApprovedCard)
                    {
                        border.Stroke = _curationMode == "approved" ? (Color)Application.Current!.Resources["BrandGreen"] : (Color)Application.Current!.Resources["BorderLight"];
                        border.StrokeThickness = _curationMode == "approved" ? 2.5 : 1;
                    }
                    else if (border == ModeThemesCard)
                    {
                        border.Stroke = _curationMode != "approved" ? (Color)Application.Current!.Resources["BrandGreen"] : (Color)Application.Current!.Resources["BorderLight"];
                        border.StrokeThickness = _curationMode != "approved" ? 2.5 : 1;
                    }
                    else if (border == TabBtnStories || border == TabBtnTime || border == TabBtnTvs || border == TabBtnSettings)
                    {
                        UpdateAdultTabStyles();
                    }
                    else if (border == TvHeroWatchBtn)
                    {
                        border.Stroke = Color.FromArgb("#34D399");
                        border.StrokeThickness = 2.5;
                    }
                    else if (border == TvAdultsBtnBorder)
                    {
                        border.Stroke = Color.FromArgb("#334155");
                        border.StrokeThickness = 1.5;
                    }
                    else if (TvHeaderCategories != null && TvHeaderCategories.Children.Contains(border))
                    {
                        var catIndex = TvHeaderCategories.Children.IndexOf(border);
                        if (catIndex >= 0 && catIndex < _tvCategories.Count)
                        {
                            var isCatSelected = _tvCategories[catIndex].Id.Equals(_selectedTvCategory, StringComparison.OrdinalIgnoreCase);
                            border.Stroke = isCatSelected ? Color.FromArgb("#34D399") : Color.FromArgb("#334155");
                            border.StrokeThickness = isCatSelected ? 2.5 : 1.5;
                        }
                    }
                    else
                    {
                        border.Stroke = Color.FromArgb("#334155");
                        border.StrokeThickness = 1.5;
                    }
                }
                else if (elem is Button btn)
                {
                    if (btn == BtnSearchYoutubeBig || btn == ResetSessionBtn || btn == BtnExpiredAdd5Min || btn == BtnExpiredAdd15Min || btn == BtnExpiredAdd30Min || btn == ParentAddMinutesTopBtn)
                    {
                        btn.BorderColor = Colors.Transparent;
                        btn.BorderWidth = 0;
                    }
                    else if (btn == BtnRestoreDefaultThemes || btn == BtnSettingsViewTutorial)
                    {
                        btn.BorderColor = (Color)Application.Current!.Resources["BrandGreen"];
                        btn.BorderWidth = 1.5;
                    }
                    else if (btn == QuitAppBtn || btn == ParentQuitTopBtn || btn == BtnExpiredQuitApp || btn == TvDisconnectAllBtn)
                    {
                        btn.BorderColor = (Color)Application.Current!.Resources["AccentRed"];
                        btn.BorderWidth = 1.5;
                    }
                    else if (btn == PlayPauseBtn)
                    {
                        btn.BorderColor = Colors.Transparent;
                        btn.BorderWidth = 0;
                    }
                    else if (btn == PlayerSuggestionsBtn || btn == PlayerBackBtn)
                    {
                        btn.BorderColor = Color.FromArgb("#334155");
                        btn.BorderWidth = 1;
                    }
                    else
                    {
                        btn.BorderColor = Colors.Transparent;
                        btn.BorderWidth = 0;
                    }
                }
            }
        }
        catch { }
    }

#if ANDROID
    private bool OnTvRemoteKeyPressed(Android.Views.Keycode code)
    {
        // 0. Splash Screen Animado
        if (AnimatedSplashOverlay is not null && AnimatedSplashOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA || code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => _ = DismissSplashAsync());
                return true;
            }
            return true; // Bloqueia outras ações enquanto decorre a animação de boas-vindas
        }

        // 1. Modal de Termos de Utilização & Privacidade
        if (TermsOverlay is not null && TermsOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                if (TermsCloseBtn is not null && TermsCloseBtn.IsVisible)
                {
                    MainThread.BeginInvokeOnMainThread(() => OnCloseTermsOverlayClicked(null, EventArgs.Empty));
                }
                return true;
            }

            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (TermsContentScrollView is not null)
                    {
                        var newY = TermsContentScrollView.ScrollY + 120;
                        await TermsContentScrollView.ScrollToAsync(0, newY, true);
                    }
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (TermsContentScrollView is not null)
                    {
                        var newY = Math.Max(0, TermsContentScrollView.ScrollY - 120);
                        await TermsContentScrollView.ScrollToAsync(0, newY, true);
                    }
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadLeft || code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _tvTermsCol = (_tvTermsCol == 0 ? 1 : 0);
                    UpdateTvTermsVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvTermsCol == 0)
                    {
                        OnAcceptTermsClicked(null, EventArgs.Empty);
                    }
                    else
                    {
                        OnContactSupportClicked(null, EventArgs.Empty);
                    }
                });
                return true;
            }

            return true;
        }

        // 0.0 Modal de Apresentação / Onboarding / Tutorial
        if (OnboardingModalOverlay is not null && OnboardingModalOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnSkipOnboardingClicked(null, EventArgs.Empty));
                return true;
            }

            if (code == Android.Views.Keycode.DpadLeft)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_currentOnboardingSlide > 1 && _tvOnboardingBtn == 1)
                    {
                        _tvOnboardingBtn = 0;
                    }
                    else if (_tvOnboardingBtn == 2)
                    {
                        _tvOnboardingBtn = 1;
                    }
                    UpdateTvOnboardingVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvOnboardingBtn == 0)
                    {
                        _tvOnboardingBtn = 1;
                    }
                    else if (_tvOnboardingBtn == 1)
                    {
                        _tvOnboardingBtn = 2;
                    }
                    UpdateTvOnboardingVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _tvOnboardingBtn = 2; // Saltar no topo
                    UpdateTvOnboardingVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _tvOnboardingBtn = 1; // Seguinte na base
                    UpdateTvOnboardingVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvOnboardingBtn == 0) OnOnboardingPrevClicked(null, EventArgs.Empty);
                    else if (_tvOnboardingBtn == 2) OnSkipOnboardingClicked(null, EventArgs.Empty);
                    else OnOnboardingNextClicked(null, EventArgs.Empty);
                });
                return true;
            }

            return true;
        }

        // 0.2 Modal de Pré-Visualização de Vídeo dos Adultos
        if (AdultPreviewModalOverlay is not null && AdultPreviewModalOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnCloseAdultPreviewClicked(null, EventArgs.Empty));
                return true;
            }

            if (code == Android.Views.Keycode.DpadLeft)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPreviewCol > 0) _tvPreviewCol--;
                    UpdateTvPreviewVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var maxCol = (BtnPreviewSendToTv?.IsVisible ?? false) ? 2 : 1;
                    if (_tvPreviewCol < maxCol) _tvPreviewCol++;
                    UpdateTvPreviewVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _tvPreviewCol = 3; // Botão Fechar ✕ no topo
                    UpdateTvPreviewVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPreviewCol == 3) _tvPreviewCol = 0; // Volta aos botões de ação
                    UpdateTvPreviewVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPreviewCol == 0) OnPreviewToggleApprovalClicked(null, EventArgs.Empty);
                    else if (_tvPreviewCol == 1) OnPreviewBlockVideoClicked(null, EventArgs.Empty);
                    else if (_tvPreviewCol == 2 && (BtnPreviewSendToTv?.IsVisible ?? false)) OnPreviewSendToTvClicked(null, EventArgs.Empty);
                    else OnCloseAdultPreviewClicked(null, EventArgs.Empty);
                });
                return true;
            }

            return true;
        }

        // 0.3 Modal de Inspeção & Teste de Playlist
        if (PlaylistInspectModalOverlay is not null && PlaylistInspectModalOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnClosePlaylistInspectModalClicked(null, EventArgs.Empty));
                return true;
            }

            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvInspectRow == 3)
                    {
                        if (_inspectingPlaylistVideos.Count > 0)
                        {
                            _tvInspectRow = 2;
                            _tvInspectVideoIndex = _inspectingPlaylistVideos.Count - 1;
                            PlaylistInspectVideosList?.ScrollTo(_tvInspectVideoIndex, position: ScrollToPosition.Center);
                        }
                        else
                        {
                            _tvInspectRow = 1;
                        }
                    }
                    else if (_tvInspectRow == 2)
                    {
                        if (_tvInspectVideoIndex > 0)
                        {
                            _tvInspectVideoIndex--;
                            PlaylistInspectVideosList?.ScrollTo(_tvInspectVideoIndex, position: ScrollToPosition.Center);
                        }
                        else
                        {
                            _tvInspectRow = 1;
                        }
                    }
                    else if (_tvInspectRow == 1)
                    {
                        _tvInspectRow = 0;
                    }
                    UpdateTvInspectVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvInspectRow == 0)
                    {
                        _tvInspectRow = 1;
                    }
                    else if (_tvInspectRow == 1)
                    {
                        if (_inspectingPlaylistVideos.Count > 0)
                        {
                            _tvInspectRow = 2;
                            _tvInspectVideoIndex = 0;
                            PlaylistInspectVideosList?.ScrollTo(_tvInspectVideoIndex, position: ScrollToPosition.Center);
                        }
                        else
                        {
                            _tvInspectRow = 3;
                        }
                    }
                    else if (_tvInspectRow == 2)
                    {
                        if (_tvInspectVideoIndex < _inspectingPlaylistVideos.Count - 1)
                        {
                            _tvInspectVideoIndex++;
                            PlaylistInspectVideosList?.ScrollTo(_tvInspectVideoIndex, position: ScrollToPosition.Center);
                        }
                        else
                        {
                            _tvInspectRow = 3;
                        }
                    }
                    UpdateTvInspectVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadLeft || code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvInspectRow == 2)
                    {
                        _tvInspectCol = (_tvInspectCol == 0 ? 1 : 0);
                    }
                    UpdateTvInspectVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvInspectRow == 0)
                    {
                        OnClosePlaylistInspectModalClicked(null, EventArgs.Empty);
                    }
                    else if (_tvInspectRow == 1)
                    {
                        OnToggleApproveFullPlaylistClicked(null, EventArgs.Empty);
                    }
                    else if (_tvInspectRow == 2 && _tvInspectVideoIndex >= 0 && _tvInspectVideoIndex < _inspectingPlaylistVideos.Count)
                    {
                        var video = _inspectingPlaylistVideos[_tvInspectVideoIndex];
                        if (_tvInspectCol == 0)
                        {
                            var parentItem = new ParentVideoItem
                            {
                                Id = video.Id,
                                Title = video.Title,
                                IsApproved = _videos.Any(v => v.Id == video.Id) || _approvedPlaylists.Any(p => p.Videos != null && p.Videos.Any(pv => pv.Id == video.Id))
                            };
                            OpenAdultPreview(parentItem);
                        }
                        else
                        {
                            _inspectingPlaylistVideos.RemoveAt(_tvInspectVideoIndex);
                            BlockVideo(video.Id, video.Title);
                            if (_tvInspectVideoIndex >= _inspectingPlaylistVideos.Count)
                            {
                                _tvInspectVideoIndex = Math.Max(0, _inspectingPlaylistVideos.Count - 1);
                            }
                        }
                    }
                    else if (_tvInspectRow == 3)
                    {
                        OnClosePlaylistInspectModalClicked(null, EventArgs.Empty);
                    }
                });
                return true;
            }

            return true;
        }

        // 0.1 Modais de Controlo Remoto (Adicionar TV & Enviar para TV)
        if (AddTvModalOverlay is not null && AddTvModalOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnCloseAddTvModalClicked(null, EventArgs.Empty));
                return true;
            }
            return false;
        }

        if (SendToTvModalOverlay is not null && SendToTvModalOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnCloseSendToTvModalClicked(null, EventArgs.Empty));
                return true;
            }
            return false;
        }

        // 1. Ecrã de Fim de Tempo da Sessão (TimeExpiredView)
        if (TimeExpiredView is not null && TimeExpiredView.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                // No ecrã bloqueado por tempo, tecla voltar foca na área de adultos
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _tvExpiredRow = 1;
                    _tvExpiredCol = 0;
                    UpdateTvExpiredVisualFocus();
                });
                return true;
            }

            // D-Pad CIMA
            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvExpiredRow > 0)
                    {
                        _tvExpiredRow--;
                        _tvExpiredCol = Math.Clamp(_tvExpiredCol, 0, 2);
                    }
                    UpdateTvExpiredVisualFocus();
                });
                return true;
            }

            // D-Pad BAIXO
            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvExpiredRow < 1)
                    {
                        _tvExpiredRow++;
                        _tvExpiredCol = Math.Clamp(_tvExpiredCol, 0, 1);
                    }
                    UpdateTvExpiredVisualFocus();
                });
                return true;
            }

            // D-Pad ESQUERDA
            if (code == Android.Views.Keycode.DpadLeft)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvExpiredCol > 0) _tvExpiredCol--;
                    UpdateTvExpiredVisualFocus();
                });
                return true;
            }

            // D-Pad DIREITA
            if (code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var maxCol = (_tvExpiredRow == 0) ? 2 : 1;
                    if (_tvExpiredCol < maxCol) _tvExpiredCol++;
                    UpdateTvExpiredVisualFocus();
                });
                return true;
            }

            // D-Pad OK / ENTER / BOTÃO A
            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvExpiredRow == 0)
                    {
                        if (_tvExpiredCol == 0) OnQuickAddMinutesClicked(BtnExpiredAdd5Min, EventArgs.Empty);
                        else if (_tvExpiredCol == 1) OnQuickAddMinutesClicked(BtnExpiredAdd15Min, EventArgs.Empty);
                        else if (_tvExpiredCol == 2) OnQuickAddMinutesClicked(BtnExpiredAdd30Min, EventArgs.Empty);
                    }
                    else
                    {
                        if (_tvExpiredCol == 0) OnParentsClicked(BtnExpiredOpenParents, EventArgs.Empty);
                        else OnQuitAppClicked(BtnExpiredQuitApp, EventArgs.Empty);
                    }
                });
                return true;
            }

            return true;
        }

        // 2. Ecrã de Reprodução de Vídeo (Controlos de Comando TV)
        if (PlayerView.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (PlayerSuggestionsDrawer is not null && PlayerSuggestionsDrawer.IsVisible)
                    {
                        PlayerSuggestionsDrawer.IsVisible = false;
                        _tvPlayerInSuggestions = false;
                        UpdateTvPlayerVisualFocus();
                    }
                    else
                    {
                        OnBackClicked(null!, EventArgs.Empty);
                    }
                });
                return true;
            }

            // Teclas de Reprodução / Pausa diretas do comando da TV
            if (code == Android.Views.Keycode.MediaPlayPause || 
                code == Android.Views.Keycode.MediaPlay || 
                code == Android.Views.Keycode.MediaPause)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnPlayPauseClicked(PlayPauseBtn, EventArgs.Empty);
                    ShowControls();
                });
                return true;
            }

            // D-Pad OK / ENTER / BOTÃO A / SPACE
            if (code == Android.Views.Keycode.DpadCenter || 
                code == Android.Views.Keycode.Enter || 
                code == Android.Views.Keycode.ButtonA || 
                code == Android.Views.Keycode.Space)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPlayerInSuggestions && PlayerSuggestionsDrawer is not null && PlayerSuggestionsDrawer.IsVisible && _playerSuggestionVideos.Count > 0)
                    {
                        var sIdx = Math.Clamp(_tvPlayerSuggestionIndex, 0, _playerSuggestionVideos.Count - 1);
                        var item = _playerSuggestionVideos[sIdx];
                        OnPlayerSuggestionVideoTapped(null, new TappedEventArgs(item));
                    }
                    else if (ControlsBar is not null && ControlsBar.IsVisible)
                    {
                        if (_tvPlayerColIndex == 0)
                        {
                            OnBackClicked(null!, EventArgs.Empty);
                        }
                        else if (_tvPlayerColIndex == 1)
                        {
                            OnPlayPauseClicked(PlayPauseBtn, EventArgs.Empty);
                            ShowControls();
                        }
                        else if (_tvPlayerColIndex == 2)
                        {
                            OnTogglePlayerSuggestionsClicked(PlayerSuggestionsBtn, EventArgs.Empty);
                            if (PlayerSuggestionsDrawer is not null && PlayerSuggestionsDrawer.IsVisible)
                            {
                                _tvPlayerInSuggestions = true;
                                _tvPlayerSuggestionIndex = 0;
                            }
                            UpdateTvPlayerVisualFocus();
                        }
                    }
                    else
                    {
                        OnPlayPauseClicked(PlayPauseBtn, EventArgs.Empty);
                        ShowControls();
                    }
                });
                return true;
            }

            // D-Pad BAIXO: Abre gaveta de sugestões / foca controlos
            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (PlayerSuggestionsDrawer is not null && !PlayerSuggestionsDrawer.IsVisible && _playerSuggestionVideos.Count > 0)
                    {
                        PlayerSuggestionsDrawer.IsVisible = true;
                        _tvPlayerInSuggestions = true;
                        _tvPlayerSuggestionIndex = 0;
                    }
                    else if (PlayerSuggestionsDrawer is not null && PlayerSuggestionsDrawer.IsVisible)
                    {
                        _tvPlayerInSuggestions = true;
                    }
                    else
                    {
                        _tvPlayerInSuggestions = false;
                        _tvPlayerColIndex = 1;
                    }
                    ShowControls();
                    UpdateTvPlayerVisualFocus();
                });
                return true;
            }

            // D-Pad CIMA: Fecha gaveta de sugestões / foca barra de topo
            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPlayerInSuggestions)
                    {
                        _tvPlayerInSuggestions = false;
                        _tvPlayerColIndex = 1;
                    }
                    else
                    {
                        _tvPlayerColIndex = 0; // Foca no botão Voltar
                    }
                    ShowControls();
                    UpdateTvPlayerVisualFocus();
                });
                return true;
            }

            // D-Pad ESQUERDA ou REWIND: Retrocede 10 segundos ou navega sugestões
            if (code == Android.Views.Keycode.DpadLeft || 
                code == Android.Views.Keycode.MediaRewind || 
                code == Android.Views.Keycode.MediaPrevious)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPlayerInSuggestions && _playerSuggestionVideos.Count > 0)
                    {
                        if (_tvPlayerSuggestionIndex > 0) _tvPlayerSuggestionIndex--;
                        UpdateTvPlayerVisualFocus();
                    }
                    else if (ControlsBar is not null && ControlsBar.IsVisible && _tvPlayerColIndex > 0)
                    {
                        _tvPlayerColIndex--;
                        ShowControls();
                        UpdateTvPlayerVisualFocus();
                    }
                    else
                    {
                        try
                        {
                            _ = YoutubePlayer.EvaluateJavaScriptAsync("if(typeof player!=='undefined'&&player.seekTo){player.seekTo(Math.max(0, player.getCurrentTime()-10), true);}");
                        }
                        catch { }
                        ShowControls();
                    }
                });
                return true;
            }

            // D-Pad DIREITA ou FAST FORWARD: Avança 10 segundos ou navega sugestões
            if (code == Android.Views.Keycode.DpadRight || 
                code == Android.Views.Keycode.MediaFastForward || 
                code == Android.Views.Keycode.MediaNext)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPlayerInSuggestions && _playerSuggestionVideos.Count > 0)
                    {
                        if (_tvPlayerSuggestionIndex < _playerSuggestionVideos.Count - 1) _tvPlayerSuggestionIndex++;
                        UpdateTvPlayerVisualFocus();
                    }
                    else if (ControlsBar is not null && ControlsBar.IsVisible && _tvPlayerColIndex < 2)
                    {
                        _tvPlayerColIndex++;
                        ShowControls();
                        UpdateTvPlayerVisualFocus();
                    }
                    else
                    {
                        try
                        {
                            _ = YoutubePlayer.EvaluateJavaScriptAsync("if(typeof player!=='undefined'&&player.seekTo){player.seekTo(player.getCurrentTime()+10, true);}");
                        }
                        catch { }
                        ShowControls();
                    }
                });
                return true;
            }

            return false;
        }

        // 2.5 Modal de Autorização pelo Telemóvel na TV
        if (TvRemoteAuthPromptOverlay is not null && TvRemoteAuthPromptOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnTvAuthCancelClicked(null!, EventArgs.Empty));
                return true;
            }

            var isPinVisible = BtnTvAuthUsePinFallback is not null && BtnTvAuthUsePinFallback.IsVisible;

            if (code == Android.Views.Keycode.DpadUp || code == Android.Views.Keycode.DpadDown || code == Android.Views.Keycode.DpadLeft || code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (isPinVisible)
                    {
                        _tvRemoteAuthCol = _tvRemoteAuthCol == 0 ? 1 : 0;
                    }
                    else
                    {
                        _tvRemoteAuthCol = 1;
                    }
                    UpdateTvRemoteAuthVisualFocus();
                });
                return true;
            }

            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (isPinVisible && _tvRemoteAuthCol == 0) OnTvAuthUsePinFallbackClicked(null!, EventArgs.Empty);
                    else OnTvAuthCancelClicked(null!, EventArgs.Empty);
                });
                return true;
            }

            // Teclas de atalho secreto no comando da TV (Menu, Info, Guide, Tecla Amarela)
            if (code == Android.Views.Keycode.Menu || code == Android.Views.Keycode.Info || code == Android.Views.Keycode.Guide || code == Android.Views.Keycode.ProgYellow)
            {
                MainThread.BeginInvokeOnMainThread(() => RevealEmergencyPinOnTv());
                return true;
            }

            return true;
        }

        // 3. Modal de Autenticação / PIN
        if (PinPromptOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnCancelPinPromptClicked(null!, EventArgs.Empty));
                return true;
            }

            // D-Pad CIMA
            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPinRow > 0) _tvPinRow--;
                    UpdateTvPinVisualFocus();
                });
                return true;
            }

            // D-Pad BAIXO
            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var maxRow = (TriggerBiometricBtn is not null && TriggerBiometricBtn.IsVisible) ? 5 : 5;
                    if (_tvPinRow < maxRow) _tvPinRow++;
                    UpdateTvPinVisualFocus();
                });
                return true;
            }

            // D-Pad ESQUERDA
            if (code == Android.Views.Keycode.DpadLeft)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPinRow <= 3 && _tvPinCol > 0) _tvPinCol--;
                    else if (_tvPinRow == 5) _tvPinCol = 0;
                    UpdateTvPinVisualFocus();
                });
                return true;
            }

            // D-Pad DIREITA
            if (code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPinRow <= 3 && _tvPinCol < 2) _tvPinCol++;
                    else if (_tvPinRow == 5) _tvPinCol = 1;
                    UpdateTvPinVisualFocus();
                });
                return true;
            }

            // Teclado numérico físico (0 a 9)
            if (code >= Android.Views.Keycode.Num0 && code <= Android.Views.Keycode.Num9)
            {
                var digit = (char)('0' + (code - Android.Views.Keycode.Num0));
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if ((PinEntryBox.Text ?? string.Empty).Length < 4)
                    {
                        PinEntryBox.Text = (PinEntryBox.Text ?? string.Empty) + digit;
                    }
                    if (PinEntryBox.Text?.Length == 4)
                    {
                        OnSubmitPinClicked(null, EventArgs.Empty);
                    }
                });
                return true;
            }

            // Backspace / Delete
            if (code == Android.Views.Keycode.Del)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!string.IsNullOrEmpty(PinEntryBox.Text))
                    {
                        PinEntryBox.Text = PinEntryBox.Text[..^1];
                    }
                });
                return true;
            }

            // D-Pad OK / ENTER / BOTÃO A
            if (code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvPinRow >= 0 && _tvPinRow <= 2)
                    {
                        var digit = (_tvPinRow * 3 + _tvPinCol + 1).ToString();
                        if ((PinEntryBox.Text ?? string.Empty).Length < 4)
                        {
                            PinEntryBox.Text = (PinEntryBox.Text ?? string.Empty) + digit;
                        }
                    }
                    else if (_tvPinRow == 3)
                    {
                        if (_tvPinCol == 0) // Del
                        {
                            if (!string.IsNullOrEmpty(PinEntryBox.Text))
                            {
                                PinEntryBox.Text = PinEntryBox.Text[..^1];
                            }
                        }
                        else if (_tvPinCol == 1) // 0
                        {
                            if ((PinEntryBox.Text ?? string.Empty).Length < 4)
                            {
                                PinEntryBox.Text = (PinEntryBox.Text ?? string.Empty) + "0";
                            }
                        }
                        else if (_tvPinCol == 2) // Submit
                        {
                            OnSubmitPinClicked(null, EventArgs.Empty);
                        }
                    }
                    else if (_tvPinRow == 4) // Biometric
                    {
                        OnTriggerBiometricClicked(null, EventArgs.Empty);
                    }
                    else if (_tvPinRow == 5) // Cancel / Enter
                    {
                        if (_tvPinCol == 0) OnCancelPinPromptClicked(null!, EventArgs.Empty);
                        else OnSubmitPinClicked(null, EventArgs.Empty);
                    }
                });
                return true;
            }
            return false;
        }

        // 4. Painel de Gestão dos Adultos (Layout TV-First)
        if (ParentOverlay.IsVisible)
        {
            if (code == Android.Views.Keycode.Back)
            {
                MainThread.BeginInvokeOnMainThread(() => OnCloseParentsClicked(null!, EventArgs.Empty));
                return true;
            }

            // D-Pad CIMA
            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvParentRow == 20) // Na lista de vídeos (coluna da direita)
                    {
                        if (_tvParentVideoIndex > 0)
                        {
                            _tvParentVideoIndex--;
                        }
                        else
                        {
                            _tvParentRow = 1; // Volta ao topo (Tabs)
                            _tvParentCol = 0;
                        }
                    }
                    else if (_tvParentRow == 21) // No botão Carregar Mais
                    {
                        _tvParentRow = 20;
                        _tvParentVideoIndex = Math.Max(0, _parentDisplayVideos.Count - 1);
                    }
                    else if (_currentAdultTab == "stories" && _curationMode == "themes" && _tvParentRow == 4)
                    {
                        if (_tvParentThemeIndex > 0)
                        {
                            _tvParentThemeIndex--;
                        }
                        else
                        {
                            _tvParentRow = 3;
                            _tvParentCol = 0;
                        }
                    }
                    else // Nas linhas de conteúdo ou topo (0..10)
                    {
                        if (_tvParentRow > 0)
                        {
                            _tvParentRow--;
                            _tvParentCol = 0;
                        }
                    }
                    UpdateTvParentVisualFocus();
                });
                return true;
            }

            // D-Pad BAIXO
            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvParentRow == 20) // Na lista de vídeos (coluna da direita)
                    {
                        if (_tvParentVideoIndex < _parentDisplayVideos.Count - 1)
                        {
                            _tvParentVideoIndex++;
                        }
                        else if (LoadMoreSearchContainer is not null && LoadMoreSearchContainer.IsVisible)
                        {
                            _tvParentRow = 21;
                        }
                    }
                    else if (_tvParentRow == 21)
                    {
                        // Já está no fundo
                    }
                    else if (_currentAdultTab == "stories" && _curationMode == "themes" && _tvParentRow == 4)
                    {
                        if (_tvParentThemeIndex < _parentThemes.Count - 1)
                        {
                            _tvParentThemeIndex++;
                        }
                        else
                        {
                            _tvParentRow = 5;
                            _tvParentCol = 0;
                        }
                    }
                    else
                    {
                        var maxRow = _currentAdultTab switch
                        {
                            "stories" => 5,
                            "time" => 3,
                            "tvs" => 3,
                            "settings" => 10,
                            _ => 5
                        };

                        if (_tvParentRow < maxRow)
                        {
                            _tvParentRow++;
                            if (_currentAdultTab == "settings" && _tvParentRow == 5 && _blockedVideos.Count == 0)
                            {
                                _tvParentRow = 6; // Salta lista vazia de bloqueados
                            }
                            _tvParentCol = 0;
                        }
                    }
                    UpdateTvParentVisualFocus();
                });
                return true;
            }

            // D-Pad ESQUERDA
            if (code == Android.Views.Keycode.DpadLeft)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvParentRow == 20 || _tvParentRow == 21) // Da lista da direita para a esquerda
                    {
                        _tvParentRow = 3; // Volta para o campo de pesquisa
                        _tvParentCol = 0;
                    }
                    else if (_tvParentRow == 0) // Linha Topo: [Voltar] [+15m] [Sair] [Fechar]
                    {
                        if (_tvParentCol > 0) _tvParentCol--;
                    }
                    else if (_tvParentRow == 1) // Linha Tabs: [Histórias] [Tempo] [TVs] [Definições]
                    {
                        if (_tvParentCol > 0)
                        {
                            _tvParentCol--;
                            var tabs = new[] { "stories", "time", "tvs", "settings" };
                            SelectAdultTab(tabs[_tvParentCol]);
                        }
                    }
                    else if (_currentAdultTab == "stories")
                    {
                        if (_curationMode == "approved")
                        {
                            if (_tvParentRow == 2 && _tvParentCol > 0) _tvParentCol--;
                            else if (_tvParentRow == 3 && _tvParentCol > 0) _tvParentCol--;
                            else if (_tvParentRow == 4 && _tvParentSuggestionIndex > 0) _tvParentSuggestionIndex--;
                        }
                        else
                        {
                            if (_tvParentRow == 2 && _tvParentCol > 0) _tvParentCol--;
                            else if (_tvParentRow == 3 && _tvParentCol > 0) _tvParentCol--;
                        }
                    }
                    else if (_currentAdultTab == "time")
                    {
                        if (_tvParentRow == 2 && _tvParentCol > 0) _tvParentCol--;
                    }
                    else if (_currentAdultTab == "settings")
                    {
                        if (_tvParentRow == 5 && _tvBlockedVideoIndex > 0) _tvBlockedVideoIndex--;
                        else if (_tvParentRow == 6 && _tvParentCol > 0) _tvParentCol--;
                        else if (_tvParentRow == 10 && _tvParentCol > 0) _tvParentCol--;
                    }
                    UpdateTvParentVisualFocus();
                });
                return true;
            }

            // D-Pad DIREITA
            if (code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvParentRow == 20 || _tvParentRow == 21)
                    {
                        // Já está na coluna da direita
                    }
                    else if (_tvParentRow == 0) // Linha Topo (0..3)
                    {
                        if (_tvParentCol < 3) _tvParentCol++;
                    }
                    else if (_tvParentRow == 1) // Linha Tabs (0..3)
                    {
                        if (_tvParentCol < 3)
                        {
                            _tvParentCol++;
                            var tabs = new[] { "stories", "time", "tvs", "settings" };
                            SelectAdultTab(tabs[_tvParentCol]);
                        }
                    }
                    else if (_currentAdultTab == "stories")
                    {
                        if (_curationMode == "approved")
                        {
                            var maxColInRow = _tvParentRow switch
                            {
                                2 => 1,
                                3 => (ClearSearchBtn is not null && ClearSearchBtn.IsVisible ? 2 : 1),
                                _ => 0
                            };

                            if (_tvParentRow == 4 && _tvParentSuggestionIndex < _suggestions.Count - 1)
                            {
                                _tvParentSuggestionIndex++;
                            }
                            else if (_tvParentCol < maxColInRow)
                            {
                                _tvParentCol++;
                            }
                            else
                            {
                                // Salto para a coluna da direita com vídeos
                                if (_parentDisplayVideos.Count > 0)
                                {
                                    _tvParentRow = 20;
                                    _tvParentVideoIndex = Math.Clamp(_tvParentVideoIndex, 0, _parentDisplayVideos.Count - 1);
                                }
                            }
                        }
                        else
                        {
                            if (_tvParentRow == 2 && _tvParentCol < 1) _tvParentCol++;
                            else if (_tvParentRow == 3 && _tvParentCol < 1) _tvParentCol++;
                        }
                    }
                    else if (_currentAdultTab == "time")
                    {
                        if (_tvParentRow == 2 && _tvParentCol < 1) _tvParentCol++;
                    }
                    else if (_currentAdultTab == "settings")
                    {
                        if (_tvParentRow == 5 && _tvBlockedVideoIndex < _blockedVideos.Count - 1) _tvBlockedVideoIndex++;
                        else if (_tvParentRow == 6 && _tvParentCol < 1) _tvParentCol++;
                        else if (_tvParentRow == 10 && _tvParentCol < 1) _tvParentCol++;
                    }
                    UpdateTvParentVisualFocus();
                });
                return true;
            }

            // D-Pad OK / ENTER / BOTÃO A
            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvParentRow == 0) // Barra do Topo
                    {
                        if (_tvParentCol == 0) OnCloseParentsClicked(null!, EventArgs.Empty);
                        else if (_tvParentCol == 1) OnQuickAddMinutesClicked(ParentAddMinutesTopBtn, EventArgs.Empty);
                        else if (_tvParentCol == 2) OnQuitAppClicked(null!, EventArgs.Empty);
                        else if (_tvParentCol == 3) OnCloseParentsClicked(null!, EventArgs.Empty);
                    }
                    else if (_tvParentRow == 1) // Tabs
                    {
                        var tabs = new[] { "stories", "time", "tvs", "settings" };
                        var tab = tabs[Math.Clamp(_tvParentCol, 0, tabs.Length - 1)];
                        SelectAdultTab(tab);
                    }
                    else if (_currentAdultTab == "stories")
                    {
                        if (_tvParentRow == 2)
                        {
                            if (_tvParentCol == 0) OnSelectModeApprovedTapped(null, EventArgs.Empty);
                            else OnSelectModeThemesTapped(null, EventArgs.Empty);
                        }
                        else if (_curationMode == "approved")
                        {
                            if (_tvParentRow == 3)
                            {
                                var isClearVisible = ClearSearchBtn is not null && ClearSearchBtn.IsVisible;
                                if (_tvParentCol == 0) SearchEntry.Focus();
                                else if (_tvParentCol == 1 && isClearVisible) OnClearSearchClicked(null, EventArgs.Empty);
                                else OnSearchVideosClicked(null, EventArgs.Empty);
                            }
                            else if (_tvParentRow == 4 && _suggestions.Count > 0)
                            {
                                var sIdx = Math.Clamp(_tvParentSuggestionIndex, 0, _suggestions.Count - 1);
                                SearchEntry.Text = _suggestions[sIdx].Text;
                                ClearSearchBtn.IsVisible = true;
                                OnSearchVideosClicked(null, EventArgs.Empty);
                            }
                            else if (_tvParentRow == 5)
                            {
                                OnSearchVideosClicked(null, EventArgs.Empty);
                            }
                            else if (_tvParentRow == 20 && _parentDisplayVideos.Count > 0)
                            {
                                var vIdx = Math.Clamp(_tvParentVideoIndex, 0, _parentDisplayVideos.Count - 1);
                                var videoItem = _parentDisplayVideos[vIdx];
                                OnToggleApprovalClicked(new Button { CommandParameter = videoItem }, EventArgs.Empty);
                            }
                            else if (_tvParentRow == 21)
                            {
                                OnLoadMoreSearchResultsClicked(null, EventArgs.Empty);
                            }
                        }
                        else // _curationMode == "themes"
                        {
                            if (_tvParentRow == 3)
                            {
                                if (_tvParentCol == 0) NewThemeEntry?.Focus();
                                else OnAddNewThemeClicked(null, EventArgs.Empty);
                            }
                            else if (_tvParentRow == 4 && _parentThemes.Count > 0)
                            {
                                var tIdx = Math.Clamp(_tvParentThemeIndex, 0, _parentThemes.Count - 1);
                                OnParentThemeItemTapped(null, new TappedEventArgs(_parentThemes[tIdx]));
                            }
                            else if (_tvParentRow == 5)
                            {
                                OnRestoreDefaultThemesClicked(null, EventArgs.Empty);
                            }
                        }
                    }
                    else if (_currentAdultTab == "time")
                    {
                        if (_tvParentRow == 2)
                        {
                            if (_tvParentCol == 0) OnDecreaseMinutesClicked(null, EventArgs.Empty);
                            else if (_tvParentCol == 1) OnIncreaseMinutesClicked(null, EventArgs.Empty);
                        }
                        else if (_tvParentRow == 3)
                        {
                            OnResetSessionClicked(null, EventArgs.Empty);
                        }
                    }
                    else if (_currentAdultTab == "tvs")
                    {
                        if (_tvParentRow == 2)
                        {
                            if (DeviceModeService.IsTv) OnRegenerateTvPairingCodeClicked(null, EventArgs.Empty);
                            else OnScanTvsNetworkClicked(null, EventArgs.Empty);
                        }
                        else if (_tvParentRow == 3)
                        {
                            if (DeviceModeService.IsTv) OnDisconnectAllTvClientsClicked(null, EventArgs.Empty);
                        }
                    }
                    else if (_currentAdultTab == "settings")
                    {
                        if (_tvParentRow == 2)
                        {
                            LanguagePicker.Focus();
                        }
                        else if (_tvParentRow == 3)
                        {
                            LanguagePicker2.Focus();
                        }
                        else if (_tvParentRow == 4)
                        {
                            LanguagePicker3.Focus();
                        }
                        else if (_tvParentRow == 5 && _blockedVideos.Count > 0)
                        {
                            var bIdx = Math.Clamp(_tvBlockedVideoIndex, 0, _blockedVideos.Count - 1);
                            var item = _blockedVideos[bIdx];
                            OnUnblockVideoClicked(new Button { CommandParameter = item }, EventArgs.Empty);
                        }
                        else if (_tvParentRow == 6)
                        {
                            if (_tvParentCol == 0) NewPinEntry.Focus();
                            else OnSavePinClicked(null, EventArgs.Empty);
                        }
                        else if (_tvParentRow == 7)
                        {
                            OnAllowChildDiscoverToggleTapped(null, EventArgs.Empty);
                        }
                        else if (_tvParentRow == 8)
                        {
                            OnShowOnboardingClicked(null, EventArgs.Empty);
                        }
                        else if (_tvParentRow == 9)
                        {
                            OnQuitAppClicked(null, EventArgs.Empty);
                        }
                        else if (_tvParentRow == 10)
                        {
                            if (_tvParentCol == 0) OnViewTermsClicked(null, EventArgs.Empty);
                            else OnContactSupportClicked(null, EventArgs.Empty);
                        }
                    }
                });
                return true;
            }
            return false;
        }

        // 5. Experiência TV Principal (TV-First 10-Foot UI)
        if (TvHomeContainer.IsVisible)
        {
            // Atalho de configurações
            if (code == Android.Views.Keycode.Menu || 
                code == Android.Views.Keycode.Settings || 
                code == Android.Views.Keycode.Info || 
                code == Android.Views.Keycode.Guide || 
                code == Android.Views.Keycode.TvContentsMenu ||
                code == Android.Views.Keycode.ProgRed)
            {
                MainThread.BeginInvokeOnMainThread(() => OnParentsClicked(TvAdultsBtnBorder, EventArgs.Empty));
                return true;
            }

            // D-Pad CIMA
            if (code == Android.Views.Keycode.DpadUp)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    int nextRow = _tvFocusRow - 1;
                    while (nextRow > 0 && !IsTvRowAvailable(nextRow))
                    {
                        nextRow--;
                    }
                    _tvFocusRow = Math.Max(0, nextRow);
                    UpdateTvVisualFocus();
                });
                return true;
            }

            // D-Pad BAIXO
            if (code == Android.Views.Keycode.DpadDown)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    int nextRow = _tvFocusRow + 1;
                    while (nextRow < 4 && !IsTvRowAvailable(nextRow))
                    {
                        nextRow++;
                    }
                    if (IsTvRowAvailable(nextRow))
                    {
                        _tvFocusRow = nextRow;
                    }
                    else if (IsTvRowAvailable(5))
                    {
                        _tvFocusRow = 5;
                    }
                    UpdateTvVisualFocus();
                });
                return true;
            }

            // D-Pad ESQUERDA
            if (code == Android.Views.Keycode.DpadLeft)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvFocusRow == 0)
                    {
                        if (_tvHeaderColIndex > 0) _tvHeaderColIndex--;
                    }
                    else if (_tvFocusRow == 2 && _tvContinueColIndex > 0)
                    {
                        _tvContinueColIndex--;
                    }
                    else if (_tvFocusRow == 3 && _tvFavoritesColIndex > 0)
                    {
                        _tvFavoritesColIndex--;
                    }
                    else if (_tvFocusRow == 4 && _tvCategoriesColIndex > 0)
                    {
                        _tvCategoriesColIndex--;
                    }
                    UpdateTvVisualFocus();
                });
                return true;
            }

            // D-Pad DIREITA
            if (code == Android.Views.Keycode.DpadRight)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvFocusRow == 0)
                    {
                        var maxHeaderIndex = _tvCategories.Count; // _tvCategories.Count é o botão Adultos
                        if (_tvHeaderColIndex < maxHeaderIndex) _tvHeaderColIndex++;
                    }
                    else if (_tvFocusRow == 2 && _tvContinueColIndex < _tvContinueVideos.Count - 1)
                    {
                        _tvContinueColIndex++;
                    }
                    else if (_tvFocusRow == 3 && _tvFavoritesColIndex < _tvFavoriteVideos.Count - 1)
                    {
                        _tvFavoritesColIndex++;
                    }
                    else if (_tvFocusRow == 4 && _tvCategoriesColIndex < _tvCategories.Count - 1)
                    {
                        _tvCategoriesColIndex++;
                    }
                    UpdateTvVisualFocus();
                });
                return true;
            }

            // D-Pad OK / ENTER / BOTÃO A
            if (code == Android.Views.Keycode.DpadCenter || code == Android.Views.Keycode.Enter || code == Android.Views.Keycode.ButtonA)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_tvFocusRow == 0)
                    {
                        if (_tvHeaderColIndex < _tvCategories.Count)
                        {
                            FilterTvCategory(_tvCategories[_tvHeaderColIndex].Id);
                        }
                        else
                        {
                            OnParentsClicked(TvAdultsBtnBorder, EventArgs.Empty);
                        }
                    }
                    else if (_tvFocusRow == 1)
                    {
                        OnTvHeroTapped(null, EventArgs.Empty);
                    }
                    else if (_tvFocusRow == 2 && _tvContinueVideos.Count > 0)
                    {
                        var idx = Math.Clamp(_tvContinueColIndex, 0, _tvContinueVideos.Count - 1);
                        StartSession(_tvContinueVideos[idx]);
                    }
                    else if (_tvFocusRow == 3 && _tvFavoriteVideos.Count > 0)
                    {
                        var idx = Math.Clamp(_tvFavoritesColIndex, 0, _tvFavoriteVideos.Count - 1);
                        StartSession(_tvFavoriteVideos[idx]);
                    }
                    else if (_tvFocusRow == 4 && _tvCategories.Count > 0)
                    {
                        var idx = Math.Clamp(_tvCategoriesColIndex, 0, _tvCategories.Count - 1);
                        FilterTvCategory(_tvCategories[idx].Id);
                    }
                    else if (_tvFocusRow == 5)
                    {
                        OnParentsClicked(TvEmptyAdultsBtn, EventArgs.Empty);
                    }
                });
                return true;
            }

            // Botão VOLTAR na TV
            if (code == Android.Views.Keycode.Back)
            {
                if (!string.Equals(_selectedTvCategory, "all", StringComparison.OrdinalIgnoreCase))
                {
                    MainThread.BeginInvokeOnMainThread(() => FilterTvCategory("all"));
                    return true;
                }
            }
        }

        return false;
    }
#endif

    private async Task PromptScreenLockIfNeededAsync()
    {
#if ANDROID
        if (DeviceInfo.Idiom == DeviceIdiom.TV || Services.DeviceModeService.IsTv) return;

        // Se os termos ou onboarding ainda estão a decorrer, não interrompe
        if (!Preferences.Default.Get(TermsAcceptedKey, false) || !Preferences.Default.Get(OnboardingCompletedKey, false))
        {
            return;
        }

        // Se o ecrã já estiver fixado/bloqueado ou o painel de pais estiver aberto, não precisa de perguntar
        if (IsScreenLocked() || (ParentOverlay is not null && ParentOverlay.IsVisible))
        {
            return;
        }

        // Verifica se existem vídeos disponíveis para a criança (vídeos avulsos, playlists aprovadas, feed ou temas)
        var hasVideosForChild = _childDisplayVideos.Count > 0 
            || _videos.Count > 0 
            || _approvedPlaylists.Count > 0 
            || _curationMode == "themes";

        if (!hasVideosForChild) return;

        await Task.Delay(800);

        try
        {
            if (IsScreenLocked() || (ParentOverlay is not null && ParentOverlay.IsVisible)) return;

            var lockChoice = await DisplayAlert(
                AppStrings.Get("DialogProtectScreenTitle"),
                AppStrings.Get("DialogProtectScreenMsg"),
                AppStrings.Get("BtnYesLockScreen"),
                AppStrings.Get("BtnNotNow")
            );

            if (lockChoice)
            {
                var activity = Platform.CurrentActivity;
                try
                {
                    activity?.StartLockTask();
                    await DisplayAlert(
                        AppStrings.Get("DialogLockSuccessTitle"),
                        AppStrings.Get("DialogLockSuccessMsg"),
                        AppStrings.Get("BtnUnderstood")
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LockTaskError] {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PromptScreenLockIfNeededAsync] {ex.Message}");
        }
#endif
    }

    private bool _hasCheckedStartupLock = false;
    private bool _hasRunSplashSequence = false;
    private bool _isSplashDismissed = false;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        StartupProfiler.Milestone("MainPage OnAppearing Start");
        try
        {
            SetFullScreen(true);

            // Iniciar animação mágica de splash para as crianças
            _ = RunAnimatedSplashSequenceAsync();

            // Verifica a aceitação dos Termos de Utilização & Privacidade
            if (!Preferences.Default.Get(TermsAcceptedKey, false))
            {
                ShowTermsOverlay(isMandatory: true);
            }
            else if (!Preferences.Default.Get(OnboardingCompletedKey, false))
            {
                ShowOnboardingOverlay();
            }

            if (Services.DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
            {
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
                {
                    try
                    {
                        UpdateTvUI();
                        UpdateTvVisualFocus();
                        StartupProfiler.Milestone("TV UI Rendered");
                        System.Diagnostics.Debug.WriteLine(StartupProfiler.GetReport());
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TvUI] {ex.Message}");
                    }
                });
            }
            else
            {
                StartupProfiler.Milestone("Mobile UI Rendered");
                System.Diagnostics.Debug.WriteLine(StartupProfiler.GetReport());
            }

            if (!_hasCheckedStartupLock)
            {
                _hasCheckedStartupLock = true;
                _ = PromptScreenLockIfNeededAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnAppearing] {ex.Message}");
        }
    }

    #region Splash Screen Animado

    private async Task RunAnimatedSplashSequenceAsync()
    {
        if (_hasRunSplashSequence) return;
        _hasRunSplashSequence = true;

        try
        {
            if (AnimatedSplashOverlay == null || !AnimatedSplashOverlay.IsVisible)
                return;

            // Configuração dos estados iniciais para animação
            AnimatedSplashOverlay.Opacity = 1;
            AnimatedSplashOverlay.Scale = 1;

            if (SplashMascotCard != null)
            {
                SplashMascotCard.Scale = 0.2;
                SplashMascotCard.Opacity = 0;
            }
            if (SplashGlowCircle != null)
            {
                SplashGlowCircle.Scale = 0.4;
                SplashGlowCircle.Opacity = 0;
            }
            if (SplashTitleLayout != null)
            {
                SplashTitleLayout.Opacity = 0;
                SplashTitleLayout.TranslationY = 25;
            }
            if (SplashTagline != null)
            {
                SplashTagline.Opacity = 0;
                SplashTagline.TranslationY = 15;
            }
            if (SplashLoadingPill != null)
            {
                SplashLoadingPill.Opacity = 0;
            }

            // 1. Entrada com Salto Elástico do Mascote (Bounce / Pop)
            Task mascotFade = SplashMascotCard?.FadeTo(1, 350, Easing.CubicOut) ?? Task.CompletedTask;
            Task mascotScale = SplashMascotCard?.ScaleTo(1.18, 550, Easing.SpringOut) ?? Task.CompletedTask;
            Task glowFade = SplashGlowCircle?.FadeTo(0.4, 400) ?? Task.CompletedTask;
            Task glowScale = SplashGlowCircle?.ScaleTo(1.3, 700, Easing.SinInOut) ?? Task.CompletedTask;
            await Task.WhenAll(mascotFade, mascotScale, glowFade, glowScale);

            if (_isSplashDismissed) return;

            // 2. Wobble / Dança alegre do Mascote
            if (SplashMascotCard != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            if (SplashMascotCard == null) return;
                            await SplashMascotCard.RotateTo(-12, 130, Easing.SinInOut);
                            await SplashMascotCard.RotateTo(12, 150, Easing.SinInOut);
                            await SplashMascotCard.RotateTo(-6, 120, Easing.SinInOut);
                            await SplashMascotCard.RotateTo(0, 100, Easing.SinInOut);
                            await SplashMascotCard.ScaleTo(1.0, 200, Easing.CubicIn);
                        });
                    }
                    catch { }
                });
            }

            // 3. Estrelas / Balões a surgir e flutuar
            _ = AnimateFloatingParticlesAsync();

            // 4. Entrada do Título, Subtítulo e Indicador de Carga
            Task titleFade = SplashTitleLayout?.FadeTo(1, 400, Easing.CubicOut) ?? Task.CompletedTask;
            Task titleSlide = SplashTitleLayout?.TranslateTo(0, 0, 400, Easing.CubicOut) ?? Task.CompletedTask;
            Task tagFade = SplashTagline?.FadeTo(1, 450, Easing.CubicOut) ?? Task.CompletedTask;
            Task tagSlide = SplashTagline?.TranslateTo(0, 0, 450, Easing.CubicOut) ?? Task.CompletedTask;
            Task pillFade = SplashLoadingPill?.FadeTo(1, 400, Easing.CubicOut) ?? Task.CompletedTask;
            await Task.WhenAll(titleFade, titleSlide, tagFade, tagSlide, pillFade);

            if (_isSplashDismissed) return;

            // 5. Pequeno compasso de espera lúdico (~1.5s)
            await Task.Delay(1500);

            // 6. Transição suave de Saída para o ecrã principal
            await DismissSplashAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Splash] {ex.Message}");
            if (AnimatedSplashOverlay != null)
            {
                AnimatedSplashOverlay.IsVisible = false;
            }
        }
    }

    private async Task AnimateFloatingParticlesAsync()
    {
        try
        {
            var stars = new[] { SplashStar1, SplashStar2, SplashStar3, SplashStar4, SplashStar5, SplashStar6 };
            for (int i = 0; i < stars.Length; i++)
            {
                var star = stars[i];
                if (star == null) continue;

                star.Scale = 0.2;
                star.Opacity = 0;

                int index = i;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(index * 90);
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            if (star == null) return;
                            _ = star.FadeTo(0.9, 350);
                            _ = star.ScaleTo(1.15, 500, Easing.SpringOut);
                            _ = star.RelRotateTo(360, 2200, Easing.Linear);
                            _ = star.TranslateTo(star.TranslationX + (index % 2 == 0 ? 12 : -12), star.TranslationY - 18, 1400, Easing.SinInOut);
                        });
                    }
                    catch { }
                });
            }
        }
        catch { }
    }

    private async Task DismissSplashAsync()
    {
        if (_isSplashDismissed || AnimatedSplashOverlay == null || !AnimatedSplashOverlay.IsVisible)
            return;

        _isSplashDismissed = true;

        try
        {
            Task mascotExit = SplashMascotCard?.ScaleTo(1.22, 280, Easing.CubicOut) ?? Task.CompletedTask;
            Task overlayFade = AnimatedSplashOverlay.FadeTo(0, 420, Easing.CubicInOut);
            Task overlayScale = AnimatedSplashOverlay.ScaleTo(1.03, 420, Easing.CubicInOut);
            await Task.WhenAll(mascotExit, overlayFade, overlayScale);
        }
        catch { }
        finally
        {
#if ANDROID
            MainActivity.DismissNativeSplash();
#endif
            if (AnimatedSplashOverlay != null)
            {
                AnimatedSplashOverlay.IsVisible = false;
            }
        }
    }

    private void OnSplashTapped(object? sender, EventArgs e)
    {
        _ = DismissSplashAsync();
    }

    #endregion

    private bool _isSessionExpired = false;
    private bool _sessionStarted = false;

    private void UpdateTimeExpiredUI()
    {
        if (DeviceModeService.IsTv)
        {
            if (TvHomeContainer is not null) TvHomeContainer.IsVisible = !_isSessionExpired;
            if (LibraryView is not null) LibraryView.IsVisible = false;
            if (HeaderView is not null) HeaderView.IsVisible = false;
            if (!_isSessionExpired) UpdateTvUI();
        }
        else
        {
            if (TvHomeContainer is not null) TvHomeContainer.IsVisible = false;
            if (LibraryView is not null) LibraryView.IsVisible = !_isSessionExpired;
            if (HeaderView is not null) HeaderView.IsVisible = true;
        }

        if (TimeExpiredView is not null)
        {
            TimeExpiredView.IsVisible = _isSessionExpired;
        }

        if (_isSessionExpired && (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV))
        {
            _tvExpiredRow = 0;
            _tvExpiredCol = 1; // Default +15m
            UpdateTvExpiredVisualFocus();
        }

        if (SubtitleLabel is not null)
        {
            SubtitleLabel.Text = _isSessionExpired ? AppStrings.Get("SubtitleTimeExpired") : AppStrings.Get("AppSubtitle");
        }

        UpdateChildDiscoverButtonVisibility();
    }

    private int _mealDurationMinutes = 15;

    private void UpdateMealDurationUI()
    {
        if (MinutesLabel is not null)
        {
            MinutesLabel.Text = AppStrings.Get("MinutesFormat", _mealDurationMinutes);
        }
        if (ParentAddMinutesTopBtn is not null)
        {
            ParentAddMinutesTopBtn.Text = $"⏱️ +{_mealDurationMinutes}m";
            ParentAddMinutesTopBtn.CommandParameter = _mealDurationMinutes.ToString();
        }
    }

    private void OnDecreaseMinutesClicked(object? sender, EventArgs e)
    {
        _mealDurationMinutes = Math.Max(5, _mealDurationMinutes - 5);
        Preferences.Default.Set(DurationKey, (double)_mealDurationMinutes);
        UpdateMealDurationUI();
        _isSessionExpired = false;
        _sessionStarted = false;
        _timer?.Stop();
        UpdateTimeExpiredUI();
    }

    private void OnIncreaseMinutesClicked(object? sender, EventArgs e)
    {
        _mealDurationMinutes = Math.Min(45, _mealDurationMinutes + 5);
        Preferences.Default.Set(DurationKey, (double)_mealDurationMinutes);
        UpdateMealDurationUI();
        _isSessionExpired = false;
        _sessionStarted = false;
        _timer?.Stop();
        UpdateTimeExpiredUI();
    }

    private void OnResetSessionClicked(object sender, EventArgs e)
    {
        _isSessionExpired = false;
        _sessionStarted = true;
        _sessionEndsAt = DateTime.Now.AddMinutes(_mealDurationMinutes);
        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
        UpdateTimeExpiredUI();
        ParentOverlay.IsVisible = false;
    }

    private async void OnQuickAddMinutesClicked(object? sender, EventArgs e)
    {
        int minutesToAdd = 15;
        if (sender is Button btn && int.TryParse(btn.CommandParameter?.ToString(), out var m))
        {
            minutesToAdd = m;
        }

        // Se já estiver na Área de Adultos, o PIN já foi autenticado: aplica imediatamente
        if (ParentOverlay.IsVisible)
        {
            ApplyMinutesExtension(minutesToAdd);
            return;
        }

        // Se estiver no TimeExpiredView ou ecrã bloqueado, autentica por biometria ou PIN
        var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
        var isBiometricEnabled = !isTv && Preferences.Default.Get(BiometricEnabledKey, true);
        if (isBiometricEnabled && await _biometricService.IsBiometricAvailableAsync())
        {
            var result = await _biometricService.AuthenticateAsync(
                AppStrings.Get("BiometricTitle"),
                AppStrings.Get("BiometricSubtitle"),
                AppStrings.Get("BiometricNegativeButton")
            );

            if (result == Services.BiometricAuthResult.Success)
            {
                ApplyMinutesExtension(minutesToAdd);
                return;
            }
        }

        _pendingMinutesToAdd = minutesToAdd;
        ShowPinPrompt();
    }

    private void ApplyMinutesExtension(int minutes)
    {
        if (_sessionStarted && !_isSessionExpired)
        {
            _sessionEndsAt = _sessionEndsAt.AddMinutes(minutes);
        }
        else
        {
            _sessionStarted = true;
            _sessionEndsAt = DateTime.Now.AddMinutes(minutes);
            if (!_timer.IsRunning)
            {
                _timer.Start();
            }
        }
        _isSessionExpired = false;
        UpdateTimeExpiredUI();
        if (ParentOverlay.IsVisible)
        {
            ParentOverlay.IsVisible = false;
        }
    }

    private void OnVideoSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is ApprovedVideo video)
        {
            if (sender is CollectionView cv)
            {
                cv.SelectedItem = null;
            }
            StartSession(video);
        }
    }

    private void OnPlayCardTapped(object? sender, TappedEventArgs e)
    {
        if (_isSessionExpired)
        {
            UpdateTimeExpiredUI();
            return;
        }

        if (e.Parameter is ApprovedVideo video || (sender is BindableObject bo && bo.BindingContext is ApprovedVideo videoCtx && (video = videoCtx) is not null))
        {
            StartSession(video);
        }
    }

    private void OnPlayClicked(object sender, EventArgs e) 
    { 
        if (_isSessionExpired)
        {
            UpdateTimeExpiredUI();
            return;
        }

        if (((Button)sender).CommandParameter is ApprovedVideo video) 
        { 
            StartSession(video); 
        }
    }

    private void StartSession(ApprovedVideo video) 
    { 
        if (_isSessionExpired)
        {
            UpdateTimeExpiredUI();
            return;
        }

        if (HeaderView is not null) HeaderView.IsVisible = false;
        if (TvHomeContainer is not null) TvHomeContainer.IsVisible = false;
        MainGrid.Padding = new Thickness(0); // Remove todo o padding para ecrã 100% total
        ParentOverlay.IsVisible = false; 
        LibraryView.IsVisible = false; 
        TimeExpiredView.IsVisible = false;
        PlayerView.IsVisible = true; 
        if (PlayerTopBar is not null)
        {
            PlayerTopBar.Opacity = 1;
            PlayerTopBar.IsVisible = true;
        }
        if (PlayerVideoTitleLabel is not null)
        {
            PlayerVideoTitleLabel.Text = video.Title;
        }
        if (PlayerTimeElapsedLabel is not null)
        {
            PlayerTimeElapsedLabel.Text = "00:00";
        }
        ControlsBar.Opacity = 1;
        ControlsBar.IsVisible = true;
        SubtitleLabel.Text = video.Title; 
        
        // Se a sessão de refeição ainda não tinha começado, inicia a contagem global agora
        if (!_sessionStarted)
        {
            _sessionStarted = true;
            _sessionEndsAt = DateTime.Now.AddMinutes(_mealDurationMinutes); 
            _timer.Start(); 
        }

        if (!DeviceModeService.IsTv)
        {
            SetLandscape(true); // Vídeo abre na horizontal por defeito em telemóveis
        }
        SetFullScreen(true); // Ativa ecrã completo imersivo (sem barras de sistema)
        ScheduleAutoHideControls(); // Esconde controlos automaticamente após alguns segundos

        // Preenche as sugestões de vídeos (outros vídeos da lista atual para trocar rapidamente)
        _playerSuggestionVideos.Clear();
        foreach (var v in _childDisplayVideos.Where(v => v.Id != video.Id))
        {
            _playerSuggestionVideos.Add(v);
        }
        if (PlayerSuggestionsDrawer is not null) PlayerSuggestionsDrawer.IsVisible = false;

        _isPlaying = true;
        if (PlayPauseBtn is not null) PlayPauseBtn.Text = "⏸";

        var queueIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(video.Id)) queueIds.Add(video.Id);
        if (_childDisplayVideos.Count > 0)
        {
            queueIds.AddRange(_childDisplayVideos.Select(v => v.Id));
        }
        else if (_tvContinueVideos.Count > 0)
        {
            queueIds.AddRange(_tvContinueVideos.Select(v => v.Id));
        }
        else if (_videos.Count > 0)
        {
            queueIds.AddRange(_videos.Select(v => v.Id));
        }

        YoutubePlayer.Source = new HtmlWebViewSource { Html = YoutubePlayerHtml.Build(queueIds, video.Id), BaseUrl = YoutubePlayerHtml.AppOrigin }; 
    }

    private bool _isPlaying = true;

    private async void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        try
        {
            var res = await YoutubePlayer.EvaluateJavaScriptAsync("togglePlayPause()");
            if (bool.TryParse(res, out var isPlaying))
            {
                _isPlaying = isPlaying;
            }
            else
            {
                _isPlaying = !_isPlaying;
            }
            if (PlayPauseBtn is not null)
            {
                PlayPauseBtn.Text = _isPlaying ? "⏸" : "▶";
            }
            ScheduleAutoHideControls();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnPlayPauseClicked] {ex.Message}");
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e) 
    { 
        if (!_sessionStarted) return;

        var remaining = _sessionEndsAt - DateTime.Now; 
        if (remaining <= TimeSpan.Zero) 
        { 
            _timer.Stop();
            _sessionStarted = false;
            _isSessionExpired = true; // Bloqueio total ativado: a criança não pode ver mais vídeos
            await FinishSessionAsync(); 
            return; 
        } 
        TimerLabel.Text = AppStrings.Get("TimeRemaining", $"{remaining.Minutes:00}", $"{remaining.Seconds:00}"); 
    }

    private async void OnBackClicked(object sender, EventArgs e) => await FinishSessionAsync();

    private async Task FinishSessionAsync() 
    { 
        // Nota: O timer NÃO é parado no botão Voltar para não reiniciar a contagem ao mudar de vídeo!
        // A contagem do tempo de refeição continua a correr continuamente.
        _hideControlsCts?.Cancel();
        if (PlayerSuggestionsDrawer is not null) PlayerSuggestionsDrawer.IsVisible = false;
        SetFullScreen(true); // Mantém barras do sistema ocultas/integradas com a cor do tema
        if (!DeviceModeService.IsTv)
        {
            SetLandscape(false); // Restaura vertical para a biblioteca em telemóveis
            if (HeaderView is not null) HeaderView.IsVisible = true;
        }
        MainGrid.Padding = new Thickness(0); // Restaura o padding normal em ecrã total
        try { await YoutubePlayer.EvaluateJavaScriptAsync("endSession()"); } catch { } 
        YoutubePlayer.Source = null; 
        PlayerView.IsVisible = false; 
        UpdateTimeExpiredUI(); // Mostra TimeExpiredView se expirou, ou TvHomeContainer/LibraryView
        if (DeviceModeService.IsTv)
        {
            UpdateTvVisualFocus();
        }
    }
    private void OnPlayerNavigating(object sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Url)) return;
        if (e.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || e.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
        
        // Se receber notificação para alternar controlos a partir do toque no ecrã web
        if (e.Url.StartsWith("ychicatube://toggle-controls", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ControlsBar.IsVisible)
                {
                    OnHideControlsClicked(this, EventArgs.Empty);
                }
                else
                {
                    ShowControls();
                }
            });
            return;
        }

        // Se o vídeo terminar no leitor, mostra automaticamente as opções de próximas histórias
        if (e.Url.StartsWith("ychicatube://video-ended", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ShowControls();
                if (PlayerSuggestionsDrawer is not null)
                {
                    PlayerSuggestionsDrawer.IsVisible = true;
                    _hideControlsCts?.Cancel(); // Mantém visível para a criança poder escolher
                }
            });
            return;
        }

        // Se o utilizador clicar num vídeo sugerido / recomendado no leitor
        if (YoutubePlayerHtml.TryGetVideoId(e.Url, out var newVideoId) && !string.IsNullOrEmpty(newVideoId))
        {
            e.Cancel = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await YoutubePlayer.EvaluateJavaScriptAsync($"if(window.playVideoId){{window.playVideoId('{newVideoId}');}}else if(typeof player!=='undefined'&&player.loadVideoById){{player.loadVideoById('{newVideoId}');player.playVideo();}}");
                    
                    var existing = _childDisplayVideos.FirstOrDefault(v => v.Id == newVideoId);
                    if (existing != null)
                    {
                        SubtitleLabel.Text = existing.Title;
                    }
                    
                    _playerSuggestionVideos.Clear();
                    foreach (var v in _childDisplayVideos.Where(v => v.Id != newVideoId))
                    {
                        _playerSuggestionVideos.Add(v);
                    }
                    if (PlayerSuggestionsDrawer is not null) PlayerSuggestionsDrawer.IsVisible = false;
                }
                catch { }
            });
            return;
        }

        if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.EndsWith("ychicatube.app") || host.EndsWith("chicatube.app") || 
                host == "youtube.com" || host.EndsWith(".youtube.com") || 
                host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com") || 
                host.EndsWith(".googlevideo.com") || host.EndsWith(".google.com") || 
                host.EndsWith(".googleapis.com") || host.EndsWith(".doubleclick.net") || 
                host.EndsWith(".gstatic.com") || host.EndsWith(".ytimg.com"))
            {
                // Permite o carregamento de recursos internos e streaming do YouTube
                return;
            }
        }
        e.Cancel = true;
    }
    protected override bool OnBackButtonPressed()
    {
        // 0. Se os Termos de Utilização estiverem abertos
        if (TermsOverlay is not null && TermsOverlay.IsVisible)
        {
            if (TermsCloseBtn is not null && TermsCloseBtn.IsVisible)
            {
                TermsOverlay.IsVisible = false;
            }
            return true;
        }

        // 1. Se o player de vídeo do YouTube estiver aberto, sai do vídeo e volta à biblioteca
        if (PlayerView.IsVisible)
        {
            OnBackClicked(this, EventArgs.Empty);
            return true;
        }

        // 2. Se o modal de autorização remota ou PIN estiver visível, fecha-o
        if (TvRemoteAuthPromptOverlay is not null && TvRemoteAuthPromptOverlay.IsVisible)
        {
            TvRemoteAuthPromptOverlay.IsVisible = false;
            return true;
        }

        if (PinPromptOverlay.IsVisible)
        {
            PinPromptOverlay.IsVisible = false;
            return true;
        }

        // 4. Se a Área de Adultos estiver aberta, fecha com a mesma verificação de bloqueio
        if (ParentOverlay.IsVisible)
        {
            OnCloseParentsClicked(this, EventArgs.Empty);
            return true;
        }

        // 5. Durante o bloqueio de ecrã infantil na biblioteca principal, impede a criança de fechar a app
        return true;
    }

    private async void OnLockAppClicked(object sender, EventArgs e)
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        try
        {
            var currentPin = Preferences.Default.Get(PinKey, "1234");
            var confirm = await DisplayAlert(
                AppStrings.Get("DialogConfirmLockTitle"),
                AppStrings.Get("DialogConfirmLockMsg", currentPin),
                AppStrings.Get("BtnYesLockScreen"),
                AppStrings.Get("BtnCancel")
            );

            if (confirm)
            {
                activity?.StartLockTask();
                ParentOverlay.IsVisible = false; // Fecha a área de adultos para ficar pronto para a criança
                await DisplayAlert(
                    AppStrings.Get("DialogLockSuccessTitle"),
                    AppStrings.Get("DialogLockSuccessShortMsg"),
                    AppStrings.Get("BtnUnderstood")
                );
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                AppStrings.Get("DialogLockErrorTitle"),
                AppStrings.Get("DialogLockErrorMsg"),
                AppStrings.Get("BtnOk")
            );
            System.Diagnostics.Debug.WriteLine($"[LockTaskError] {ex.Message}");
        }
#endif
    }

    private async void OnUnlockAppClicked(object sender, EventArgs e)
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        try
        {
            activity?.StopLockTask();
            await DisplayAlert(
                AppStrings.Get("DialogScreenUnlockedTitle"),
                AppStrings.Get("DialogScreenUnlockedMsg"),
                AppStrings.Get("BtnOk")
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StopLockTaskError] {ex.Message}");
        }
#endif
    }

    private async void OnQuitAppClicked(object sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            AppStrings.Get("DialogQuitTitle"),
            AppStrings.Get("DialogQuitMsg"),
            AppStrings.Get("BtnYesQuit"),
            AppStrings.Get("BtnCancel")
        );
        if (confirm)
        {
#if ANDROID
            var activity = Platform.CurrentActivity;
            try
            {
                activity?.StopLockTask(); // Desbloqueia e liberta o telemóvel imediatamente a 100%
            }
            catch { }
            MainActivity.IsLegitimateExitRequested = true;
            activity?.FinishAffinity();
#endif
            Application.Current?.Quit();
        }
    }

    private void LoadVideos()
    {
        try
        {
            _videos.Clear();
            var list = JsonSerializer.Deserialize<List<ApprovedVideo>>(Preferences.Default.Get(VideosKey, "[]")) ?? [];
            foreach (var video in list)
            {
                _videos.Add(video);
            }

            LoadApprovedPlaylists();
        }
        catch { }
        UpdateChildDiscoverButtonVisibility();
        UpdateTvUI();
    }

    #region Gestão de Modos de Curadoria e Temas

    private void LoadAllowedThemes()
    {
        try
        {
            _parentThemes.Clear();
            _childThemeChips.Clear();

            var currentLangs = GetSelectedLanguageCodes();
            var defaultThemes = YoutubeSearchService.GetDefaultThemes(currentLangs);
            var savedThemesJson = Preferences.Default.Get(AllowedThemesKey, string.Empty);

            List<string> themesList;
            if (!string.IsNullOrWhiteSpace(savedThemesJson))
            {
                themesList = JsonSerializer.Deserialize<List<string>>(savedThemesJson) ?? defaultThemes;
            }
            else
            {
                themesList = defaultThemes;
            }

            if (themesList.Count == 0) themesList = defaultThemes;

            foreach (var t in themesList)
            {
                _parentThemes.Add(new ThemeChipItem { Name = t });
            }

            // Popula os chips para a criança: "Todos" + cada tema configurado
            _childThemeChips.Add(new ThemeChipItem { Name = AppStrings.Get("ThemeChipAll"), IsSelected = true });
            foreach (var t in themesList)
            {
                _childThemeChips.Add(new ThemeChipItem { Name = t, IsSelected = false });
            }

            _activeSelectedTheme = AppStrings.Get("ThemeChipAll");

            // Carrega cache persistente de vídeos de temas
            LoadPersistedThemeCache(string.Join("_", currentLangs));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadAllowedThemes] {ex.Message}");
        }
    }

    private void LoadPersistedThemeCache(string lang)
    {
        try
        {
            var cacheKey = $"theme-cache-v2-{lang}";
            var json = Preferences.Default.Get(cacheKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<ApprovedVideo>>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _themeVideosCache[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadPersistedThemeCache] {ex.Message}");
        }
    }

    private void SavePersistedThemeCache()
    {
        try
        {
            var currentLangs = GetSelectedLanguageCodes();
            var cacheKey = $"theme-cache-v2-{string.Join("_", currentLangs)}";
            var json = JsonSerializer.Serialize(_themeVideosCache);
            Preferences.Default.Set(cacheKey, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SavePersistedThemeCache] {ex.Message}");
        }
    }

    private void SaveAllowedThemes()
    {
        var list = _parentThemes.Select(t => t.Name).ToList();
        Preferences.Default.Set(AllowedThemesKey, JsonSerializer.Serialize(list));

        // Sincroniza os chips da criança
        _childThemeChips.Clear();
        _childThemeChips.Add(new ThemeChipItem { Name = AppStrings.Get("ThemeChipAll"), IsSelected = string.IsNullOrEmpty(_activeSelectedTheme) || _activeSelectedTheme == AppStrings.Get("ThemeChipAll") });
        foreach (var t in list)
        {
            _childThemeChips.Add(new ThemeChipItem { Name = t, IsSelected = t.Equals(_activeSelectedTheme, StringComparison.OrdinalIgnoreCase) });
        }

        // Sincroniza as categorias da Smart TV
        InitTvCategories();
        PreloadThemesInBackground();
    }

    private void OnSelectModeApprovedTapped(object? sender, EventArgs e)
    {
        _curationMode = "approved";
        Preferences.Default.Set(CurationModeKey, _curationMode);
        UpdateCurationModeUI();
        LoadVideos();
        UpdateTvUI();
    }

    private void OnSelectModeThemesTapped(object? sender, EventArgs e)
    {
        _curationMode = "themes";
        Preferences.Default.Set(CurationModeKey, _curationMode);
        UpdateCurationModeUI();
        _ = LoadThemeVideosForChild(_activeSelectedTheme);
        UpdateTvUI();
    }

    private void UpdateCurationModeUI()
    {
        var isApproved = _curationMode == "approved";
        
        if (ModeApprovedCard is not null)
        {
            ModeApprovedCard.Stroke = isApproved ? (Color)Application.Current!.Resources["BrandGreen"] : (Color)Application.Current!.Resources["BorderLight"];
            ModeApprovedCard.StrokeThickness = isApproved ? 2.5 : 1;
        }

        if (ModeThemesCard is not null)
        {
            ModeThemesCard.Stroke = !isApproved ? (Color)Application.Current!.Resources["BrandGreen"] : (Color)Application.Current!.Resources["BorderLight"];
            ModeThemesCard.StrokeThickness = !isApproved ? 2.5 : 1;
        }

        if (ThemesManagerContainer is not null)
        {
            ThemesManagerContainer.IsVisible = !isApproved;
        }

        if (ParentYoutubeSearchContainer is not null)
        {
            ParentYoutubeSearchContainer.IsVisible = isApproved;
        }

        if (ParentRightColumn is not null)
        {
            ParentRightColumn.IsVisible = true;
        }

        if (ChildThemesBarContainer is not null)
        {
            ChildThemesBarContainer.IsVisible = !isApproved && _childThemeChips.Count > 0;
        }
    }

    private void OnAddNewThemeClicked(object? sender, EventArgs e)
    {
        var newTheme = NewThemeEntry?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(newTheme))
        {
            if (!_parentThemes.Any(t => t.Name.Equals(newTheme, StringComparison.OrdinalIgnoreCase)))
            {
                _parentThemes.Add(new ThemeChipItem { Name = newTheme });
                SaveAllowedThemes();
                _themeVideosCache.Remove(newTheme);
            }
            if (NewThemeEntry is not null)
            {
                NewThemeEntry.Text = string.Empty;
                NewThemeEntry.Unfocus();
            }
            if (ClearThemeTextBtn is not null)
            {
                ClearThemeTextBtn.IsVisible = false;
            }
            if (_curationMode == "themes")
            {
                _ = LoadThemeVideosForChild(newTheme);
            }
        }
    }

    private void OnDeleteThemeClicked(object? sender, EventArgs e)
    {
        ThemeChipItem? item = null;
        if (sender is Button btn && btn.CommandParameter is ThemeChipItem tci) item = tci;
        else if (sender is BindableObject bo && bo.BindingContext is ThemeChipItem boTci) item = boTci;

        if (item != null)
        {
            _parentThemes.Remove(item);
            _themeVideosCache.Remove(item.Name);
            SaveAllowedThemes();
            if (_activeSelectedTheme.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
            {
                _activeSelectedTheme = AppStrings.Get("ThemeChipAll");
                if (_curationMode == "themes")
                {
                    _ = LoadThemeVideosForChild(_activeSelectedTheme);
                }
            }
        }
    }

    private void OnRestoreDefaultThemesClicked(object? sender, EventArgs e)
    {
        var currentLangs = GetSelectedLanguageCodes();
        var defaultThemes = YoutubeSearchService.GetDefaultThemes(currentLangs);
        _parentThemes.Clear();
        foreach (var t in defaultThemes)
        {
            _parentThemes.Add(new ThemeChipItem { Name = t });
        }
        SaveAllowedThemes();
        _themeVideosCache.Clear();
        if (_curationMode == "themes")
        {
            _activeSelectedTheme = AppStrings.Get("ThemeChipAll");
            _ = LoadThemeVideosForChild(_activeSelectedTheme);
        }
    }

    private async void OnChildThemeChipTapped(object? sender, TappedEventArgs e)
    {
        ThemeChipItem? selected = null;
        if (e.Parameter is ThemeChipItem tci) selected = tci;
        else if (sender is BindableObject bo && bo.BindingContext is ThemeChipItem boTci) selected = boTci;

        if (selected != null)
        {
            await LoadThemeVideosForChild(selected.Name);
        }
    }

    #region Gestão de Playlists Autorizadas (Batch Approval & Mistura Automática)

    private void LoadApprovedPlaylists()
    {
        try
        {
            _approvedPlaylists.Clear();
            var json = Preferences.Default.Get(ApprovedPlaylistsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var list = JsonSerializer.Deserialize<List<ApprovedPlaylist>>(json);
                if (list != null)
                {
                    foreach (var pl in list)
                    {
                        _approvedPlaylists.Add(pl);
                    }
                }
            }

            RebuildChildVideoFeed();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadApprovedPlaylists] {ex.Message}");
        }
    }

    private void SaveApprovedPlaylists()
    {
        try
        {
            var json = JsonSerializer.Serialize(_approvedPlaylists.ToList());
            Preferences.Default.Set(ApprovedPlaylistsKey, json);
            RebuildChildVideoFeed();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveApprovedPlaylists] {ex.Message}");
        }
    }

    private void RebuildChildVideoFeed()
    {
        try
        {
            _childDisplayVideos.Clear();

            // 1. Mistura round-robin dos episódios de todas as playlists aprovadas
            // (1 episódio de cada playlist por ronda, para variedade garantida à criança)
            var playlistLists = _approvedPlaylists
                .Where(p => p.Videos != null && p.Videos.Count > 0)
                .Select(p => p.Videos.Where(v => !_blockedVideoIds.Contains(v.Id)).ToList())
                .Where(list => list.Count > 0)
                .ToList();

            if (playlistLists.Count > 0)
            {
                int maxCount = playlistLists.Max(l => l.Count);
                for (int round = 0; round < maxCount; round++)
                {
                    foreach (var pList in playlistLists)
                    {
                        if (round < pList.Count)
                        {
                            var video = pList[round];
                            if (!_childDisplayVideos.Any(v => v.Id == video.Id))
                            {
                                _childDisplayVideos.Add(video);
                            }
                        }
                    }
                }
            }

            // 2. Adiciona vídeos individuais aprovados avulsos (se não forem duplicados)
            foreach (var v in _videos)
            {
                if (!_blockedVideoIds.Contains(v.Id) && !_childDisplayVideos.Any(cdv => cdv.Id == v.Id))
                {
                    _childDisplayVideos.Add(v);
                }
            }

            UpdateEmptyState();
            UpdateTvUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RebuildChildVideoFeed] {ex.Message}");
        }
    }

    public async void OpenPlaylistInspection(string playlistName, string playlistId = "", string thumbUrl = "", string channelTitle = "", int count = 0)
    {
        if (string.IsNullOrWhiteSpace(playlistName)) return;

        _currentInspectingPlaylistName = playlistName;
        _inspectingPlaylistVideos.Clear();

        if (PlaylistInspectTitleLabel is not null) PlaylistInspectTitleLabel.Text = playlistName;
        if (PlaylistInspectChannelLabel is not null) PlaylistInspectChannelLabel.Text = string.IsNullOrWhiteSpace(channelTitle) ? "YouTube Kids Safe" : channelTitle;
        if (PlaylistInspectCountLabel is not null) PlaylistInspectCountLabel.Text = count > 0 ? $"{count} vídeos" : "A carregar...";
        if (PlaylistInspectThumb is not null && !string.IsNullOrWhiteSpace(thumbUrl)) PlaylistInspectThumb.Source = thumbUrl;

        var isAlreadyApproved = _approvedPlaylists.Any(p => p.Title.Equals(playlistName, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(playlistId) && p.Id == playlistId));
        UpdatePlaylistInspectApproveButton(isAlreadyApproved);

        if (PlaylistInspectModalOverlay is not null)
        {
            PlaylistInspectModalOverlay.IsVisible = true;
            _tvInspectRow = 1;
            _tvInspectVideoIndex = 0;
            _tvInspectCol = 0;
            UpdateTvInspectVisualFocus();
        }

        if (PlaylistInspectIndicator is not null)
        {
            PlaylistInspectIndicator.IsVisible = true;
            PlaylistInspectIndicator.IsRunning = true;
        }

        try
        {
            var currentLang = Preferences.Default.Get(LanguageKey, "pt-PT");
            List<YoutubeSearchResult> results;

            if (!string.IsNullOrWhiteSpace(playlistId))
            {
                results = await YoutubeSearchService.GetPlaylistVideosAsync(playlistId, playlistName, currentLang, maxVideos: 30);
            }
            else
            {
                results = await YoutubeSearchService.SearchByThemeAsync(playlistName, currentLang, maxResults: 30);
            }

            foreach (var r in results)
            {
                if (!_blockedVideoIds.Contains(r.Id))
                {
                    _inspectingPlaylistVideos.Add(r);
                }
            }

            if (PlaylistInspectCountLabel is not null)
            {
                PlaylistInspectCountLabel.Text = $"{_inspectingPlaylistVideos.Count} episódios";
            }

            if (PlaylistInspectThumb is not null && (string.IsNullOrWhiteSpace(thumbUrl) || thumbUrl.Contains("placeholder")) && _inspectingPlaylistVideos.Count > 0)
            {
                PlaylistInspectThumb.Source = _inspectingPlaylistVideos[0].ThumbnailUrl;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenPlaylistInspection] {ex.Message}");
        }
        finally
        {
            if (PlaylistInspectIndicator is not null)
            {
                PlaylistInspectIndicator.IsRunning = false;
                PlaylistInspectIndicator.IsVisible = false;
            }
            UpdateTvInspectVisualFocus();
        }
    }

    private void UpdateTvInspectVisualFocus()
    {
        var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
        if (!isTv) return;

        SetVisualTvFocus(BtnClosePlaylistInspectTop, _tvInspectRow == 0);
        SetVisualTvFocus(BtnToggleApproveFullPlaylist, _tvInspectRow == 1);
        SetVisualTvFocus(BtnClosePlaylistInspectBottom, _tvInspectRow == 3);

        if (_tvInspectRow == 2 && PlaylistInspectVideosList is not null && _inspectingPlaylistVideos.Count > 0)
        {
            var idx = Math.Clamp(_tvInspectVideoIndex, 0, _inspectingPlaylistVideos.Count - 1);
            PlaylistInspectVideosList.ScrollTo(idx, position: ScrollToPosition.Center, animate: true);
        }
    }

    private void UpdatePlaylistInspectApproveButton(bool isApproved)
    {
        if (BtnToggleApproveFullPlaylist is not null)
        {
            if (isApproved)
            {
                BtnToggleApproveFullPlaylist.Text = AppStrings.Get("BtnRemoveFullPlaylist");
                BtnToggleApproveFullPlaylist.BackgroundColor = Color.FromArgb("#DC2626");
            }
            else
            {
                BtnToggleApproveFullPlaylist.Text = AppStrings.Get("BtnApproveFullPlaylist");
                BtnToggleApproveFullPlaylist.BackgroundColor = (Color)Application.Current!.Resources["BrandGreen"];
            }
        }
    }

    private async void OnToggleApproveFullPlaylistClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentInspectingPlaylistName)) return;

        var existing = _approvedPlaylists.FirstOrDefault(p => p.Title.Equals(_currentInspectingPlaylistName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Remover playlist aprovada
            _approvedPlaylists.Remove(existing);
            SaveApprovedPlaylists();
            UpdatePlaylistInspectApproveButton(false);
            await DisplayAlert(AppStrings.Get("AppTitle"), AppStrings.Get("PlaylistRemovedSuccess"), AppStrings.Get("BtnOk"));
        }
        else
        {
            // Aprovar playlist completa com todos os seus vídeos
            var videosList = _inspectingPlaylistVideos.Select(v => new ApprovedVideo(v.Id, v.Title, true)).ToList();
            var thumb = _inspectingPlaylistVideos.FirstOrDefault()?.ThumbnailUrl ?? $"https://i.ytimg.com/vi/mqdefault.jpg";
            var channel = PlaylistInspectChannelLabel?.Text ?? "Oficial";
            var newApproved = new ApprovedPlaylist(
                id: Guid.NewGuid().ToString("N")[..8],
                title: _currentInspectingPlaylistName,
                channelTitle: channel,
                thumb: thumb,
                count: videosList.Count,
                videos: videosList
            );

            _approvedPlaylists.Add(newApproved);
            SaveApprovedPlaylists();
            UpdatePlaylistInspectApproveButton(true);
            await DisplayAlert(AppStrings.Get("AppTitle"), AppStrings.Get("PlaylistApprovedSuccess"), AppStrings.Get("BtnOk"));
        }
    }

    private void OnClosePlaylistInspectModalClicked(object? sender, EventArgs e)
    {
        if (PlaylistInspectModalOverlay is not null)
        {
            PlaylistInspectModalOverlay.IsVisible = false;
        }
        _inspectingPlaylistVideos.Clear();
        if (ParentOverlay is not null && ParentOverlay.IsVisible)
        {
            UpdateTvParentVisualFocus();
        }
        else
        {
            UpdateTvUI();
            UpdateTvVisualFocus();
        }
    }

    private void OnTestPlaylistVideoAudioClicked(object? sender, EventArgs e)
    {
        YoutubeSearchResult? video = null;
        if (sender is Button btn && btn.CommandParameter is YoutubeSearchResult paramR) video = paramR;
        else if (sender is BindableObject bo && bo.BindingContext is YoutubeSearchResult ctxR) video = ctxR;

        if (video != null)
        {
            var parentItem = new ParentVideoItem
            {
                Id = video.Id,
                Title = video.Title,
                IsApproved = _videos.Any(v => v.Id == video.Id) || _approvedPlaylists.Any(p => p.Videos != null && p.Videos.Any(pv => pv.Id == video.Id))
            };
            OpenAdultPreview(parentItem);
        }
    }

    private void OnBlockPlaylistVideoClicked(object? sender, EventArgs e)
    {
        YoutubeSearchResult? video = null;
        if (sender is Button btn && btn.CommandParameter is YoutubeSearchResult paramR) video = paramR;
        else if (sender is BindableObject bo && bo.BindingContext is YoutubeSearchResult ctxR) video = ctxR;

        if (video != null)
        {
            _inspectingPlaylistVideos.Remove(video);
            BlockVideo(video.Id, video.Title);
        }
    }

    public void OnParentThemeItemTapped(object? sender, EventArgs e)
    {
        ThemeChipItem? item = null;
        if (e is TappedEventArgs te && te.Parameter is ThemeChipItem tci) item = tci;
        else if (sender is BindableObject bo && bo.BindingContext is ThemeChipItem boTci) item = boTci;

        if (item == null) return;

        OpenPlaylistInspection(item.Name);
    }

    #endregion

    private async Task LoadThemeVideosForChild(string themeName)
    {
        var allThemesChipName = AppStrings.Get("ThemeChipAll");
        if (string.IsNullOrWhiteSpace(themeName))
        {
            themeName = allThemesChipName;
        }

        _activeSelectedTheme = themeName;

        // Atualiza seleção visual dos chips
        foreach (var chip in _childThemeChips)
        {
            chip.IsSelected = chip.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase);
        }

        // Se o tema for "🌟 Todos os Temas", combina instantaneamente os vídeos de todos os temas já em cache
        if (themeName.Equals(allThemesChipName, StringComparison.OrdinalIgnoreCase))
        {
            var combined = new List<ApprovedVideo>();
            var themeEntries = _themeVideosCache.Where(kv => !kv.Key.Equals(allThemesChipName, StringComparison.OrdinalIgnoreCase)).ToList();
            
            // Intercala vídeos dos temas para variedade mágica imediata (filtrando vídeos bloqueados)
            int maxPerTheme = themeEntries.Count > 0 ? themeEntries.Max(kv => kv.Value.Count) : 0;
            for (int i = 0; i < maxPerTheme; i++)
            {
                foreach (var entry in themeEntries)
                {
                    if (i < entry.Value.Count)
                    {
                        var candidate = entry.Value[i];
                        if (!_blockedVideoIds.Contains(candidate.Id))
                        {
                            combined.Add(candidate);
                        }
                    }
                }
            }

            if (combined.Count > 0)
            {
                _childDisplayVideos.Clear();
                foreach (var v in combined)
                {
                    _childDisplayVideos.Add(v);
                }
                UpdateEmptyState();
                if (ChildThemeLoadingIndicator is not null) ChildThemeLoadingIndicator.IsVisible = false;
                if (Services.DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
                {
                    UpdateTvUI();
                }
                return;
            }
        }
        else if (_themeVideosCache.TryGetValue(themeName, out var cached) && cached.Count > 0)
        {
            // Retorno instantâneo a partir da memória excluindo vídeos bloqueados
            var filtered = cached.Where(v => !_blockedVideoIds.Contains(v.Id)).ToList();
            _childDisplayVideos.Clear();
            foreach (var v in filtered)
            {
                _childDisplayVideos.Add(v);
            }
            UpdateEmptyState();
            if (ChildThemeLoadingIndicator is not null) ChildThemeLoadingIndicator.IsVisible = false;
            if (Services.DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
            {
                UpdateTvUI();
            }
            return;
        }

        // Se ainda não estiver em cache, faz o fetch assíncrono com indicador visual instantâneo
        try
        {
            if (ChildThemeLoadingIndicator is not null)
            {
                ChildThemeLoadingIndicator.IsVisible = true;
                ChildThemeLoadingIndicator.IsRunning = true;
            }

            var currentLangs = GetSelectedLanguageCodes();
            var searchTerm = themeName.Equals(allThemesChipName, StringComparison.OrdinalIgnoreCase)
                ? (_childThemeChips.FirstOrDefault(c => c.Name != allThemesChipName)?.Name ?? "Panda e os Caricas")
                : themeName;

            var results = await YoutubeSearchService.SearchByThemeAsync(searchTerm, currentLangs, maxResults: 30);
            var list = new List<ApprovedVideo>();
            foreach (var r in results)
            {
                if (_blockedVideoIds.Contains(r.Id)) continue;
                list.Add(new ApprovedVideo
                {
                    Id = r.Id,
                    Title = r.Title,
                    IsDiscovered = true
                });
            }

            _themeVideosCache[themeName] = list;
            SavePersistedThemeCache();

            // Só atualiza a lista se a seleção do utilizador ainda for este tema
            if (_activeSelectedTheme.Equals(themeName, StringComparison.OrdinalIgnoreCase))
            {
                _childDisplayVideos.Clear();
                foreach (var v in list)
                {
                    _childDisplayVideos.Add(v);
                }
                UpdateEmptyState();
                if (Services.DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
                {
                    UpdateTvUI();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadThemeVideosForChild] {ex.Message}");
        }
        finally
        {
            if (ChildThemeLoadingIndicator is not null)
            {
                ChildThemeLoadingIndicator.IsRunning = false;
                ChildThemeLoadingIndicator.IsVisible = false;
            }
        }
    }

    // ==========================================
    // GESTÃO DE VÍDEOS OCULTADOS / BLOQUEADOS
    // ==========================================
    private void LoadBlockedVideos()
    {
        try
        {
            _blockedVideos.Clear();
            _blockedVideoIds.Clear();

            var json = Preferences.Default.Get(BlockedVideosKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var list = JsonSerializer.Deserialize<List<BlockedVideoItem>>(json);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        _blockedVideos.Add(item);
                        _blockedVideoIds.Add(item.Id);
                    }
                }
            }
            UpdateBlockedVideosUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadBlockedVideos] {ex.Message}");
        }
    }

    private void SaveBlockedVideos()
    {
        try
        {
            var json = JsonSerializer.Serialize(_blockedVideos.ToList());
            Preferences.Default.Set(BlockedVideosKey, json);
            UpdateBlockedVideosUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveBlockedVideos] {ex.Message}");
        }
    }

    private void UpdateBlockedVideosUI()
    {
        if (BlockedVideosHeaderLabel is not null)
            BlockedVideosHeaderLabel.Text = AppStrings.Get("BlockedVideosTitle", _blockedVideos.Count);
        if (BlockedCountBadge is not null)
            BlockedCountBadge.Text = _blockedVideos.Count.ToString();
        if (NoBlockedVideosLabel is not null)
            NoBlockedVideosLabel.IsVisible = _blockedVideos.Count == 0;
    }

    private async void BlockVideo(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        if (!_blockedVideoIds.Contains(id))
        {
            _blockedVideoIds.Add(id);
            _blockedVideos.Insert(0, new BlockedVideoItem
            {
                Id = id,
                Title = string.IsNullOrWhiteSpace(title) ? id : title,
                BlockedAt = DateTime.UtcNow
            });
            SaveBlockedVideos();
        }

        // Remove de todas as listas ativas
        var matchApproved = _videos.FirstOrDefault(v => v.Id == id);
        if (matchApproved != null) _videos.Remove(matchApproved);

        var matchChild = _childDisplayVideos.FirstOrDefault(v => v.Id == id);
        if (matchChild != null) _childDisplayVideos.Remove(matchChild);

        var matchParent = _parentDisplayVideos.FirstOrDefault(v => v.Id == id);
        if (matchParent != null) _parentDisplayVideos.Remove(matchParent);

        var matchTvCont = _tvContinueVideos.FirstOrDefault(v => v.Id == id);
        if (matchTvCont != null) _tvContinueVideos.Remove(matchTvCont);

        var matchTvFav = _tvFavoriteVideos.FirstOrDefault(v => v.Id == id);
        if (matchTvFav != null) _tvFavoriteVideos.Remove(matchTvFav);

        // Remove de todos os temas em cache
        foreach (var key in _themeVideosCache.Keys.ToList())
        {
            _themeVideosCache[key].RemoveAll(v => v.Id == id);
        }
        SavePersistedThemeCache();
        SaveVideos();
        UpdateEmptyState();

        await DisplayAlert(AppStrings.Get("AppTitle"), AppStrings.Get("VideoBlockedSuccess"), AppStrings.Get("BtnOk"));
    }

    private async void OnUnblockVideoClicked(object? sender, EventArgs e)
    {
        BlockedVideoItem? item = null;
        if (sender is Button btn && btn.CommandParameter is BlockedVideoItem bvi) item = bvi;
        else if (sender is BindableObject bo && bo.BindingContext is BlockedVideoItem boBvi) item = boBvi;

        if (item != null)
        {
            _blockedVideoIds.Remove(item.Id);
            _blockedVideos.Remove(item);
            SaveBlockedVideos();

            // Limpa o cache dos temas para recarregar se necessário
            _themeVideosCache.Clear();
            SavePersistedThemeCache();

            if (_curationMode == "themes")
            {
                _ = LoadThemeVideosForChild(_activeSelectedTheme);
            }

            await DisplayAlert(AppStrings.Get("AppTitle"), AppStrings.Get("VideoUnblockedSuccess"), AppStrings.Get("BtnOk"));
        }
    }

    // ==========================================
    // PRÉ-VISUALIZAÇÃO DE VÍDEO (ADULTOS)
    // ==========================================
    private void OnThumbnailPreviewTapped(object? sender, EventArgs e)
    {
        ParentVideoItem? item = null;
        if (e is TappedEventArgs te && te.Parameter is ParentVideoItem pi) item = pi;
        else if (sender is BindableObject bo && bo.BindingContext is ParentVideoItem boPi) item = boPi;

        if (item != null)
        {
            OpenAdultPreview(item);
        }
    }

    private void OnDirectPreviewClicked(object? sender, EventArgs e)
    {
        ParentVideoItem? item = null;
        if (sender is Button btn && btn.CommandParameter is ParentVideoItem pi) item = pi;
        else if (sender is BindableObject bo && bo.BindingContext is ParentVideoItem boPi) item = boPi;

        if (item != null)
        {
            OpenAdultPreview(item);
        }
    }

    private void OpenAdultPreview(ParentVideoItem item)
    {
        _currentPreviewingVideo = item;
        if (PreviewVideoTitleLabel is not null) PreviewVideoTitleLabel.Text = item.Title;

        UpdatePreviewModalButtons(item);

        if (AdultPreviewWebView is not null)
        {
            AdultPreviewWebView.Source = new HtmlWebViewSource
            {
                Html = YoutubePlayerHtml.Build(new List<string> { item.Id }, item.Id),
                BaseUrl = YoutubePlayerHtml.AppOrigin
            };
        }

        if (AdultPreviewModalOverlay is not null)
        {
            AdultPreviewModalOverlay.IsVisible = true;
            _tvPreviewCol = 0;
            UpdateTvPreviewVisualFocus();
        }
    }

    private void UpdatePreviewModalButtons(ParentVideoItem item)
    {
        var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
        if (BtnPreviewSendToTv is not null)
        {
            BtnPreviewSendToTv.IsVisible = !isTv;
        }
        if (BtnPreviewToggleApproval is not null)
        {
            BtnPreviewToggleApproval.Text = item.IsApproved ? AppStrings.Get("BtnRemoveFromKids") : AppStrings.Get("BtnApproveForKids");
            BtnPreviewToggleApproval.BackgroundColor = item.IsApproved ? Color.FromArgb("#DC2626") : (Color)Application.Current!.Resources["BrandGreen"];
        }
    }

    private void OnCloseAdultPreviewClicked(object? sender, EventArgs e)
    {
        if (AdultPreviewModalOverlay is not null)
        {
            AdultPreviewModalOverlay.IsVisible = false;
        }
        if (AdultPreviewWebView is not null)
        {
            AdultPreviewWebView.Source = new HtmlWebViewSource
            {
                Html = "<html><body style='background:#000;'></body></html>",
                BaseUrl = YoutubePlayerHtml.AppOrigin
            };
        }
        _currentPreviewingVideo = null;
        if (ParentOverlay is not null && ParentOverlay.IsVisible)
        {
            UpdateTvParentVisualFocus();
        }
        else
        {
            UpdateTvUI();
            UpdateTvVisualFocus();
        }
    }

    private void OnPreviewToggleApprovalClicked(object? sender, EventArgs e)
    {
        if (_currentPreviewingVideo == null) return;
        OnToggleApprovalClicked(_currentPreviewingVideo, EventArgs.Empty);
        UpdatePreviewModalButtons(_currentPreviewingVideo);
    }

    private void OnPreviewBlockVideoClicked(object? sender, EventArgs e)
    {
        if (_currentPreviewingVideo == null) return;
        var videoToBlock = _currentPreviewingVideo;
        OnCloseAdultPreviewClicked(sender, e);
        BlockVideo(videoToBlock.Id, videoToBlock.Title);
    }

    private void OnPreviewSendToTvClicked(object? sender, EventArgs e)
    {
        if (_currentPreviewingVideo == null) return;
        OnQuickSendSingleVideoToTvClicked(_currentPreviewingVideo, e);
    }

    private void OnDirectBlockVideoClicked(object? sender, EventArgs e)
    {
        ParentVideoItem? item = null;
        if (sender is Button btn && btn.CommandParameter is ParentVideoItem pi) item = pi;
        else if (sender is BindableObject bo && bo.BindingContext is ParentVideoItem boPi) item = boPi;

        if (item != null)
        {
            BlockVideo(item.Id, item.Title);
        }
    }

    private void PreloadThemesInBackground()
    {
        Task.Run(async () =>
        {
            try
            {
                var currentLang = Preferences.Default.Get(LanguageKey, "pt-PT");
                var allThemesChipName = AppStrings.Get("ThemeChipAll");
                var themesToFetch = _childThemeChips
                    .Where(c => !c.Name.Equals(allThemesChipName, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Name)
                    .ToList();

                // Dispara o download em paralelo de todos os temas para 0ms de espera
                var tasks = themesToFetch.Select(async theme =>
                {
                    if (!_themeVideosCache.ContainsKey(theme) || _themeVideosCache[theme].Count == 0)
                    {
                        var results = await YoutubeSearchService.SearchByThemeAsync(theme, currentLang, maxResults: 25);
                        var list = new List<ApprovedVideo>();
                        foreach (var r in results)
                        {
                            list.Add(new ApprovedVideo
                            {
                                Id = r.Id,
                                Title = r.Title,
                                IsDiscovered = true
                            });
                        }
                        _themeVideosCache[theme] = list;
                    }
                }).ToList();

                await Task.WhenAll(tasks);
                SavePersistedThemeCache();

                // Se estiver no modo de temas e a lista de vídeos ainda estiver vazia, atualiza agora
                if (_curationMode == "themes" && _childDisplayVideos.Count == 0)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _ = LoadThemeVideosForChild(_activeSelectedTheme);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreloadThemes] {ex.Message}");
            }
        });
    }

    #endregion

    #region Adult Tabs Navigation

    private string _currentAdultTab = "stories";

    private void OnAdultTabSelected(object? sender, TappedEventArgs e)
    {
        var tab = e.Parameter as string ?? "stories";
        SelectAdultTab(tab);
    }

    private void SelectAdultTab(string tab)
    {
        _currentAdultTab = tab;

        var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
        if (BtnSendApprovedListToTv is not null) BtnSendApprovedListToTv.IsVisible = !isTv;

        if (AdultTabStoriesView is not null) AdultTabStoriesView.IsVisible = tab == "stories";
        if (AdultTabTimeView is not null) AdultTabTimeView.IsVisible = tab == "time";
        if (AdultTabTvsView is not null) AdultTabTvsView.IsVisible = tab == "tvs";
        if (AdultTabSettingsView is not null) AdultTabSettingsView.IsVisible = tab == "settings";

        if (tab == "tvs")
        {
            if (TvServerPairingCard is not null) TvServerPairingCard.IsVisible = isTv;
            if (MobileTvsManagementCard is not null) MobileTvsManagementCard.IsVisible = !isTv;

            if (isTv)
            {
                StartTvPairingCodeDisplay();
                RefreshTvPairedClients();
            }
            else
            {
                LoadMobilePairedTvs();
            }
        }
        else
        {
            StopTvPairingCodeDisplay();
        }

        UpdateAdultTabStyles();

        if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
        {
            UpdateTvParentVisualFocus();
        }
    }

    private void UpdateAdultTabStyles()
    {
        try
        {
            var activeBg = (Color)Application.Current!.Resources["BrandGreen"];
            var inactiveBg = Application.Current.RequestedTheme == AppTheme.Dark 
                ? (Color)Application.Current.Resources["SurfaceElevatedDark"] 
                : (Color)Application.Current.Resources["SurfaceElevatedLight"];

            var activeBorder = (Color)Application.Current!.Resources["BrandGreen"];
            var inactiveBorder = Application.Current.RequestedTheme == AppTheme.Dark 
                ? (Color)Application.Current.Resources["BorderDark"] 
                : (Color)Application.Current.Resources["BorderLight"];

            var activeText = (Color)Application.Current!.Resources["White"];
            var inactiveText = Application.Current.RequestedTheme == AppTheme.Dark 
                ? (Color)Application.Current.Resources["TextPrimaryDark"] 
                : (Color)Application.Current.Resources["TextPrimaryLight"];

            if (TabBtnStories is not null && TabLblStories is not null)
            {
                var active = _currentAdultTab == "stories";
                TabBtnStories.BackgroundColor = active ? activeBg : inactiveBg;
                TabBtnStories.Stroke = active ? activeBorder : inactiveBorder;
                TabLblStories.TextColor = active ? activeText : inactiveText;
            }

            if (TabBtnTime is not null && TabLblTime is not null)
            {
                var active = _currentAdultTab == "time";
                TabBtnTime.BackgroundColor = active ? activeBg : inactiveBg;
                TabBtnTime.Stroke = active ? activeBorder : inactiveBorder;
                TabLblTime.TextColor = active ? activeText : inactiveText;
            }

            if (TabBtnTvs is not null && TabLblTvs is not null)
            {
                var active = _currentAdultTab == "tvs";
                TabBtnTvs.BackgroundColor = active ? activeBg : inactiveBg;
                TabBtnTvs.Stroke = active ? activeBorder : inactiveBorder;
                TabLblTvs.TextColor = active ? activeText : inactiveText;
            }

            if (TabBtnSettings is not null && TabLblSettings is not null)
            {
                var active = _currentAdultTab == "settings";
                TabBtnSettings.BackgroundColor = active ? activeBg : inactiveBg;
                TabBtnSettings.Stroke = active ? activeBorder : inactiveBorder;
                TabLblSettings.TextColor = active ? activeText : inactiveText;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateAdultTabStyles] {ex.Message}");
        }
    }

    #endregion

    #region In-Player Suggestions Drawer

    private void OnTogglePlayerSuggestionsClicked(object? sender, EventArgs e)
    {
        if (PlayerSuggestionsDrawer is not null)
        {
            PlayerSuggestionsDrawer.IsVisible = !PlayerSuggestionsDrawer.IsVisible;
            if (PlayerSuggestionsDrawer.IsVisible)
            {
                // Cancela o auto-hide para dar tempo à criança de escolher a história
                _hideControlsCts?.Cancel();
            }
            else
            {
                ScheduleAutoHideControls();
            }
        }
    }

    private void OnClosePlayerSuggestionsClicked(object? sender, EventArgs e)
    {
        if (PlayerSuggestionsDrawer is not null)
        {
            PlayerSuggestionsDrawer.IsVisible = false;
            ScheduleAutoHideControls();
        }
    }

    private async void OnPlayerSuggestionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ApprovedVideo selectedVideo)
        {
            if (sender is CollectionView cv)
            {
                cv.SelectedItem = null;
            }
            await SwitchToPlayerVideoAsync(selectedVideo);
        }
    }

    private async void OnPlayerSuggestionVideoTapped(object? sender, TappedEventArgs e)
    {
        ApprovedVideo? targetVideo = null;
        if (e.Parameter is ApprovedVideo av) targetVideo = av;
        else if (sender is BindableObject bo && bo.BindingContext is ApprovedVideo ctxAv) targetVideo = ctxAv;

        if (targetVideo != null)
        {
            await SwitchToPlayerVideoAsync(targetVideo);
        }
    }

    private async Task SwitchToPlayerVideoAsync(ApprovedVideo targetVideo)
    {
        try
        {
            if (PlayerSuggestionsDrawer is not null) PlayerSuggestionsDrawer.IsVisible = false;
            _isPlaying = true;
            if (PlayPauseBtn is not null) PlayPauseBtn.Text = "⏸";

            await YoutubePlayer.EvaluateJavaScriptAsync($"if(window.playVideoId){{window.playVideoId('{targetVideo.Id}');}}else if(typeof player!=='undefined'&&player.loadVideoById){{player.loadVideoById('{targetVideo.Id}');player.playVideo();}}");
            
            if (SubtitleLabel is not null) SubtitleLabel.Text = targetVideo.Title;

            // Atualiza sugestões para o novo vídeo selecionado
            _playerSuggestionVideos.Clear();
            foreach (var v in _childDisplayVideos.Where(v => v.Id != targetVideo.Id))
            {
                _playerSuggestionVideos.Add(v);
            }
            ScheduleAutoHideControls();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaySuggestion] {ex.Message}");
        }
    }

    #endregion

    private void SaveVideos()
    {
        Preferences.Default.Set(VideosKey, JsonSerializer.Serialize(_videos));
        UpdateTvUI();
    }

    private void UpdateEmptyState()
    {
        EmptyLabel.IsVisible = _childDisplayVideos.Count == 0;
    }

    #region Termos de Utilização & Contacto / Suporte

    private bool _termsScrolledToBottom = false;

    private void ShowTermsOverlay(bool isMandatory)
    {
        if (TermsOverlay is null) return;
        if (TermsCloseBtn is not null)
        {
            TermsCloseBtn.IsVisible = !isMandatory;
        }

        var alreadyAccepted = Preferences.Default.Get(TermsAcceptedKey, false);
        _termsScrolledToBottom = alreadyAccepted;

        if (BtnAcceptTermsOverlay is not null)
        {
            BtnAcceptTermsOverlay.IsEnabled = alreadyAccepted;
            BtnAcceptTermsOverlay.Opacity = alreadyAccepted ? 1.0 : 0.45;
        }

        if (TermsScrollHintLabel is not null)
        {
            TermsScrollHintLabel.Text = alreadyAccepted 
                ? AppStrings.Get("TermsScrolledCompleteHint") 
                : AppStrings.Get("TermsScrollToBottomHint");
            TermsScrollHintLabel.TextColor = alreadyAccepted ? Color.FromArgb("#10B981") : Color.FromArgb("#EAB308");
        }

        if (TermsContentScrollView is not null)
        {
            _ = TermsContentScrollView.ScrollToAsync(0, 0, false);
        }

        _tvTermsCol = 0;
        TermsOverlay.IsVisible = true;
        UpdateTvTermsVisualFocus();
    }

    private void OnTermsContentScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_termsScrolledToBottom) return;

        if (sender is ScrollView sv && sv.Content is VisualElement content)
        {
            var contentHeight = content.Height;
            var viewHeight = sv.Height;
            if (contentHeight > 0 && viewHeight > 0)
            {
                // Quando o utilizador desce pelo menos até aos últimos 50px do texto
                if (e.ScrollY + viewHeight >= contentHeight - 50)
                {
                    _termsScrolledToBottom = true;
                    if (BtnAcceptTermsOverlay is not null)
                    {
                        BtnAcceptTermsOverlay.IsEnabled = true;
                        BtnAcceptTermsOverlay.Opacity = 1.0;
                    }
                    if (TermsScrollHintLabel is not null)
                    {
                        TermsScrollHintLabel.Text = AppStrings.Get("TermsScrolledCompleteHint");
                        TermsScrollHintLabel.TextColor = Color.FromArgb("#10B981");
                    }
                }
            }
        }
    }

    private void UpdateTvTermsVisualFocus()
    {
        if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
        {
            SetVisualTvFocus(BtnAcceptTermsOverlay, _tvTermsCol == 0);
            SetVisualTvFocus(BtnTermsContactSupport, _tvTermsCol == 1);
        }
    }

    private void UpdateTvOnboardingVisualFocus()
    {
        if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
        {
            SetVisualTvFocus(BtnOnboardingPrev, _tvOnboardingBtn == 0);
            SetVisualTvFocus(BtnOnboardingNext, _tvOnboardingBtn == 1);
            SetVisualTvFocus(BtnSkipOnboarding, _tvOnboardingBtn == 2);
        }
    }

    private void UpdateTvPreviewVisualFocus()
    {
        if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
        {
            SetVisualTvFocus(BtnPreviewToggleApproval, _tvPreviewCol == 0);
            SetVisualTvFocus(BtnPreviewBlockVideo, _tvPreviewCol == 1);
            SetVisualTvFocus(BtnPreviewSendToTv, _tvPreviewCol == 2 && (BtnPreviewSendToTv?.IsVisible ?? false));
            SetVisualTvFocus(BtnCloseAdultPreviewModal, _tvPreviewCol == 3);
        }
    }

    private void OnViewTermsClicked(object? sender, EventArgs e)
    {
        ShowTermsOverlay(isMandatory: false);
    }

    private void OnCloseTermsOverlayClicked(object? sender, EventArgs e)
    {
        if (TermsOverlay is not null)
        {
            TermsOverlay.IsVisible = false;
        }
    }

    private void OnAcceptTermsClicked(object? sender, EventArgs e)
    {
        if (!_termsScrolledToBottom && !Preferences.Default.Get(TermsAcceptedKey, false))
        {
            return; // Bloqueia aceitação se ainda não fez scroll
        }

        Preferences.Default.Set(TermsAcceptedKey, true);
        if (TermsOverlay is not null)
        {
            TermsOverlay.IsVisible = false;
        }

        if (!Preferences.Default.Get(OnboardingCompletedKey, false))
        {
            ShowOnboardingOverlay();
        }
        else
        {
            _ = PromptScreenLockIfNeededAsync();
        }
    }

    private async void OnContactSupportClicked(object? sender, EventArgs e)
    {
        await OpenFeedbackEmailAsync();
    }

    private async Task OpenFeedbackEmailAsync()
    {
        const string devEmail = "joaopaulotsantos@gmail.com";
        var subject = $"[ChicaTube] {AppStrings.Get("DialogContactTitle")}";
        var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
        var body = $"ChicaTube - {(isTv ? "Android TV" : "Mobile")}\nVersão: 1.0\n\n[Escreva aqui a sua mensagem ou sugestão / Write your message or suggestion here]:\n";

        try
        {
            if (Email.Default.IsComposeSupported)
            {
                var message = new EmailMessage
                {
                    Subject = subject,
                    Body = body,
                    To = new List<string> { devEmail }
                };
                await Email.Default.ComposeAsync(message);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EmailComposeError] {ex.Message}");
        }

        try
        {
            var mailtoUri = $"mailto:{devEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
            await Launcher.Default.OpenAsync(new Uri(mailtoUri));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MailtoLauncherError] {ex.Message}");
            await Clipboard.Default.SetTextAsync(devEmail);
            await DisplayAlert(
                AppStrings.Get("DialogContactTitle"),
                $"{AppStrings.Get("ContactSupportDesc")}\n\nEmail: {devEmail}",
                AppStrings.Get("BtnUnderstood")
            );
        }
    }

    #region TV Remote Server & Mobile Client Controller

    private void InitTvRemoteSystem()
    {
        try
        {
            var isTv = DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV;
            if (isTv)
            {
                TvServerService.Instance.PlayVideoRequested += OnTvServerPlayVideoRequested;
                TvServerService.Instance.CommandReceived += OnTvServerCommandReceived;
                TvServerService.Instance.QueueUpdated += OnTvServerQueueUpdated;
                TvServerService.Instance.PairingCompleted += OnTvServerPairingCompleted;
                TvServerService.Instance.GetCurrentPlaybackState = GetTvPlaybackState;

                TvQueueManager.Instance.QueueChanged += OnTvQueueChanged;
                TvServerService.Instance.SyncDataReceived += OnTvServerSyncDataReceived;
                TvServerService.Instance.Start();
            }
            else
            {
                LoadMobilePairedTvs();
                StartMobileTvAuthWatcher();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InitTvRemoteSystem] {ex.Message}");
        }
    }

    private void StartMobileTvAuthWatcher()
    {
        if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV) return;

        Dispatcher.StartTimer(TimeSpan.FromSeconds(2.5), () =>
        {
            if (_isPromptingRemoteAuth || _isCheckingTvAuth) return true;

            _ = Task.Run(async () =>
            {
                _isCheckingTvAuth = true;
                try
                {
                    var tvs = TvPairingManager.Instance.GetPairedTvs().ToList();
                    foreach (var tv in tvs)
                    {
                        if (_lastTvAuthPromptTimes.TryGetValue(tv.IpAddress, out var lastTime) && (DateTime.UtcNow - lastTime).TotalSeconds < 60)
                        {
                            continue;
                        }

                        var isPending = await TvClientService.Instance.CheckTvAuthPendingAsync(tv);
                        if (isPending)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                PromptBiometricOrPinForRemoteTv(tv);
                            });
                            break;
                        }
                    }
                }
                catch { }
                finally
                {
                    _isCheckingTvAuth = false;
                }
            });

            return true;
        });
    }

    private async void PromptBiometricOrPinForRemoteTv(TvDevice tv)
    {
        if (_isPromptingRemoteAuth) return;
        if (_lastTvAuthPromptTimes.TryGetValue(tv.IpAddress, out var lastTime) && (DateTime.UtcNow - lastTime).TotalSeconds < 60) return;
        _isPromptingRemoteAuth = true;

        try
        {
            var isBiometricEnabled = Preferences.Default.Get(BiometricEnabledKey, true);
            bool authorized = false;

            if (isBiometricEnabled && await _biometricService.IsBiometricAvailableAsync())
            {
                var result = await _biometricService.AuthenticateAsync(
                    AppStrings.Get("TvAuthTitle"),
                    AppStrings.Get("TvAuthSubtitleBiometric", tv.Name),
                    AppStrings.Get("BiometricNegativeButton")
                );
                if (result == Services.BiometricAuthResult.Success)
                {
                    authorized = true;
                }
            }

            if (!authorized)
            {
                var enteredPin = await DisplayPromptAsync(
                    AppStrings.Get("TvAuthTitle"),
                    AppStrings.Get("TvAuthPinPrompt", tv.Name),
                    maxLength: 4,
                    keyboard: Keyboard.Numeric
                );
                var currentPin = Preferences.Default.Get(PinKey, "1234");
                if (!string.IsNullOrEmpty(enteredPin) && enteredPin == currentPin)
                {
                    authorized = true;
                }
            }

            if (authorized)
            {
                _lastTvAuthPromptTimes[tv.IpAddress] = DateTime.UtcNow;
                var (success, msg) = await TvClientService.Instance.UnlockParentAreaOnTvAsync(tv);
                if (success)
                {
                    await DisplayAlert(
                        AppStrings.Get("DialogTvUnlockedTitle"),
                        AppStrings.Get("DialogTvUnlockedMsg", tv.Name),
                        AppStrings.Get("BtnOk")
                    );
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteAuthPrompt] {ex.Message}");
        }
        finally
        {
            _isPromptingRemoteAuth = false;
        }
    }

    private TvPlaybackState GetTvPlaybackState()
    {
        var currentVideo = _videos.FirstOrDefault(v => v.Title == SubtitleLabel?.Text);
        var remainingSeconds = _sessionStarted ? Math.Max(0, (int)(_sessionEndsAt - DateTime.Now).TotalSeconds) : 0;

        return new TvPlaybackState
        {
            Status = (_isPlaying && PlayerView is not null && PlayerView.IsVisible) ? "playing" : "idle",
            CurrentVideo = currentVideo != null ? TvVideoItem.FromApprovedVideo(currentVideo) : null,
            CurrentPositionSeconds = 0,
            DurationSeconds = _mealDurationMinutes * 60,
            RemainingSessionSeconds = remainingSeconds,
            IsSessionExpired = _isSessionExpired,
            Queue = TvQueueManager.Instance.Queue.ToList(),
            QueueIndex = TvQueueManager.Instance.CurrentIndex,
            TvName = TvPairingManager.Instance.TvName
        };
    }

    private void OnTvServerPlayVideoRequested(TvVideoItem video)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var approved = video.ToApprovedVideo();
                StartSession(approved);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TvServer.PlayVideoRequested] {ex.Message}");
            }
        });
    }

    private void OnTvServerCommandReceived(TvRemoteCommand command)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var action = command.Action?.ToLowerInvariant() ?? string.Empty;
                switch (action)
                {
                    case "play":
                    case "resume":
                        if (PlayerView is not null && PlayerView.IsVisible)
                        {
                            try { await YoutubePlayer.EvaluateJavaScriptAsync("if(window.playVideo){window.playVideo();}else if(typeof player!=='undefined'&&player.playVideo){player.playVideo();}"); } catch { }
                            _isPlaying = true;
                            if (PlayPauseBtn is not null) PlayPauseBtn.Text = "⏸";
                        }
                        break;

                    case "pause":
                        if (PlayerView is not null && PlayerView.IsVisible)
                        {
                            try { await YoutubePlayer.EvaluateJavaScriptAsync("if(window.pauseVideo){window.pauseVideo();}else if(typeof player!=='undefined'&&player.pauseVideo){player.pauseVideo();}"); } catch { }
                            _isPlaying = false;
                            if (PlayPauseBtn is not null) PlayPauseBtn.Text = "▶";
                        }
                        break;

                    case "stop":
                        if (PlayerView is not null && PlayerView.IsVisible)
                        {
                            await FinishSessionAsync();
                        }
                        break;

                    case "next":
                        if (TvQueueManager.Instance.HasNext)
                        {
                            var nextItem = TvQueueManager.Instance.Next();
                            if (nextItem != null)
                            {
                                StartSession(nextItem.ToApprovedVideo());
                            }
                        }
                        else if (_playerSuggestionVideos.Count > 0)
                        {
                            var nextSuggestion = _playerSuggestionVideos.FirstOrDefault();
                            if (nextSuggestion != null)
                            {
                                await SwitchToPlayerVideoAsync(nextSuggestion);
                            }
                        }
                        break;

                    case "prev":
                    case "previous":
                        if (TvQueueManager.Instance.HasPrevious)
                        {
                            var prevItem = TvQueueManager.Instance.Previous();
                            if (prevItem != null)
                            {
                                StartSession(prevItem.ToApprovedVideo());
                            }
                        }
                        break;

                    case "add_time":
                    case "addminutes":
                        if (command.MinutesToAdd > 0)
                        {
                            ApplyMinutesExtension(command.MinutesToAdd);
                        }
                        break;

                    case "seek":
                        if (command.PositionSeconds >= 0 && PlayerView is not null && PlayerView.IsVisible)
                        {
                            try { await YoutubePlayer.EvaluateJavaScriptAsync($"if(typeof player!=='undefined'&&player.seekTo){{player.seekTo({command.PositionSeconds}, true);}}"); } catch { }
                        }
                        break;

                    case "unlock_parent_area":
                    case "unlock_tv":
                        if (PinPromptOverlay is not null) PinPromptOverlay.IsVisible = false;
                        if (TvRemoteAuthPromptOverlay is not null) TvRemoteAuthPromptOverlay.IsVisible = false;
                        OpenParentAreaDirectly();
                        break;

                    case "lock_parent_area":
                    case "lock_tv":
                        if (ParentOverlay.IsVisible) ParentOverlay.IsVisible = false;
                        if (TvRemoteAuthPromptOverlay is not null) TvRemoteAuthPromptOverlay.IsVisible = false;
                        if (PinPromptOverlay is not null) PinPromptOverlay.IsVisible = false;
                        UpdateTvUI();
                        UpdateTvVisualFocus();
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TvServer.CommandReceived] {ex.Message}");
            }
        });
    }

    private void OnTvServerQueueUpdated()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Fila atualizada no servidor da TV
        });
    }

    private void OnTvServerPairingCompleted(PairedClientInfo client)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshTvPairedClients();
        });
    }

    private void OnTvQueueChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Sincronização de fila
        });
    }

    // ==========================================
    // TV PAIRING CODE DISPLAY & UI
    // ==========================================

    private void StartTvPairingCodeDisplay()
    {
        RefreshTvPairingCodeDisplay();

        _tvPairingCountdownTimer?.Stop();
        _tvPairingCountdownTimer = Dispatcher.CreateTimer();
        _tvPairingCountdownTimer.Interval = TimeSpan.FromSeconds(1);
        _tvPairingCountdownTimer.Tick += (s, e) =>
        {
            var remaining = TvPairingManager.Instance.RemainingCodeSeconds;
            if (remaining <= 0)
            {
                TvPairingManager.Instance.GenerateNewPairingCode();
                remaining = 300;
            }

            if (TvPairingCountdownLabel is not null)
            {
                TvPairingCountdownLabel.Text = AppStrings.Get("TvCodeExpiresIn", remaining);
            }
        };
        _tvPairingCountdownTimer.Start();
    }

    private void StopTvPairingCodeDisplay()
    {
        _tvPairingCountdownTimer?.Stop();
    }

    private void RefreshTvPairingCodeDisplay()
    {
        var code = TvPairingManager.Instance.FormattedCurrentCode;
        if (string.IsNullOrWhiteSpace(code) || TvPairingManager.Instance.RemainingCodeSeconds <= 0)
        {
            var gen = TvPairingManager.Instance.GenerateNewPairingCode();
            code = gen.code;
        }

        if (TvPairingCodeLabel is not null)
        {
            TvPairingCodeLabel.Text = code;
        }

        if (TvPairingCountdownLabel is not null)
        {
            TvPairingCountdownLabel.Text = AppStrings.Get("TvCodeExpiresIn", TvPairingManager.Instance.RemainingCodeSeconds);
        }
    }

    private void RefreshTvPairedClients()
    {
        _tvPairedClients.Clear();
        var clients = TvPairingManager.Instance.GetPairedClients();
        foreach (var c in clients)
        {
            _tvPairedClients.Add(c);
        }
        if (TvPairedClientsList is not null)
        {
            TvPairedClientsList.IsVisible = _tvPairedClients.Count > 0;
        }
        if (TvDisconnectAllBtn is not null)
        {
            TvDisconnectAllBtn.IsVisible = _tvPairedClients.Count > 0;
        }
    }

    private void OnRegenerateTvPairingCodeClicked(object? sender, EventArgs e)
    {
        TvPairingManager.Instance.GenerateNewPairingCode();
        RefreshTvPairingCodeDisplay();
    }

    private async void OnDisconnectAllTvClientsClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            AppStrings.Get("DialogDisconnectTvsTitle"),
            AppStrings.Get("DialogDisconnectTvsMsg"),
            AppStrings.Get("BtnDisconnectAllPhones"),
            AppStrings.Get("BtnCancel")
        );

        if (confirm)
        {
            TvPairingManager.Instance.ClearAllPairedClients();
            RefreshTvPairedClients();
        }
    }

    // ==========================================
    // GESTÃO DE TVs NO TELEMÓVEL (PAIS)
    // ==========================================

    private void LoadMobilePairedTvs()
    {
        try
        {
            _mobilePairedTvs.Clear();
            var tvs = TvPairingManager.Instance.GetPairedTvs();
            foreach (var tv in tvs)
            {
                _mobilePairedTvs.Add(tv);
            }

            if (EmptyMobileTvsHint is not null)
            {
                EmptyMobileTvsHint.IsVisible = _mobilePairedTvs.Count == 0;
            }

            if (TargetTvPicker is not null)
            {
                TargetTvPicker.ItemsSource = _mobilePairedTvs.Select(t => t.Name).ToList();
                if (_mobilePairedTvs.Count > 0)
                {
                    TargetTvPicker.SelectedIndex = 0;
                }
            }

            // Pings em background para atualizar o estado Online/Offline
            Task.Run(async () =>
            {
                foreach (var tv in _mobilePairedTvs.ToList())
                {
                    var isOnline = await TvClientService.Instance.CheckTvOnlineAsync(tv);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var index = _mobilePairedTvs.IndexOf(tv);
                        if (index >= 0)
                        {
                            _mobilePairedTvs[index] = tv; // Dispara atualização visual
                        }
                    });
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadMobilePairedTvs] {ex.Message}");
        }
    }

    private void OnOpenAddTvModalClicked(object? sender, EventArgs e)
    {
        if (TvIpEntry is not null) TvIpEntry.Text = string.Empty;
        if (TvCustomNameEntry is not null) TvCustomNameEntry.Text = string.Empty;
        if (TvPairingCodeEntry is not null) TvPairingCodeEntry.Text = string.Empty;
        if (TvPairingErrorLabel is not null) TvPairingErrorLabel.IsVisible = false;

        if (AddTvModalOverlay is not null)
        {
            AddTvModalOverlay.IsVisible = true;
        }

        OnRefreshDiscoveredTvsClicked(null, EventArgs.Empty);
    }

    private void OnCloseAddTvModalClicked(object? sender, EventArgs e)
    {
        if (AddTvModalOverlay is not null)
        {
            AddTvModalOverlay.IsVisible = false;
        }
    }

    private async void OnRefreshDiscoveredTvsClicked(object? sender, EventArgs e)
    {
        if (ModalScanIndicator is not null)
        {
            ModalScanIndicator.IsVisible = true;
            ModalScanIndicator.IsRunning = true;
        }

        try
        {
            _discoveredTvs.Clear();
            var found = await TvClientService.Instance.DiscoverTvsAsync(2500);
            foreach (var tv in found)
            {
                _discoveredTvs.Add(tv);
            }
        }
        catch { }
        finally
        {
            if (ModalScanIndicator is not null)
            {
                ModalScanIndicator.IsRunning = false;
                ModalScanIndicator.IsVisible = false;
            }
        }
    }

    private async void OnScanTvsNetworkClicked(object? sender, EventArgs e)
    {
        if (ScanTvsIndicator is not null)
        {
            ScanTvsIndicator.IsVisible = true;
            ScanTvsIndicator.IsRunning = true;
        }

        try
        {
            var found = await TvClientService.Instance.DiscoverTvsAsync(3000);
            if (found.Count == 0)
            {
                await DisplayAlert(AppStrings.Get("DialogNetworkScanTitle"), AppStrings.Get("DialogNetworkScanNoTvsMsg"), AppStrings.Get("BtnOk"));
            }
            else
            {
                OnOpenAddTvModalClicked(null, EventArgs.Empty);
            }
        }
        catch { }
        finally
        {
            if (ScanTvsIndicator is not null)
            {
                ScanTvsIndicator.IsRunning = false;
                ScanTvsIndicator.IsVisible = false;
            }
        }
    }

    private void OnDiscoveredTvCardTapped(object? sender, TappedEventArgs e)
    {
        TvDiscoveryPacket? selected = null;
        if (e.Parameter is TvDiscoveryPacket p) selected = p;
        else if (sender is BindableObject bo && bo.BindingContext is TvDiscoveryPacket boP) selected = boP;

        if (selected != null)
        {
            SelectTvDiscoveryPacket(selected);
        }
    }

    private void OnDiscoveredTvSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is TvDiscoveryPacket selected)
        {
            SelectTvDiscoveryPacket(selected);
        }
    }

    private void SelectTvDiscoveryPacket(TvDiscoveryPacket selected)
    {
        if (TvIpEntry is not null) TvIpEntry.Text = selected.IpAddress;
        if (TvCustomNameEntry is not null && string.IsNullOrWhiteSpace(TvCustomNameEntry.Text))
        {
            TvCustomNameEntry.Text = selected.TvName;
        }
        if (TvPairingCodeEntry is not null)
        {
            TvPairingCodeEntry.Focus();
        }
    }

    private async void OnConfirmPairTvClicked(object? sender, EventArgs e)
    {
        var ip = TvIpEntry?.Text?.Trim();
        var code = TvPairingCodeEntry?.Text?.Trim();
        var customName = TvCustomNameEntry?.Text?.Trim();

        if (string.IsNullOrWhiteSpace(ip))
        {
            if (TvPairingErrorLabel is not null)
            {
                TvPairingErrorLabel.Text = "Por favor insira o endereço IP da TV.";
                TvPairingErrorLabel.IsVisible = true;
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(code) || code.Replace(" ", "").Length < 6)
        {
            if (TvPairingErrorLabel is not null)
            {
                TvPairingErrorLabel.Text = "Por favor insira o código de 6 dígitos apresentado na TV.";
                TvPairingErrorLabel.IsVisible = true;
            }
            return;
        }

        if (PairingIndicator is not null)
        {
            PairingIndicator.IsVisible = true;
            PairingIndicator.IsRunning = true;
        }
        if (BtnConfirmPairTv is not null) BtnConfirmPairTv.IsEnabled = false;

        try
        {
            var (success, message, tv) = await TvClientService.Instance.PairWithTvAsync(ip, TvServerService.ServerPort, code, customName);

            if (success && tv != null)
            {
                if (AddTvModalOverlay is not null) AddTvModalOverlay.IsVisible = false;
                LoadMobilePairedTvs();
                _ = SyncDataToTvInternalAsync(tv, showSuccessDialog: false);
                await DisplayAlert(AppStrings.Get("DialogPairSuccessTitle"), AppStrings.Get("DialogPairSuccessMsg", tv.Name), AppStrings.Get("BtnUnderstood"));
            }
            else
            {
                if (TvPairingErrorLabel is not null)
                {
                    TvPairingErrorLabel.Text = message;
                    TvPairingErrorLabel.IsVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            if (TvPairingErrorLabel is not null)
            {
                TvPairingErrorLabel.Text = $"Erro de ligação: {ex.Message}";
                TvPairingErrorLabel.IsVisible = true;
            }
        }
        finally
        {
            if (PairingIndicator is not null)
            {
                PairingIndicator.IsRunning = false;
                PairingIndicator.IsVisible = false;
            }
            if (BtnConfirmPairTv is not null) BtnConfirmPairTv.IsEnabled = true;
        }
    }

    private async void OnTestTvConnectionClicked(object? sender, EventArgs e)
    {
        TvDevice? tv = null;
        if (sender is Button btn && btn.CommandParameter is TvDevice d) tv = d;
        else if (sender is BindableObject bo && bo.BindingContext is TvDevice ctxD) tv = ctxD;

        if (tv == null) return;

        var isOnline = await TvClientService.Instance.CheckTvOnlineAsync(tv);
        LoadMobilePairedTvs();

        if (isOnline)
        {
            await DisplayAlert(AppStrings.Get("DialogTvConnectionTitle"), AppStrings.Get("DialogTvOnlineMsg", tv.Name, tv.IpAddress), AppStrings.Get("BtnOk"));
        }
        else
        {
            await DisplayAlert(AppStrings.Get("DialogTvConnectionTitle"), AppStrings.Get("DialogTvOfflineMsg", tv.Name, tv.IpAddress), AppStrings.Get("BtnOk"));
        }
    }

    private async void OnRenameTvClicked(object? sender, EventArgs e)
    {
        TvDevice? tv = null;
        if (sender is Button btn && btn.CommandParameter is TvDevice d) tv = d;
        else if (sender is BindableObject bo && bo.BindingContext is TvDevice ctxD) tv = ctxD;

        if (tv == null) return;

        var newName = await DisplayPromptAsync(AppStrings.Get("DialogRenameTvTitle"), AppStrings.Get("DialogRenameTvPrompt"), initialValue: tv.Name, maxLength: 30);
        if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, tv.Name, StringComparison.OrdinalIgnoreCase))
        {
            tv.Name = newName.Trim();
            TvPairingManager.Instance.AddOrUpdatePairedTv(tv);
            LoadMobilePairedTvs();
        }
    }

    private async void OnRemoveTvPairingClicked(object? sender, EventArgs e)
    {
        TvDevice? tv = null;
        if (sender is Button btn && btn.CommandParameter is TvDevice d) tv = d;
        else if (sender is BindableObject bo && bo.BindingContext is TvDevice ctxD) tv = ctxD;

        if (tv == null) return;

        var confirm = await DisplayAlert(
            AppStrings.Get("DialogDeleteTvTitle"),
            AppStrings.Get("DialogDeleteTvMsg", tv.Name),
            AppStrings.Get("BtnYesDelete"),
            AppStrings.Get("BtnCancel")
        );

        if (confirm)
        {
            TvPairingManager.Instance.RemovePairedTv(tv.Id);
            LoadMobilePairedTvs();
        }
    }

    private async void OnUnlockRemoteTvClicked(object? sender, EventArgs e)
    {
        TvDevice? tv = null;
        if (sender is Button btn && btn.CommandParameter is TvDevice d) tv = d;
        else if (sender is BindableObject bo && bo.BindingContext is TvDevice ctxD) tv = ctxD;

        if (tv == null) return;

        try
        {
            var (success, message) = await TvClientService.Instance.UnlockParentAreaOnTvAsync(tv);
            if (success)
            {
                await DisplayAlert(
                    AppStrings.Get("TvAuthTitle"),
                    AppStrings.Get("TvUnlockedSuccess"),
                    AppStrings.Get("BtnOk")
                );
            }
            else
            {
                await DisplayAlert(AppStrings.Get("TvAuthTitle"), $"Não foi possível desbloquear a TV ({message}). Certifique-se de que a TV está ligada à mesma rede Wi-Fi.", AppStrings.Get("BtnOk"));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(AppStrings.Get("TvAuthTitle"), ex.Message, AppStrings.Get("BtnOk"));
        }
    }

    // ==========================================
    // SINCRONIZAÇÃO COMPLETA TELEMÓVEL -> TV
    // ==========================================

    private TvSyncPayload BuildCurrentSyncPayload()
    {
        var currentPin = Preferences.Default.Get(PinKey, "1234");
        var duration = (int)Preferences.Default.Get(DurationKey, 15.0);
        var lang = LocalizationManager.Instance.CurrentLanguage;
        var canChangeTheme = Preferences.Default.Get(AllowChildDiscoverKey, true);

        var approvedVideos = _videos.Select(v => new TvSyncApprovedVideo
        {
            Id = v.Id,
            Title = v.Title,
            IsDiscovered = v.IsDiscovered
        }).ToList();

        var playlists = _approvedPlaylists.Select(p => new TvSyncApprovedPlaylist
        {
            Id = p.Id,
            Title = p.Title,
            ChannelTitle = p.ChannelTitle,
            ThumbnailUrl = p.ThumbnailUrl,
            VideoCount = p.VideoCount,
            Videos = p.Videos?.Select(v => new TvSyncApprovedVideo { Id = v.Id, Title = v.Title, IsDiscovered = v.IsDiscovered }).ToList() ?? new List<TvSyncApprovedVideo>()
        }).ToList();

        var blocked = _blockedVideos.Select(b => new TvSyncBlockedVideo
        {
            Id = b.Id,
            Title = b.Title,
            ThumbnailUrl = b.ThumbnailUrl,
            BlockedAt = b.BlockedAt
        }).ToList();

        var themes = _parentThemes.Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

        return new TvSyncPayload
        {
            SourceDeviceId = Guid.NewGuid().ToString(),
            SourceDeviceName = DeviceInfo.Name ?? "Telemóvel",
            SyncedAt = DateTime.UtcNow,
            Videos = approvedVideos,
            Playlists = playlists,
            BlockedVideos = blocked,
            AllowedThemes = themes,
            MealDurationMinutes = duration,
            PrimaryLanguage = lang,
            Pin = currentPin,
            AllowChildDiscover = canChangeTheme
        };
    }

    private async void OnSyncAllWithTvClicked(object? sender, EventArgs e)
    {
        var tvs = _mobilePairedTvs.ToList();
        if (tvs.Count == 0)
        {
            await DisplayAlert(AppStrings.Get("DialogNoPairedTvsTitle"), AppStrings.Get("DialogNoPairedTvsMsg"), AppStrings.Get("BtnOk"));
            return;
        }

        var payload = BuildCurrentSyncPayload();
        int successCount = 0;

        if (ScanTvsIndicator is not null)
        {
            ScanTvsIndicator.IsVisible = true;
            ScanTvsIndicator.IsRunning = true;
        }

        try
        {
            foreach (var tv in tvs)
            {
                var (success, _) = await TvClientService.Instance.SyncDataToTvAsync(tv, payload);
                if (success) successCount++;
            }

            if (successCount > 0)
            {
                await DisplayAlert(AppStrings.Get("DialogSyncSuccessTitle"), AppStrings.Get("DialogSyncSuccessMsg", $"{successCount}/{tvs.Count}"), AppStrings.Get("BtnOk"));
            }
            else
            {
                await DisplayAlert(AppStrings.Get("DialogSyncErrorTitle"), AppStrings.Get("DialogSyncErrorMsg", "0"), AppStrings.Get("BtnOk"));
            }
        }
        finally
        {
            if (ScanTvsIndicator is not null)
            {
                ScanTvsIndicator.IsRunning = false;
                ScanTvsIndicator.IsVisible = false;
            }
        }
    }

    private async void OnSyncSingleTvClicked(object? sender, EventArgs e)
    {
        TvDevice? tv = null;
        if (sender is Button btn && btn.CommandParameter is TvDevice d) tv = d;
        else if (sender is BindableObject bo && bo.BindingContext is TvDevice ctxD) tv = ctxD;

        if (tv == null) return;

        await SyncDataToTvInternalAsync(tv, showSuccessDialog: true);
    }

    private async Task SyncDataToTvInternalAsync(TvDevice tv, bool showSuccessDialog)
    {
        var payload = BuildCurrentSyncPayload();
        var (success, errorMsg) = await TvClientService.Instance.SyncDataToTvAsync(tv, payload);

        if (success)
        {
            if (showSuccessDialog)
            {
                await DisplayAlert(AppStrings.Get("DialogSyncSuccessTitle"), AppStrings.Get("DialogSyncSuccessMsg", tv.Name), AppStrings.Get("BtnOk"));
            }
        }
        else
        {
            if (showSuccessDialog)
            {
                await DisplayAlert(AppStrings.Get("DialogSyncErrorTitle"), errorMsg ?? AppStrings.Get("DialogSyncErrorMsg", tv.Name), AppStrings.Get("BtnOk"));
            }
        }
    }

    private void OnTvServerSyncDataReceived(TvSyncPayload payload)
    {
        if (payload == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // 1. Sincronizar PIN de Pais
                if (!string.IsNullOrWhiteSpace(payload.Pin))
                {
                    Preferences.Default.Set(PinKey, payload.Pin);
                }

                // 2. Sincronizar Duração de Sessão
                if (payload.MealDurationMinutes >= 5)
                {
                    _mealDurationMinutes = payload.MealDurationMinutes;
                    Preferences.Default.Set(DurationKey, (double)_mealDurationMinutes);
                    UpdateMealDurationUI();
                }

                // 3. Sincronizar Idioma
                if (!string.IsNullOrWhiteSpace(payload.PrimaryLanguage))
                {
                    LocalizationManager.Instance.CurrentLanguage = payload.PrimaryLanguage;
                    Preferences.Default.Set(LanguageKey, payload.PrimaryLanguage);
                }

                // 4. Sincronizar Vídeos Aprovados
                if (payload.Videos != null && payload.Videos.Count > 0)
                {
                    _videos.Clear();
                    foreach (var av in payload.Videos)
                    {
                        _videos.Add(new ApprovedVideo(av.Id, av.Title, av.IsDiscovered));
                    }
                    SaveVideos();
                }

                // 5. Sincronizar Playlists Aprovadas
                if (payload.Playlists != null && payload.Playlists.Count > 0)
                {
                    _approvedPlaylists.Clear();
                    foreach (var ap in payload.Playlists)
                    {
                        var playlistVideos = ap.Videos?.Select(v => new ApprovedVideo(v.Id, v.Title, v.IsDiscovered)).ToList() ?? new List<ApprovedVideo>();
                        _approvedPlaylists.Add(new ApprovedPlaylist(ap.Id, ap.Title, ap.ChannelTitle, ap.ThumbnailUrl, ap.VideoCount, playlistVideos));
                    }
                    SaveApprovedPlaylists();
                }

                // 6. Sincronizar Vídeos Bloqueados
                if (payload.BlockedVideos != null && payload.BlockedVideos.Count > 0)
                {
                    _blockedVideos.Clear();
                    _blockedVideoIds.Clear();
                    foreach (var bv in payload.BlockedVideos)
                    {
                        _blockedVideos.Add(new BlockedVideoItem
                        {
                            Id = bv.Id,
                            Title = bv.Title,
                            BlockedAt = bv.BlockedAt
                        });
                        _blockedVideoIds.Add(bv.Id);
                    }
                    SaveBlockedVideos();
                }

                // 7. Sincronizar Temas Autorizados
                if (payload.AllowedThemes != null && payload.AllowedThemes.Count > 0)
                {
                    Preferences.Default.Set(AllowedThemesKey, JsonSerializer.Serialize(payload.AllowedThemes));
                    LoadAllowedThemes();
                }

                RebuildChildVideoFeed();
                UpdateTvUI();
                UpdateTvVisualFocus();

                // Notificação de sucesso no ecrã da TV
                _ = DisplayAlert(AppStrings.Get("DialogSyncSuccessTitle"), AppStrings.Get("TvSyncReceivedToast", payload.SourceDeviceName), AppStrings.Get("BtnOk"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnTvServerSyncDataReceived] {ex.Message}");
            }
        });
    }

    private int _tvRemoteAuthCol = 0; // 0: Usar PIN, 1: Cancelar
    private int _secretEmergencyTapCount = 0;
    private DateTime _lastSecretEmergencyTapTime = DateTime.MinValue;

    private void OnSecretEmergencyIconTapped(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSecretEmergencyTapTime).TotalSeconds > 3.5)
        {
            _secretEmergencyTapCount = 0;
        }

        _lastSecretEmergencyTapTime = now;
        _secretEmergencyTapCount++;

        if (_secretEmergencyTapCount >= 5)
        {
            _secretEmergencyTapCount = 0;
            RevealEmergencyPinOnTv();
        }
    }

    private void RevealEmergencyPinOnTv()
    {
        if (BtnTvAuthUsePinFallback is not null)
        {
            BtnTvAuthUsePinFallback.IsVisible = true;
        }
        if (TvEmergencyBanner is not null)
        {
            TvEmergencyBanner.IsVisible = true;
        }
        _tvRemoteAuthCol = 0;
        UpdateTvRemoteAuthVisualFocus();
    }

    private void UpdateTvRemoteAuthVisualFocus()
    {
        if (DeviceModeService.IsTv || DeviceInfo.Idiom == DeviceIdiom.TV)
        {
            var isPinVisible = BtnTvAuthUsePinFallback is not null && BtnTvAuthUsePinFallback.IsVisible;
            if (isPinVisible)
            {
                SetVisualTvFocus(BtnTvAuthUsePinFallback, _tvRemoteAuthCol == 0);
                SetVisualTvFocus(BtnTvAuthCancel, _tvRemoteAuthCol == 1);
            }
            else
            {
                SetVisualTvFocus(BtnTvAuthUsePinFallback, false);
                SetVisualTvFocus(BtnTvAuthCancel, true);
            }
        }
    }

    private void OnTvAuthUsePinFallbackClicked(object? sender, EventArgs e)
    {
        if (TvRemoteAuthPromptOverlay is not null)
        {
            TvRemoteAuthPromptOverlay.IsVisible = false;
        }
        ShowPinPrompt();
    }

    private void OnTvAuthCancelClicked(object? sender, EventArgs e)
    {
        if (TvRemoteAuthPromptOverlay is not null)
        {
            TvRemoteAuthPromptOverlay.IsVisible = false;
        }
        UpdateTvUI();
        UpdateTvVisualFocus();
    }

    // ==========================================
    // ENVIAR VÍDEOS / HISTÓRIAS PARA A TV
    // ==========================================

    private void OnSendApprovedListToTvClicked(object? sender, EventArgs e)
    {
        if (_mobilePairedTvs.Count == 0)
        {
            DisplayAlert(AppStrings.Get("DialogNoPairedTvsTitle"), AppStrings.Get("DialogNoPairedTvsMsg"), AppStrings.Get("BtnOk"));
            return;
        }

        if (_videos.Count == 0)
        {
            DisplayAlert(AppStrings.Get("DialogEmptyListTitle"), AppStrings.Get("DialogEmptyListMsg"), AppStrings.Get("BtnOk"));
            return;
        }

        _pendingVideosToSendToTv = _videos.ToList();
        OpenSendToTvModal();
    }

    private void OnQuickSendSingleVideoToTvClicked(object? sender, EventArgs e)
    {
        if (_mobilePairedTvs.Count == 0)
        {
            DisplayAlert(AppStrings.Get("DialogNoPairedTvsTitle"), AppStrings.Get("DialogNoPairedTvsMsg"), AppStrings.Get("BtnOk"));
            return;
        }

        ApprovedVideo? targetVideo = null;
        if (sender is Button btn && btn.CommandParameter is ParentVideoItem pvi)
        {
            targetVideo = new ApprovedVideo(pvi.Id, pvi.Title);
        }
        else if (sender is Button btn2 && btn2.CommandParameter is ApprovedVideo av)
        {
            targetVideo = av;
        }
        else if (sender is BindableObject bo && bo.BindingContext is ParentVideoItem boPvi)
        {
            targetVideo = new ApprovedVideo(boPvi.Id, boPvi.Title);
        }

        if (targetVideo != null)
        {
            _pendingVideosToSendToTv = new List<ApprovedVideo> { targetVideo };
            OpenSendToTvModal();
        }
    }

    private void OpenSendToTvModal()
    {
        if (_pendingVideosToSendToTv == null || _pendingVideosToSendToTv.Count == 0) return;

        if (SendToTvCountLabel is not null)
        {
            SendToTvCountLabel.Text = _pendingVideosToSendToTv.Count == 1 
                ? $"1 história: '{_pendingVideosToSendToTv[0].Title}'" 
                : $"{_pendingVideosToSendToTv.Count} histórias selecionadas";
        }

        if (TargetTvPicker is not null)
        {
            TargetTvPicker.ItemsSource = _mobilePairedTvs.Select(t => t.Name).ToList();
            if (_mobilePairedTvs.Count > 0 && TargetTvPicker.SelectedIndex < 0)
            {
                TargetTvPicker.SelectedIndex = 0;
            }
        }

        if (SendToTvStatusLabel is not null) SendToTvStatusLabel.IsVisible = false;
        if (SendToTvModalOverlay is not null) SendToTvModalOverlay.IsVisible = true;
    }

    private void OnCloseSendToTvModalClicked(object? sender, EventArgs e)
    {
        if (SendToTvModalOverlay is not null)
        {
            SendToTvModalOverlay.IsVisible = false;
        }
        _pendingVideosToSendToTv = null;
    }

    private async void OnSendPlayNowClicked(object? sender, EventArgs e)
    {
        await ExecuteSendToTvAsync(isPlayNow: true);
    }

    private async void OnSendAddToQueueClicked(object? sender, EventArgs e)
    {
        await ExecuteSendToTvAsync(isPlayNow: false);
    }

    private async Task ExecuteSendToTvAsync(bool isPlayNow)
    {
        if (_pendingVideosToSendToTv == null || _pendingVideosToSendToTv.Count == 0) return;

        var selectedIndex = TargetTvPicker?.SelectedIndex ?? -1;
        if (selectedIndex < 0 || selectedIndex >= _mobilePairedTvs.Count)
        {
            await DisplayAlert(AppStrings.Get("DialogSelectTvTitle"), AppStrings.Get("DialogSelectTvMsg"), AppStrings.Get("BtnOk"));
            return;
        }

        var tv = _mobilePairedTvs[selectedIndex];
        var tvItems = _pendingVideosToSendToTv.Select(v => TvVideoItem.FromApprovedVideo(v)).ToList();

        if (SendToTvIndicator is not null)
        {
            SendToTvIndicator.IsVisible = true;
            SendToTvIndicator.IsRunning = true;
        }
        if (BtnSendPlayNow is not null) BtnSendPlayNow.IsEnabled = false;
        if (BtnSendAddToQueue is not null) BtnSendAddToQueue.IsEnabled = false;

        try
        {
            bool success;
            if (isPlayNow)
            {
                if (tvItems.Count == 1)
                {
                    var res = await TvClientService.Instance.PlayNowAsync(tv, tvItems[0]);
                    success = res.success;
                }
                else
                {
                    var res = await TvClientService.Instance.ReplaceQueueAndPlayAsync(tv, tvItems);
                    success = res.success;
                }
            }
            else
            {
                var res = await TvClientService.Instance.AddVideosToQueueAsync(tv, tvItems);
                success = res.success;
            }

            if (success)
            {
                if (SendToTvModalOverlay is not null) SendToTvModalOverlay.IsVisible = false;
                var msg = isPlayNow 
                    ? AppStrings.Get("DialogSentToTvSuccessMsg", tv.Name)
                    : AppStrings.Get("DialogSentQueueSuccessMsg", tv.Name);

                await DisplayAlert(AppStrings.Get("DialogSentToTvSuccessTitle"), msg, AppStrings.Get("BtnUnderstood"));
            }
            else
            {
                if (SendToTvStatusLabel is not null)
                {
                    SendToTvStatusLabel.Text = $"Falha no envio para a TV '{tv.Name}'. Verifique a ligação Wi-Fi.";
                    SendToTvStatusLabel.TextColor = (Color)Application.Current!.Resources["AccentRed"];
                    SendToTvStatusLabel.IsVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            if (SendToTvStatusLabel is not null)
            {
                SendToTvStatusLabel.Text = $"Erro: {ex.Message}";
                SendToTvStatusLabel.TextColor = (Color)Application.Current!.Resources["AccentRed"];
                SendToTvStatusLabel.IsVisible = true;
            }
        }
        finally
        {
            if (SendToTvIndicator is not null)
            {
                SendToTvIndicator.IsRunning = false;
                SendToTvIndicator.IsVisible = false;
            }
            if (BtnSendPlayNow is not null) BtnSendPlayNow.IsEnabled = true;
            if (BtnSendAddToQueue is not null) BtnSendAddToQueue.IsEnabled = true;
            _pendingVideosToSendToTv = null;
        }
    }

    #endregion

    #endregion

    #region Apresentação Inicial / Tutorial / Onboarding

    private void ShowOnboardingOverlay()
    {
        _currentOnboardingSlide = 1;
        _tvOnboardingBtn = 1;
        RenderOnboardingSlide(_currentOnboardingSlide);
        if (OnboardingModalOverlay is not null)
        {
            OnboardingModalOverlay.IsVisible = true;
            UpdateTvOnboardingVisualFocus();
        }
    }

    private void RenderOnboardingSlide(int slideIndex)
    {
        if (OnboardingModalOverlay is null) return;

        _currentOnboardingSlide = Math.Clamp(slideIndex, 1, 5);

        // Atualizar contador e botões
        if (OnboardingStepLabel is not null)
        {
            OnboardingStepLabel.Text = string.Format(AppStrings.Get("OnboardingStepFormat"), _currentOnboardingSlide, 5);
        }

        if (BtnOnboardingPrev is not null)
        {
            BtnOnboardingPrev.IsVisible = _currentOnboardingSlide > 1;
        }

        if (BtnOnboardingNext is not null)
        {
            BtnOnboardingNext.Text = _currentOnboardingSlide == 5 
                ? AppStrings.Get("OnboardingFinish") 
                : AppStrings.Get("OnboardingNext");
        }

        // Atualizar indicadores de progresso (Dots)
        UpdateOnboardingDots(_currentOnboardingSlide);
        UpdateTvOnboardingVisualFocus();

        // Preencher conteúdos do slide atual
        switch (_currentOnboardingSlide)
        {
            case 1:
                SetOnboardingSlideContent(
                    icon: "🐥",
                    title: AppStrings.Get("Slide1Title"),
                    subtitle: AppStrings.Get("Slide1Subtitle"),
                    imageSource: "slide1_welcome.jpg",
                    bullet1: AppStrings.Get("Slide1Bullet1"),
                    bullet2: AppStrings.Get("Slide1Bullet2"),
                    bullet3: AppStrings.Get("Slide1Bullet3")
                );
                break;
            case 2:
                SetOnboardingSlideContent(
                    icon: "🎬",
                    title: AppStrings.Get("Slide2Title"),
                    subtitle: AppStrings.Get("Slide2Subtitle"),
                    imageSource: "slide2_modes.jpg",
                    bullet1: AppStrings.Get("Slide2Bullet1"),
                    bullet2: AppStrings.Get("Slide2Bullet2"),
                    bullet3: AppStrings.Get("Slide2Bullet3")
                );
                break;
            case 3:
                SetOnboardingSlideContent(
                    icon: "▶️ 🚫",
                    title: AppStrings.Get("Slide3Title"),
                    subtitle: AppStrings.Get("Slide3Subtitle"),
                    imageSource: "slide3_preview_hide.jpg",
                    bullet1: AppStrings.Get("Slide3Bullet1"),
                    bullet2: AppStrings.Get("Slide3Bullet2"),
                    bullet3: AppStrings.Get("Slide3Bullet3")
                );
                break;
            case 4:
                SetOnboardingSlideContent(
                    icon: "⏱️ 🔒",
                    title: AppStrings.Get("Slide4Title"),
                    subtitle: AppStrings.Get("Slide4Subtitle"),
                    imageSource: "slide4_timer_lock.jpg",
                    bullet1: AppStrings.Get("Slide4Bullet1"),
                    bullet2: AppStrings.Get("Slide4Bullet2"),
                    bullet3: AppStrings.Get("Slide4Bullet3")
                );
                break;
            case 5:
                SetOnboardingSlideContent(
                    icon: "📺 📱",
                    title: AppStrings.Get("Slide5Title"),
                    subtitle: AppStrings.Get("Slide5Subtitle"),
                    imageSource: "slide5_smart_tv.jpg",
                    bullet1: AppStrings.Get("Slide5Bullet1"),
                    bullet2: AppStrings.Get("Slide5Bullet2"),
                    bullet3: AppStrings.Get("Slide5Bullet3")
                );
                break;
        }
    }

    private void SetOnboardingSlideContent(string icon, string title, string subtitle, string? imageSource, string bullet1, string bullet2, string bullet3)
    {
        try
        {
            if (OnboardingSlideIcon is not null) OnboardingSlideIcon.Text = icon;
            if (OnboardingSlideTitle is not null) OnboardingSlideTitle.Text = title;
            if (OnboardingSlideSubtitle is not null) OnboardingSlideSubtitle.Text = subtitle;
            if (OnboardingSlideImage is not null)
            {
                try
                {
                    OnboardingSlideImage.Source = string.IsNullOrEmpty(imageSource) ? null : ImageSource.FromFile(imageSource);
                }
                catch
                {
                    OnboardingSlideImage.Source = null;
                }
            }
            if (OnboardingImageBorder is not null)
            {
                OnboardingImageBorder.IsVisible = !string.IsNullOrEmpty(imageSource);
            }
            if (OnboardingBullet1 is not null) SetFormattedBullet(OnboardingBullet1, bullet1);
            if (OnboardingBullet2 is not null) SetFormattedBullet(OnboardingBullet2, bullet2);
            if (OnboardingBullet3 is not null) SetFormattedBullet(OnboardingBullet3, bullet3);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetOnboardingSlideContent] {ex.Message}");
        }
    }

    private static void SetFormattedBullet(Label label, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            label.FormattedText = null;
            label.Text = string.Empty;
            return;
        }

        var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
        if (parts.Length >= 3)
        {
            var fs = new FormattedString();
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                bool isBold = (i % 2 == 1);
                fs.Spans.Add(new Span
                {
                    Text = parts[i],
                    FontAttributes = isBold ? FontAttributes.Bold : FontAttributes.None
                });
            }
            label.FormattedText = fs;
        }
        else
        {
            label.FormattedText = null;
            label.Text = text.Replace("**", "");
        }
    }

    private void UpdateOnboardingDots(int activeIndex)
    {
        var activeColor = Color.FromArgb("#059669");
        var inactiveColor = Color.FromArgb("#8094A3B8");

        var dots = new[] { OnboardingDot1, OnboardingDot2, OnboardingDot3, OnboardingDot4, OnboardingDot5 };
        for (int i = 0; i < dots.Length; i++)
        {
            if (dots[i] is null) continue;
            bool isActive = (i + 1) == activeIndex;
            dots[i]!.WidthRequest = isActive ? 24 : 8;
            dots[i]!.BackgroundColor = isActive ? activeColor : inactiveColor;
        }
    }

    private void OnOnboardingNextClicked(object? sender, EventArgs e)
    {
        if (_currentOnboardingSlide < 5)
        {
            _currentOnboardingSlide++;
            RenderOnboardingSlide(_currentOnboardingSlide);
        }
        else
        {
            FinishOnboarding();
        }
    }

    private void OnOnboardingPrevClicked(object? sender, EventArgs e)
    {
        if (_currentOnboardingSlide > 1)
        {
            _currentOnboardingSlide--;
            RenderOnboardingSlide(_currentOnboardingSlide);
        }
    }

    private void OnSkipOnboardingClicked(object? sender, EventArgs e)
    {
        FinishOnboarding();
    }

    private void FinishOnboarding()
    {
        Preferences.Default.Set(OnboardingCompletedKey, true);
        if (OnboardingModalOverlay is not null)
        {
            OnboardingModalOverlay.IsVisible = false;
        }
        _ = PromptScreenLockIfNeededAsync();
    }

    private void OnShowOnboardingClicked(object? sender, EventArgs e)
    {
        ShowOnboardingOverlay();
    }

    #endregion
}

public class ThemeChipItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; set; } = string.Empty;
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    private bool _isTvFocused;
    public bool IsTvFocused
    {
        get => _isTvFocused;
        set
        {
            if (_isTvFocused != value)
            {
                _isTvFocused = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsTvFocused)));
            }
        }
    }
}

public class TvCategoryItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isTvFocused;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    public bool IsTvFocused
    {
        get => _isTvFocused;
        set
        {
            if (_isTvFocused != value)
            {
                _isTvFocused = value;
                OnPropertyChanged(nameof(IsTvFocused));
            }
        }
    }

    public TvCategoryItem() { }
    public TvCategoryItem(string id, string name, string icon)
    {
        Id = id;
        Name = name;
        Icon = icon;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class TvSuggestionItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isTvFocused;
    public string Text { get; set; } = string.Empty;

    public bool IsTvFocused
    {
        get => _isTvFocused;
        set
        {
            if (_isTvFocused != value)
            {
                _isTvFocused = value;
                OnPropertyChanged(nameof(IsTvFocused));
            }
        }
    }

    public TvSuggestionItem() { }
    public TvSuggestionItem(string text) { Text = text; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class ApprovedVideo : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isTvFocused;
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsDiscovered { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTvFocused
    {
        get => _isTvFocused;
        set
        {
            if (_isTvFocused != value)
            {
                _isTvFocused = value;
                OnPropertyChanged(nameof(IsTvFocused));
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg";
    [System.Text.Json.Serialization.JsonIgnore]
    public string BadgeIcon => IsDiscovered ? "🎲" : "⭐";
    [System.Text.Json.Serialization.JsonIgnore]
    public string BadgeText => IsDiscovered ? AppStrings.Get("BadgeDiscovery") : AppStrings.Get("ApprovedByParents");
    [System.Text.Json.Serialization.JsonIgnore]
    public Color BadgeColor => IsDiscovered ? Color.FromArgb("#2563EB") : Color.FromArgb("#059669");

    public ApprovedVideo() { }
    public ApprovedVideo(string id, string title, bool isDiscovered = false)
    {
        Id = id;
        Title = title;
        IsDiscovered = isDiscovered;
    }

    public ApprovedVideo Clone() => new(Id, Title, IsDiscovered);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class ParentVideoItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isApproved;
    private bool _isTvFocused;
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSendToTvVisible => !Services.DeviceModeService.IsTv;

    public bool IsTvFocused
    {
        get => _isTvFocused;
        set
        {
            if (_isTvFocused != value)
            {
                _isTvFocused = value;
                OnPropertyChanged(nameof(IsTvFocused));
            }
        }
    }

    public bool IsApproved
    {
        get => _isApproved;
        set
        {
            if (_isApproved != value)
            {
                _isApproved = value;
                OnPropertyChanged(nameof(IsApproved));
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ActionButtonColor));
                OnPropertyChanged(nameof(ActionButtonTextColor));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeColor));
                OnPropertyChanged(nameof(StatusBadgeBgColor));
            }
        }
    }

    public string ActionButtonText => IsApproved ? "✓" : "＋";
    public Color ActionButtonColor => IsApproved ? Color.FromArgb("#10B981") : Color.FromArgb("#2563EB");
    public Color ActionButtonTextColor => Color.FromArgb("#FFFFFF");
    public string StatusBadgeText => IsApproved ? AppStrings.Get("BadgeApproved") : AppStrings.Get("BadgeAvailable");
    public Color StatusBadgeColor => IsApproved ? Color.FromArgb("#059669") : Color.FromArgb("#64748B");
    public Color StatusBadgeBgColor => IsApproved ? Color.FromArgb("#2610B981") : Color.FromArgb("#2664748B");

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class BlockedVideoItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isTvFocused;
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg";
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTvFocused
    {
        get => _isTvFocused;
        set
        {
            if (_isTvFocused != value)
            {
                _isTvFocused = value;
                OnPropertyChanged(nameof(IsTvFocused));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class ApprovedPlaylist : System.ComponentModel.INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string _channelTitle = string.Empty;
    private string _thumbnailUrl = string.Empty;
    private int _videoCount = 0;
    private List<ApprovedVideo> _videos = new();

    public string Id { get => _id; set { _id = value; OnPropertyChanged(nameof(Id)); } }
    public string Title { get => _title; set { _title = value; OnPropertyChanged(nameof(Title)); } }
    public string ChannelTitle { get => _channelTitle; set { _channelTitle = value; OnPropertyChanged(nameof(ChannelTitle)); } }
    public string ThumbnailUrl { get => _thumbnailUrl; set { _thumbnailUrl = value; OnPropertyChanged(nameof(ThumbnailUrl)); } }
    public int VideoCount { get => _videoCount; set { _videoCount = value; OnPropertyChanged(nameof(VideoCount)); } }
    public List<ApprovedVideo> Videos { get => _videos; set { _videos = value; OnPropertyChanged(nameof(Videos)); } }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;

    public ApprovedPlaylist() { }
    public ApprovedPlaylist(string id, string title, string channelTitle, string thumb, int count, List<ApprovedVideo>? videos = null)
    {
        Id = id;
        Title = title;
        ChannelTitle = channelTitle;
        ThumbnailUrl = thumb;
        VideoCount = count;
        if (videos != null) Videos = videos;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class PlaylistCardItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isApproved;
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string _channelTitle = string.Empty;
    private string _thumbnailUrl = string.Empty;
    private int _videoCount = 0;

    public string Id { get => _id; set { _id = value; OnPropertyChanged(nameof(Id)); } }
    public string Title { get => _title; set { _title = value; OnPropertyChanged(nameof(Title)); } }
    public string ChannelTitle { get => _channelTitle; set { _channelTitle = value; OnPropertyChanged(nameof(ChannelTitle)); } }
    public string ThumbnailUrl { get => _thumbnailUrl; set { _thumbnailUrl = value; OnPropertyChanged(nameof(ThumbnailUrl)); } }
    public int VideoCount { get => _videoCount; set { _videoCount = value; OnPropertyChanged(nameof(VideoCount)); OnPropertyChanged(nameof(VideoCountBadge)); } }
    public string VideoCountBadge => $"{VideoCount} vídeos";

    public bool IsApproved
    {
        get => _isApproved;
        set
        {
            if (_isApproved != value)
            {
                _isApproved = value;
                OnPropertyChanged(nameof(IsApproved));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeColor));
                OnPropertyChanged(nameof(StatusBadgeBgColor));
                OnPropertyChanged(nameof(ApproveButtonText));
                OnPropertyChanged(nameof(ApproveButtonBgColor));
            }
        }
    }

    public string StatusBadgeText => IsApproved ? "✓ Playlist Aprovada" : "Disponível no YouTube";
    public Color StatusBadgeColor => IsApproved ? Color.FromArgb("#059669") : Color.FromArgb("#64748B");
    public Color StatusBadgeBgColor => IsApproved ? Color.FromArgb("#2610B981") : Color.FromArgb("#2664748B");
    public string ApproveButtonText => IsApproved ? "✓ Aprovada" : "＋ Aprovar Playlist";
    public Color ApproveButtonBgColor => IsApproved ? Color.FromArgb("#059669") : Color.FromArgb("#10B981");

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
