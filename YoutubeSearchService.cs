using System.Text.Json;
using System.Text.RegularExpressions;

namespace yChicaTube;

public record YoutubeSearchResult(
    string Id, 
    string Title, 
    string ThumbnailUrl, 
    int RelevanceScore = 0,
    string ChannelTitle = "",
    string DescriptionSnippet = ""
);

public record YoutubePlaylistResult(
    string Id,
    string Title,
    string ThumbnailUrl,
    int VideoCount,
    string ChannelTitle,
    List<YoutubeSearchResult>? SampleVideos = null,
    bool IsApproved = false
);

public record SearchLanguageOption(
    string Code, 
    string DisplayName, 
    string QuerySuffix, 
    string Hl, 
    string Gl, 
    List<string> BoostKeywords,
    List<string> ExcludeKeywords,
    List<string> VerifiedChannels,
    List<string> Suggestions
);

internal static class YoutubeSearchService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime CachedAt, List<YoutubeSearchResult> Results)> _searchCache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCache()
    {
        _searchCache.Clear();
    }

    public static readonly List<SearchLanguageOption> AvailableLanguages = new()
    {
        new SearchLanguageOption(
            "pt-PT",
            "🇵🇹 Português (Portugal)",
            " em português de portugal",
            "pt-PT",
            "PT",
            new List<string> 
            { 
                "português de portugal", "portugues de portugal", "pt-pt", "pt pt", "ptpt", "portugal", 
                "dobrado", "dobrada", "dobrados", "dobradas", "dobragem", "panda", "canal panda", 
                "panda e os caricas", "rtp", "zig zag", "sic k", "em português de portugal",
                "patrulha pata", "ruca", "porquinha peppa", "ovelha choné", "masha e o urso pt",
                "super wings portugal", "cocomelon em português de portugal", "noddy portugal",
                "bob o construtor portugal", "canções da maria", "carochinha", "avô cantigas", "pequenada"
            },
            new List<string> 
            { 
                "brasil", "brazil", "pt-br", "pt br", "pt_br", "ptbr", "br", "(br)", "[br]", "- br", "_br",
                "brasileiro", "brasileira", "do brasil", "em português do brasil", "em portugues do brasil",
                "português br", "portugues br", "dublado", "dublada", "dublados", "dubladas", "dublagem",
                "historinha", "turminha", "molecada", "patrulha canina", "turma da mônica", "galinha pintadinha", 
                "totoykids", "luccas neto", "mundo bita", "bob zoom"
            },
            new List<string>
            {
                "Canal Panda", "Panda e os Caricas", "RTP", "Zig Zag", "Pocoyo Português - Canal Oficial",
                "Patrulha Pata Oficial", "Porquinha Peppa Português de Portugal", "Ruca Oficial",
                "Masha e o Urso Portugal", "Cocomelon em Português de Portugal", "Super Wings Portugal"
            },
            new List<string>
            {
                "Panda e os Caricas",
                "Pocoyo em Português de Portugal",
                "Filmes infantis dobrados PT-PT",
                "Patrulha Pata em Português",
                "Porquinha Peppa PT-PT",
                "Ruca em Português de Portugal",
                "Canções infantis de embalar",
                "Masha e o Urso PT-PT",
                "Super Wings dobrado em português"
            }
        ),
        new SearchLanguageOption(
            "pt-BR",
            "🇧🇷 Português (Brasil)",
            " em português do brasil",
            "pt-BR",
            "BR",
            new List<string> { "brasil", "pt-br", "pt br", "dublado", "dublagem", "do brasil", "historinha", "turminha" },
            new List<string> { "pt-pt", "pt pt", "ptpt", "de portugal", "dobrado em português", "patrulha pata", "canal panda" },
            new List<string> { "Galinha Pintadinha", "Mundo Bita", "Turma da Mônica", "Patrulha Canina" },
            new List<string>
            {
                "Galinha Pintadinha",
                "Mundo Bita",
                "Turma da Mônica",
                "Patrulha Canina",
                "Peppa Pig Português Brasil",
                "Historinhas Infantis",
                "Desenhos Animados Infantis"
            }
        ),
        new SearchLanguageOption(
            "en-GB",
            "🇬🇧 English (British / UK)",
            " british english for kids",
            "en-GB",
            "GB",
            new List<string> { "british english", "uk english", "cbeebies", "bbc", "british accent", "for kids" },
            new List<string> { "dublado", "dobrado", "em português", "en español" },
            new List<string> { "CBeebies", "Peppa Pig - Official Channel", "Thomas & Friends UK", "Bluey - Official Channel" },
            new List<string>
            {
                "Peppa Pig British English",
                "CBeebies official stories",
                "Thomas and Friends UK",
                "Bluey full episodes",
                "Nursery Rhymes British accent",
                "Paddington Bear"
            }
        ),
        new SearchLanguageOption(
            "en-US",
            "🇺🇸 English (American / US)",
            " american english for kids",
            "en-US",
            "US",
            new List<string> { "american english", "us english", "pbs kids", "nickelodeon", "for kids" },
            new List<string> { "dublado", "dobrado", "em português", "en español" },
            new List<string> { "Cocomelon - Nursery Rhymes", "PBS KIDS", "Sesame Street", "Paw Patrol Official" },
            new List<string>
            {
                "Cocomelon Nursery Rhymes",
                "Sesame Street American",
                "Paw Patrol Official US",
                "PBS Kids full episodes",
                "Disney cartoons english",
                "Super Simple Songs"
            }
        ),
        new SearchLanguageOption(
            "es-ES",
            "🇪🇸 Español (España / Castellano)",
            " en castellano de españa para niños",
            "es-ES",
            "ES",
            new List<string> { "castellano", "españa", "es-es", "clan tv", "rtve", "en español de españa" },
            new List<string> { "latino", "mexico", "argentina", "colombia", "dublado", "dobrado", "em português" },
            new List<string> { "Clan TVE", "Pocoyo en Español", "Cantajuego", "Peppa Pig Español de España" },
            new List<string>
            {
                "Pocoyo en Castellano España",
                "Cantajuego Oficial",
                "Peppa Pig Castellano",
                "Clan TVE dibujos",
                "Cuentos infantiles en castellano",
                "Patrulla Canina España"
            }
        ),
        new SearchLanguageOption(
            "es-MX",
            "🇲🇽 Español (México / Latinoamérica)",
            " en español latino para niños",
            "es-MX",
            "MX",
            new List<string> { "español latino", "latinoamerica", "mexico", "es-419", "la granja de zenon" },
            new List<string> { "castellano", "españa", "dublado", "dobrado", "em português" },
            new List<string> { "La Granja de Zenón", "El Reino Infantil", "Plaza Sésamo", "Cri Cri" },
            new List<string>
            {
                "La Granja de Zenón",
                "El Reino Infantil",
                "Peppa Pig Español Latino",
                "Canciones de la Granja",
                "Cuentos infantiles latino",
                "Plaza Sésamo"
            }
        ),
        new SearchLanguageOption(
            "es-AR",
            "🇦🇷 Español (Argentina)",
            " en español argentina para niños",
            "es-AR",
            "AR",
            new List<string> { "argentina", "pakapaka", "la granja de zenon", "canciones del zoo" },
            new List<string> { "castellano españa", "dublado", "dobrado", "em português" },
            new List<string> { "Pakapaka", "La Granja de Zenón", "El Reino Infantil" },
            new List<string>
            {
                "Pakapaka dibujos",
                "La Granja de Zenón canciones",
                "Cuentos infantiles argentina",
                "Canciones del Zoo"
            }
        ),
        new SearchLanguageOption(
            "fr-FR",
            "🇫🇷 Français (France)",
            " en français pour enfants",
            "fr-FR",
            "FR",
            new List<string> { "français", "france", "dessin animé", "pour enfants", "comptines" },
            new List<string> { "dublado", "dobrado", "em português", "en español" },
            new List<string> { "Comptines et Chansons", "Trotro Officiel", "Peppa Pig Français" },
            new List<string>
            {
                "Comptines pour enfants",
                "Peppa Pig Français",
                "Trotro officiel épisodes",
                "Dessins animés en français",
                "Simon Superlapin"
            }
        ),
        new SearchLanguageOption(
            "de-DE",
            "🇩🇪 Deutsch (Deutschland)",
            " auf deutsch für kinder",
            "de-DE",
            "DE",
            new List<string> { "deutsch", "kinderfilme", "kika", "zeichentrick", "kinderlieder" },
            new List<string> { "dublado", "dobrado", "em português", "en español" },
            new List<string> { "KiKA", "Die Sendung mit der Maus", "Peppa Pig Deutsch" },
            new List<string>
            {
                "Die Sendung mit der Maus",
                "Peppa Pig Deutsch",
                "KiKA für Kinder",
                "Kinderlieder zum Mitsingen",
                "Paw Patrol Deutsch"
            }
        ),
        new SearchLanguageOption(
            "it-IT",
            "🇮🇹 Italiano (Italia)",
            " in italiano per bambini",
            "it-IT",
            "IT",
            new List<string> { "italiano", "cartoni animati", "rai yoyo", "per bambini", "canzoncine" },
            new List<string> { "dublado", "dobrado", "em português", "en español" },
            new List<string> { "Rai Yoyo", "Canzoni per Bimbi", "Pimpa Ufficiale" },
            new List<string>
            {
                "Canzoni per bambini",
                "Peppa Pig Italiano",
                "Rai Yoyo cartoni",
                "Pimpa episodi",
                "Masha e Orso Italiano"
            }
        ),
        new SearchLanguageOption(
            "all",
            "🌐 Sem Filtro de Idioma (Internacional)",
            "",
            "pt",
            "PT",
            new List<string>(),
            new List<string>(),
            new List<string>(),
            new List<string>
            {
                "Panda e os Caricas",
                "Pocoyo",
                "Patrulha Pata",
                "Peppa Pig",
                "Bluey",
                "Desenhos Infantis"
            }
        )
    };

    public static readonly SearchLanguageOption NoneOption = new(
        "none",
        "⚪ Nenhum (Desativado)",
        "",
        "pt",
        "PT",
        new List<string>(),
        new List<string>(),
        new List<string>(),
        new List<string>()
    );

    public static readonly List<SearchLanguageOption> AvailableSecondaryLanguages = new()
    {
        NoneOption,
        AvailableLanguages[0],
        AvailableLanguages[1],
        AvailableLanguages[2],
        AvailableLanguages[3],
        AvailableLanguages[4],
        AvailableLanguages[5],
        AvailableLanguages[6],
        AvailableLanguages[7],
        AvailableLanguages[8],
        AvailableLanguages[9],
        AvailableLanguages[10]
    };

    public static SearchLanguageOption GetLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("none", StringComparison.OrdinalIgnoreCase))
            return NoneOption;
        return AvailableLanguages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[0];
    }

    public static List<SearchLanguageOption> GetActiveLanguages(IReadOnlyList<string>? languageCodes)
    {
        var list = new List<SearchLanguageOption>();
        if (languageCodes != null)
        {
            foreach (var code in languageCodes)
            {
                if (!string.IsNullOrWhiteSpace(code) && !code.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    var opt = AvailableLanguages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (opt != null && !list.Any(x => x.Code.Equals(opt.Code, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(opt);
                    }
                }
            }
        }
        if (list.Count == 0)
        {
            list.Add(AvailableLanguages[0]);
        }
        return list;
    }

    public static async Task<List<string>> GetSuggestionsAsync(string query, IReadOnlyList<string>? languageCodes)
    {
        var activeLangs = GetActiveLanguages(languageCodes);
        var primaryLang = activeLangs[0];

        if (string.IsNullOrWhiteSpace(query))
        {
            var combinedSuggestions = new List<string>();
            var seenSuggest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in primaryLang.Suggestions.Take(5))
            {
                if (seenSuggest.Add(s)) combinedSuggestions.Add(s);
            }
            if (activeLangs.Count > 1)
            {
                foreach (var s in activeLangs[1].Suggestions.Take(3))
                {
                    if (seenSuggest.Add(s)) combinedSuggestions.Add(s);
                }
            }
            if (activeLangs.Count > 2)
            {
                foreach (var s in activeLangs[2].Suggestions.Take(2))
                {
                    if (seenSuggest.Add(s)) combinedSuggestions.Add(s);
                }
            }
            return combinedSuggestions;
        }

        var suggestions = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task FetchSuggestionsForLangAndTermAsync(SearchLanguageOption l, string term)
        {
            try
            {
                var url = $"https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&q={Uri.EscapeDataString(term)}&hl={l.Hl}&gl={l.Gl}";
                var json = await HttpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 1)
                {
                    var array = doc.RootElement[1];
                    if (array.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in array.EnumerateArray())
                        {
                            var text = item.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                var shouldExclude = l.ExcludeKeywords.Any(k => 
                                    text.Contains(k, StringComparison.OrdinalIgnoreCase) && 
                                    !activeLangs.Any(al => al.BoostKeywords.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase))));
                                if (shouldExclude) continue;

                                if (seen.Add(text))
                                {
                                    suggestions.Add(text);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[yChicaTube.Suggestions] Erro: {ex.Message}");
            }
        }

        var cleanQuery = query.Trim();
        foreach (var lang in activeLangs)
        {
            if (lang.Code == "pt-PT" && !cleanQuery.Contains("portugal", StringComparison.OrdinalIgnoreCase) && !cleanQuery.Contains("pt", StringComparison.OrdinalIgnoreCase))
            {
                await FetchSuggestionsForLangAndTermAsync(lang, $"{cleanQuery} portugal");
            }
            await FetchSuggestionsForLangAndTermAsync(lang, cleanQuery);
            if (suggestions.Count >= 10) break;
        }

        if (suggestions.Count == 0)
        {
            foreach (var lang in activeLangs)
            {
                foreach (var s in lang.Suggestions.Where(s => s.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase)))
                {
                    if (seen.Add(s)) suggestions.Add(s);
                }
            }
        }

        return suggestions.Take(8).ToList();
    }

    public static Task<List<string>> GetSuggestionsAsync(string query, string languageCode = "pt-PT")
        => GetSuggestionsAsync(query, new[] { languageCode });

    public static List<string> GetDefaultThemes(IReadOnlyList<string>? languageCodes)
    {
        var activeLangs = GetActiveLanguages(languageCodes);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pThemes = GetSingleLanguageDefaultThemes(activeLangs[0]);
        foreach (var t in pThemes.Take(6))
        {
            if (seen.Add(t)) result.Add(t);
        }

        if (activeLangs.Count > 1)
        {
            var sThemes = GetSingleLanguageDefaultThemes(activeLangs[1]);
            foreach (var t in sThemes.Take(3))
            {
                if (seen.Add(t)) result.Add(t);
            }
        }

        if (activeLangs.Count > 2)
        {
            var tThemes = GetSingleLanguageDefaultThemes(activeLangs[2]);
            foreach (var t in tThemes.Take(2))
            {
                if (seen.Add(t)) result.Add(t);
            }
        }

        if (result.Count < 5)
        {
            foreach (var t in pThemes)
            {
                if (seen.Add(t)) result.Add(t);
            }
        }

        return result;
    }

    public static List<string> GetDefaultThemes(string languageCode = "pt-PT")
        => GetDefaultThemes(new[] { languageCode });

    private static List<string> GetSingleLanguageDefaultThemes(SearchLanguageOption lang)
    {
        if (lang.Code == "pt-PT")
        {
            return new List<string>
            {
                "Panda e os Caricas",
                "Porquinha Peppa",
                "Patrulha Pata",
                "Ruca",
                "Masha e o Urso",
                "Pocoyo",
                "Super Wings",
                "Bob o Construtor",
                "Canções da Maria",
                "Ovelha Choné"
            };
        }
        if (lang.Code == "pt-BR")
        {
            return new List<string>
            {
                "Galinha Pintadinha",
                "Mundo Bita",
                "Turma da Mônica",
                "Patrulha Canina",
                "Peppa Pig",
                "Pocoyo",
                "Bob Zoom",
                "Masha e o Urso"
            };
        }
        if (lang.Code == "en-GB")
        {
            return new List<string>
            {
                "Peppa Pig",
                "CBeebies",
                "Thomas and Friends",
                "Bluey",
                "Paddington Bear",
                "Hey Duggee",
                "Nursery Rhymes"
            };
        }
        if (lang.Code == "en-US")
        {
            return new List<string>
            {
                "Cocomelon",
                "Sesame Street",
                "Paw Patrol",
                "PBS Kids",
                "Super Simple Songs",
                "Bluey",
                "Daniel Tiger"
            };
        }
        if (lang.Code == "es-ES")
        {
            return new List<string>
            {
                "Pocoyo",
                "Cantajuego",
                "Peppa Pig",
                "Patrulla Canina",
                "Clan TVE",
                "Masha y el Oso"
            };
        }
        if (lang.Code == "es-MX")
        {
            return new List<string>
            {
                "La Granja de Zenón",
                "El Reino Infantil",
                "Peppa Pig",
                "Plaza Sésamo",
                "Canciones del Zoo",
                "Patrulla Canina"
            };
        }
        if (lang.Code == "es-AR")
        {
            return new List<string>
            {
                "La Granja de Zenón",
                "Pakapaka",
                "El Reino Infantil",
                "Canciones del Zoo",
                "Peppa Pig"
            };
        }
        if (lang.Code == "fr-FR")
        {
            return new List<string>
            {
                "Comptines pour enfants",
                "Trotro",
                "Peppa Pig Français",
                "Simon Superlapin",
                "Sam le Pompier"
            };
        }
        if (lang.Code == "de-DE")
        {
            return new List<string>
            {
                "Die Sendung mit der Maus",
                "KiKA",
                "Peppa Wutz",
                "Paw Patrol Deutsch",
                "Kinderlieder"
            };
        }
        if (lang.Code == "it-IT")
        {
            return new List<string>
            {
                "Canzoni per bambini",
                "Pimpa",
                "Peppa Pig Italiano",
                "Rai Yoyo",
                "Masha e Orso"
            };
        }
        return new List<string>
        {
            "Cocomelon",
            "Peppa Pig",
            "Paw Patrol",
            "Bluey",
            "Nursery Rhymes",
            "Disney Kids"
        };
    }

    public static async Task<List<YoutubeSearchResult>> SearchByThemeAsync(string theme, IReadOnlyList<string>? languageCodes, int page = 1, int maxResults = 30)
    {
        if (string.IsNullOrWhiteSpace(theme)) return new List<YoutubeSearchResult>();

        var primaryResults = await SearchPageAsync(theme, languageCodes, page, maxResults);
        
        if (primaryResults.Count < 15)
        {
            var fallbackResults = await SearchInternalAsync(theme, "all", maxResults);
            var seenIds = primaryResults.Select(p => p.Id).ToHashSet();
            foreach (var fb in fallbackResults)
            {
                if (seenIds.Add(fb.Id))
                {
                    primaryResults.Add(fb);
                    if (primaryResults.Count >= maxResults) break;
                }
            }
        }

        return primaryResults;
    }

    public static Task<List<YoutubeSearchResult>> SearchByThemeAsync(string theme, string languageCode = "pt-PT", int page = 1, int maxResults = 30)
        => SearchByThemeAsync(theme, new[] { languageCode }, page, maxResults);

    public static async Task<List<YoutubeSearchResult>> SearchAsync(string query, IReadOnlyList<string>? languageCodes, int maxResults = 30)
    {
        return await SearchPageAsync(query, languageCodes, page: 1, maxResults: maxResults);
    }

    public static Task<List<YoutubeSearchResult>> SearchAsync(string query, string languageCode = "pt-PT", int maxResults = 30)
        => SearchAsync(query, new[] { languageCode }, maxResults);

    public static async Task<List<YoutubeSearchResult>> SearchPageAsync(string query, IReadOnlyList<string>? languageCodes, int page = 1, int maxResults = 25)
    {
        var activeLangs = GetActiveLanguages(languageCodes);
        if (activeLangs.Count == 1)
        {
            return await SearchPageSingleLangAsync(query, activeLangs[0], page, maxResults);
        }

        var combined = new List<YoutubeSearchResult>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1º Idioma (Prioridade Máxima +100)
        var lang1Results = await SearchPageSingleLangAsync(query, activeLangs[0], page, maxResults);
        foreach (var r in lang1Results)
        {
            if (seenIds.Add(r.Id))
            {
                combined.Add(r with { RelevanceScore = r.RelevanceScore + 100 });
            }
        }

        // 2º Idioma (Prioridade Secundária +50)
        if (activeLangs.Count > 1)
        {
            var lang2Results = await SearchPageSingleLangAsync(query, activeLangs[1], page, maxResults);
            foreach (var r in lang2Results)
            {
                if (seenIds.Add(r.Id))
                {
                    combined.Add(r with { RelevanceScore = r.RelevanceScore + 50 });
                }
            }
        }

        // 3º Idioma (Prioridade Terciária +20)
        if (activeLangs.Count > 2)
        {
            var lang3Results = await SearchPageSingleLangAsync(query, activeLangs[2], page, maxResults);
            foreach (var r in lang3Results)
            {
                if (seenIds.Add(r.Id))
                {
                    combined.Add(r with { RelevanceScore = r.RelevanceScore + 20 });
                }
            }
        }

        return combined
            .OrderByDescending(r => r.RelevanceScore)
            .Take(maxResults)
            .ToList();
    }

    public static Task<List<YoutubeSearchResult>> SearchPageAsync(string query, string languageCode = "pt-PT", int page = 1, int maxResults = 25)
        => SearchPageAsync(query, new[] { languageCode }, page, maxResults);

    private static async Task<List<YoutubeSearchResult>> SearchPageSingleLangAsync(string query, SearchLanguageOption lang, int page = 1, int maxResults = 25)
    {
        if (page <= 1)
        {
            return await SearchInternalAsync(query, lang.Code, maxResults);
        }

        string[] suffixes;
        if (lang.Code == "pt-PT")
        {
            suffixes = new[] 
            { 
                " dobrado português de portugal", 
                " desenhos animados portugal", 
                " filmes dobrados pt-pt", 
                " episódios completos portugal", 
                " canal panda portugal", 
                " rtp zig zag portugal", 
                " canções infantis portugal" 
            };
        }
        else
        {
            suffixes = new[] { " episódios", " desenhos", " filmes", " completo", " temporada", " novos episódios", " canções" };
        }
        var suffixIndex = (page - 2) % suffixes.Length;
        var modifiedQuery = $"{query.Trim()}{suffixes[suffixIndex]}";

        return await SearchInternalAsync(modifiedQuery, lang.Code, maxResults);
    }

    private static async Task<List<YoutubeSearchResult>> SearchInternalAsync(string query, string languageCode = "pt-PT", int maxResults = 30)
    {
        var rawResults = new List<YoutubeSearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return rawResults;

        var cleanQuery = query.Trim();
        var cacheKey = $"{languageCode.Trim().ToLowerInvariant()}::{cleanQuery.ToLowerInvariant()}::{maxResults}";
        if (_searchCache.TryGetValue(cacheKey, out var cachedEntry))
        {
            if ((DateTime.UtcNow - cachedEntry.CachedAt).TotalHours < 2 && cachedEntry.Results.Count > 0)
            {
                return cachedEntry.Results;
            }
        }

        var lang = GetLanguage(languageCode);
        var effectiveQuery = BuildLocalizedQuery(cleanQuery, lang);

        // =========================================================================
        // 1. YOUTUBE INNERTUBE API (Ultra-rápido, fiável, sem bloqueios de consent)
        // =========================================================================
        try
        {
            var reqBody = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = "2.20240101.00.00",
                        hl = lang.Hl,
                        gl = lang.Gl
                    }
                },
                query = effectiveQuery
            };

            var postJson = JsonSerializer.Serialize(reqBody);
            using var content = new StringContent(postJson, System.Text.Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync("https://www.youtube.com/youtubei/v1/search", content);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                ExtractVideosFromJson(doc.RootElement, rawResults, maxResults * 4);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[yChicaTube.Search] Innertube API error: {ex.Message}");
        }

        // =========================================================================
        // 2. FALLBACK WEB SCRAPING COM COOKIE DE CONSENTIMENTO
        // =========================================================================
        if (rawResults.Count == 0)
        {
            try
            {
                var url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(effectiveQuery)}&hl={lang.Hl}&gl={lang.Gl}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Accept-Language", $"{lang.Hl},{lang.Gl.ToLower()};q=0.9,en;q=0.8");
                request.Headers.Add("Cookie", "SOCS=CAESEwgDEgk1NDM5MjM5NDcaAnB0IAEaBgiA_LyaBg;");

                var response = await HttpClient.SendAsync(request);
                var html = await response.Content.ReadAsStringAsync();

                // Procura o bloco JSON ytInitialData
                var match = Regex.Match(html, @"(?:var ytInitialData|window\[""ytInitialData""\]|ytInitialData)\s*=\s*(\{.*?\});", RegexOptions.Singleline);

                if (match.Success)
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value);
                    ExtractVideosFromJson(doc.RootElement, rawResults, maxResults * 4);
                }

                // Fallback com regex caso a estrutura JSON tenha mudado
                if (rawResults.Count == 0)
                {
                    var videoMatches = Regex.Matches(html, @"/watch\?v=([A-Za-z0-9_-]{11})");
                    var seen = new HashSet<string>();
                    foreach (Match m in videoMatches)
                    {
                        var id = m.Groups[1].Value;
                        if (seen.Add(id))
                        {
                            rawResults.Add(new YoutubeSearchResult(id, $"Vídeo YouTube ({id})", $"https://i.ytimg.com/vi/{id}/mqdefault.jpg"));
                            if (rawResults.Count >= maxResults * 2) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[yChicaTube.Search] Web scrape error: {ex.Message}");
            }
        }

        // =========================================================================
        // 3. FILTRAGEM INTELIGENTE LÉXICA, CANAIS E PONTUAÇÃO DE RELEVÂNCIA
        // =========================================================================
        var validResults = new List<YoutubeSearchResult>();

        foreach (var r in rawResults)
        {
            var combinedText = $"{r.Title} {r.ChannelTitle} {r.DescriptionSnippet}".ToLowerInvariant();

            // 1. REJEIÇÃO TOTAL: Se contiver palavras excluídas explicitamente
            var isExcluded = lang.ExcludeKeywords.Any(ex => combinedText.Contains(ex, StringComparison.OrdinalIgnoreCase));
            if (isExcluded)
            {
                continue;
            }

            var score = 0;

            // 2. VERIFICAÇÃO DE CANAIS OFICIAIS (WHITELIST)
            if (lang.Code == "pt-PT")
            {
                var isVerifiedChannel = lang.VerifiedChannels.Any(c => 
                    r.ChannelTitle.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                    combinedText.Contains(c, StringComparison.OrdinalIgnoreCase));

                if (isVerifiedChannel)
                {
                    score += 100;
                }

                // 3. BOOST POR PALAVRAS-CHAVE EXPLÍCITAS DE PORTUGAL
                foreach (var boost in lang.BoostKeywords)
                {
                    if (combinedText.Contains(boost, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 25;
                    }
                }
            }
            else
            {
                foreach (var boost in lang.BoostKeywords)
                {
                    if (combinedText.Contains(boost, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 15;
                    }
                }
            }

            validResults.Add(r with { RelevanceScore = score });
        }

        // Retorna os vídeos válidos ordenados por relevância e pureza linguística
        var finalResults = validResults
            .OrderByDescending(r => r.RelevanceScore)
            .Take(maxResults)
            .ToList();

        if (finalResults.Count > 0)
        {
            _searchCache[cacheKey] = (DateTime.UtcNow, finalResults);
        }

        return finalResults;
    }

    private static string BuildLocalizedQuery(string userQuery, SearchLanguageOption lang)
    {
        if (lang.Code == "all" || string.IsNullOrEmpty(lang.QuerySuffix))
            return userQuery;

        var clean = userQuery.Trim();

        if (lang.Code == "pt-PT")
        {
            // Tradução / Substituição automática de termos brasileiros clássicos para o seu equivalente em Portugal
            clean = Regex.Replace(clean, @"\bpatrulha\s+canina\b", "patrulha pata", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bcaillou\b", "ruca", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bpeppa\s+pig\b", "porquinha peppa", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bshaun\s+o\s+carneiro\b", "ovelha choné", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bturma\s+da\s+m[oô]nica\b", "panda e os caricas", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bgalinha\s+pintadinha\b", "canções infantis portugal", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bmundo\s+bita\b", "panda e os caricas", RegexOptions.IgnoreCase);

            var lower = clean.ToLowerInvariant();
            var hasExplicitPt = lower.Contains("portugal") || lower.Contains("pt-pt") || lower.Contains("pt pt") || lower.Contains("dobrado") || lower.Contains("canal panda");
            
            return hasExplicitPt ? clean : $"{clean} portugal";
        }

        if (lang.Code == "pt-BR")
        {
            var lower = clean.ToLowerInvariant();
            if (lower.Contains("brasil") || lower.Contains("pt-br") || lower.Contains("dublado"))
                return clean;
            return $"{clean}{lang.QuerySuffix}";
        }

        if (lang.Code.StartsWith("en"))
        {
            var lower = clean.ToLowerInvariant();
            if (lower.Contains("english") || lower.Contains("kids"))
                return clean;
            return $"{clean}{lang.QuerySuffix}";
        }

        if (lang.Code.StartsWith("es"))
        {
            var lower = clean.ToLowerInvariant();
            if (lower.Contains("español") || lower.Contains("castellano") || lower.Contains("latino"))
                return clean;
            return $"{clean}{lang.QuerySuffix}";
        }

        if (lang.Code.StartsWith("fr"))
        {
            var lower = clean.ToLowerInvariant();
            if (lower.Contains("français") || lower.Contains("enfants"))
                return clean;
            return $"{clean}{lang.QuerySuffix}";
        }

        if (lang.Code.StartsWith("de"))
        {
            var lower = clean.ToLowerInvariant();
            if (lower.Contains("deutsch") || lower.Contains("kinder"))
                return clean;
            return $"{clean}{lang.QuerySuffix}";
        }

        if (lang.Code.StartsWith("it"))
        {
            var lower = clean.ToLowerInvariant();
            if (lower.Contains("italiano") || lower.Contains("bambini"))
                return clean;
            return $"{clean}{lang.QuerySuffix}";
        }

        return $"{clean}{lang.QuerySuffix}";
    }

    private static void ExtractVideosFromJson(JsonElement element, List<YoutubeSearchResult> results, int maxResults)
    {
        if (results.Count >= maxResults) return;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("videoRenderer", out var videoRenderer))
            {
                if (videoRenderer.TryGetProperty("videoId", out var idProp))
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id) && Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") && results.All(r => r.Id != id))
                    {
                        var title = "História do YouTube";
                        if (videoRenderer.TryGetProperty("title", out var titleObj))
                        {
                            if (titleObj.TryGetProperty("runs", out var runs) && runs.GetArrayLength() > 0)
                            {
                                title = runs[0].GetProperty("text").GetString() ?? title;
                            }
                            else if (titleObj.TryGetProperty("simpleText", out var simpleText))
                            {
                                title = simpleText.GetString() ?? title;
                            }
                        }

                        var channelTitle = "";
                        if (videoRenderer.TryGetProperty("ownerText", out var ownerTextObj) && ownerTextObj.TryGetProperty("runs", out var ownerRuns) && ownerRuns.GetArrayLength() > 0)
                        {
                            channelTitle = ownerRuns[0].GetProperty("text").GetString() ?? "";
                        }
                        else if (videoRenderer.TryGetProperty("shortBylineText", out var shortBylineObj) && shortBylineObj.TryGetProperty("runs", out var bylineRuns) && bylineRuns.GetArrayLength() > 0)
                        {
                            channelTitle = bylineRuns[0].GetProperty("text").GetString() ?? "";
                        }
                        else if (videoRenderer.TryGetProperty("longBylineText", out var longBylineObj) && longBylineObj.TryGetProperty("runs", out var longRuns) && longRuns.GetArrayLength() > 0)
                        {
                            channelTitle = longRuns[0].GetProperty("text").GetString() ?? "";
                        }

                        var desc = "";
                        if (videoRenderer.TryGetProperty("detailedMetadataSnippets", out var metaSnippets) && metaSnippets.ValueKind == JsonValueKind.Array && metaSnippets.GetArrayLength() > 0)
                        {
                            var firstSnippet = metaSnippets[0];
                            if (firstSnippet.TryGetProperty("snippetText", out var snippetText) && snippetText.TryGetProperty("runs", out var sRuns))
                            {
                                desc = string.Concat(sRuns.EnumerateArray().Select(r => r.TryGetProperty("text", out var t) ? t.GetString() ?? "" : ""));
                            }
                        }
                        else if (videoRenderer.TryGetProperty("descriptionSnippet", out var descSnippet) && descSnippet.TryGetProperty("runs", out var dRuns))
                        {
                            desc = string.Concat(dRuns.EnumerateArray().Select(r => r.TryGetProperty("text", out var t) ? t.GetString() ?? "" : ""));
                        }

                        var thumb = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";
                        results.Add(new YoutubeSearchResult(id, title, thumb, 0, channelTitle, desc));
                        if (results.Count >= maxResults) return;
                    }
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                ExtractVideosFromJson(prop.Value, results, maxResults);
                if (results.Count >= maxResults) return;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ExtractVideosFromJson(item, results, maxResults);
                if (results.Count >= maxResults) return;
            }
        }
    }

    // =========================================================================
    // PESQUISA E GESTÃO DE PLAYLISTS & COLEÇÕES REAIS
    // =========================================================================

    public static async Task<List<YoutubePlaylistResult>> SearchPlaylistsAsync(string query, IReadOnlyList<string>? languageCodes, int maxResults = 12)
    {
        var activeLangs = GetActiveLanguages(languageCodes);
        var cleanQuery = query?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            return GetCuratedStarterPlaylists(activeLangs[0].Code);
        }

        var results = new List<YoutubePlaylistResult>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lang in activeLangs)
        {
            var pList = await SearchPlaylistsSingleLangAsync(cleanQuery, lang, maxResults);
            foreach (var p in pList)
            {
                if (seenIds.Add(p.Id))
                {
                    results.Add(p);
                    if (results.Count >= maxResults) break;
                }
            }
            if (results.Count >= maxResults) break;
        }

        return results;
    }

    public static Task<List<YoutubePlaylistResult>> SearchPlaylistsAsync(string query, string languageCode = "pt-PT", int maxResults = 12)
        => SearchPlaylistsAsync(query, new[] { languageCode }, maxResults);

    private static async Task<List<YoutubePlaylistResult>> SearchPlaylistsSingleLangAsync(string cleanQuery, SearchLanguageOption lang, int maxResults = 12)
    {
        var rawPlaylists = new List<YoutubePlaylistResult>();
        var effectiveQuery = BuildLocalizedQuery(cleanQuery, lang);

        // 1. Pesquisa de Playlists via Innertube API com filtro de Playlists ("params": "EgIQAw%3D%3D")
        try
        {
            var reqBody = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = "2.20240101.00.00",
                        hl = lang.Hl,
                        gl = lang.Gl
                    }
                },
                query = effectiveQuery,
                @params = "EgIQAw%3D%3D"
            };

            var postJson = JsonSerializer.Serialize(reqBody);
            using var content = new StringContent(postJson, System.Text.Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync("https://www.youtube.com/youtubei/v1/search", content);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                ExtractPlaylistsFromJson(doc.RootElement, rawPlaylists, maxResults * 2);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[yChicaTube.Playlists] Innertube error: {ex.Message}");
        }

        // 2. Fallback Web Scraping de Playlists
        if (rawPlaylists.Count == 0)
        {
            try
            {
                var url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(effectiveQuery)}&sp=EgIQAw%253D%253D&hl={lang.Hl}&gl={lang.Gl}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Accept-Language", $"{lang.Hl},{lang.Gl.ToLower()};q=0.9,en;q=0.8");
                request.Headers.Add("Cookie", "SOCS=CAESEwgDEgk1NDM5MjM5NDcaAnB0IAEaBgiA_LyaBg;");

                var response = await HttpClient.SendAsync(request);
                var html = await response.Content.ReadAsStringAsync();

                var match = Regex.Match(html, @"(?:var ytInitialData|window\[""ytInitialData""\]|ytInitialData)\s*=\s*(\{.*?\});", RegexOptions.Singleline);
                if (match.Success)
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value);
                    ExtractPlaylistsFromJson(doc.RootElement, rawPlaylists, maxResults * 2);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[yChicaTube.Playlists] Scrape error: {ex.Message}");
            }
        }

        // 3. Se ainda não houver playlists do YouTube suficientes, cria coleção baseada nos vídeos da pesquisa do termo
        if (rawPlaylists.Count == 0)
        {
            var fallbackVideos = await SearchByThemeAsync(cleanQuery, lang.Code, maxResults: 25);
            if (fallbackVideos.Count > 0)
            {
                var firstThumb = fallbackVideos[0].ThumbnailUrl;
                rawPlaylists.Add(new YoutubePlaylistResult(
                    $"collection_{Uri.EscapeDataString(cleanQuery)}",
                    $"Coleção {cleanQuery}",
                    firstThumb,
                    fallbackVideos.Count,
                    fallbackVideos[0].ChannelTitle,
                    fallbackVideos
                ));
            }
        }

        // 4. Filtragem léxica regional (excluindo sotaques errados)
        var filteredPlaylists = new List<YoutubePlaylistResult>();
        foreach (var p in rawPlaylists)
        {
            var text = $"{p.Title} {p.ChannelTitle}".ToLowerInvariant();
            if (lang.ExcludeKeywords.Any(ex => text.Contains(ex, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            filteredPlaylists.Add(p);
        }

        if (filteredPlaylists.Count == 0 && rawPlaylists.Count > 0)
        {
            filteredPlaylists = rawPlaylists;
        }

        return filteredPlaylists.Take(maxResults).ToList();
    }

    public static async Task<List<YoutubeSearchResult>> GetPlaylistVideosAsync(string playlistId, string playlistTitle, string languageCode = "pt-PT", int maxVideos = 35)
    {
        var results = new List<YoutubeSearchResult>();
        if (string.IsNullOrWhiteSpace(playlistId)) return results;

        var lang = GetLanguage(languageCode);

        // Se for uma coleção gerada localmente
        if (playlistId.StartsWith("collection_") || playlistId.StartsWith("curated_"))
        {
            var term = string.IsNullOrWhiteSpace(playlistTitle) ? playlistId.Replace("collection_", "").Replace("curated_", "") : playlistTitle;
            return await SearchByThemeAsync(term, languageCode, maxResults: maxVideos);
        }

        // 1. Tenta carregar os vídeos da playlist via Innertube Browse API
        try
        {
            var browseId = playlistId.StartsWith("VL", StringComparison.OrdinalIgnoreCase) ? playlistId : $"VL{playlistId}";
            var reqBody = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = "2.20240101.00.00",
                        hl = lang.Hl,
                        gl = lang.Gl
                    }
                },
                browseId = browseId
            };

            var postJson = JsonSerializer.Serialize(reqBody);
            using var content = new StringContent(postJson, System.Text.Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync("https://www.youtube.com/youtubei/v1/browse", content);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                ExtractVideosFromJson(doc.RootElement, results, maxVideos * 2);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[yChicaTube.GetPlaylistVideos] Innertube browse error: {ex.Message}");
        }

        // 2. Fallback Web Scraping da página da Playlist
        if (results.Count == 0)
        {
            try
            {
                var url = $"https://www.youtube.com/playlist?list={Uri.EscapeDataString(playlistId)}&hl={lang.Hl}&gl={lang.Gl}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Accept-Language", $"{lang.Hl},{lang.Gl.ToLower()};q=0.9,en;q=0.8");
                request.Headers.Add("Cookie", "SOCS=CAESEwgDEgk1NDM5MjM5NDcaAnB0IAEaBgiA_LyaBg;");

                var response = await HttpClient.SendAsync(request);
                var html = await response.Content.ReadAsStringAsync();

                var match = Regex.Match(html, @"(?:var ytInitialData|window\[""ytInitialData""\]|ytInitialData)\s*=\s*(\{.*?\});", RegexOptions.Singleline);
                if (match.Success)
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value);
                    ExtractVideosFromJson(doc.RootElement, results, maxVideos * 2);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[yChicaTube.GetPlaylistVideos] Scrape error: {ex.Message}");
            }
        }

        // 3. Se a playlist estiver vazia no YouTube, faz fallback inteligente para o tema da playlist
        if (results.Count == 0)
        {
            var cleanTitle = Regex.Replace(playlistTitle ?? "", @"(?i)\b(playlist|coleção|episódios)\b", "").Trim();
            if (!string.IsNullOrWhiteSpace(cleanTitle))
            {
                results = await SearchByThemeAsync(cleanTitle, languageCode, maxResults: maxVideos);
            }
        }

        return results.Take(maxVideos).ToList();
    }

    public static List<YoutubePlaylistResult> GetCuratedStarterPlaylists(string languageCode = "pt-PT")
    {
        var lang = GetLanguage(languageCode);
        if (lang.Code == "pt-PT")
        {
            return new List<YoutubePlaylistResult>
            {
                new("curated_panda", "Panda e os Caricas - Vídeos e Canções", "https://i.ytimg.com/vi/0k6gIqY-qjM/mqdefault.jpg", 30, "Canal Panda Oficial"),
                new("curated_ruca", "Ruca em Português de Portugal - Episódios", "https://i.ytimg.com/vi/e0E2r8X1lQ4/mqdefault.jpg", 25, "Ruca Oficial"),
                new("curated_peppa", "Porquinha Peppa PT-PT - Histórias Completas", "https://i.ytimg.com/vi/9j8K2L3M4N5/mqdefault.jpg", 28, "Peppa Pig Português de Portugal"),
                new("curated_patrulha", "Patrulha Pata em Português de Portugal", "https://i.ytimg.com/vi/4a5b6c7d8e9/mqdefault.jpg", 26, "Patrulha Pata Oficial"),
                new("curated_pocoyo", "Pocoyo em Português de Portugal", "https://i.ytimg.com/vi/1a2b3c4d5e6/mqdefault.jpg", 22, "Pocoyo Português"),
                new("curated_masha", "Masha e o Urso PT-PT", "https://i.ytimg.com/vi/3f4g5h6j7k8/mqdefault.jpg", 24, "Masha Portugal"),
                new("curated_superwings", "Super Wings em Português de Portugal", "https://i.ytimg.com/vi/5l6m7n8p9q0/mqdefault.jpg", 20, "Super Wings Portugal"),
                new("curated_cancoes_maria", "As Canções da Maria - Músicas Infantis", "https://i.ytimg.com/vi/7r8s9t0u1v2/mqdefault.jpg", 18, "Canções da Maria")
            };
        }
        if (lang.Code == "pt-BR")
        {
            return new List<YoutubePlaylistResult>
            {
                new("curated_galinha", "Galinha Pintadinha - Clássicos Infantis", "https://i.ytimg.com/vi/Galinha1234/mqdefault.jpg", 30, "Galinha Pintadinha"),
                new("curated_bita", "Mundo Bita - Canções Educativas", "https://i.ytimg.com/vi/MundoBita12/mqdefault.jpg", 25, "Mundo Bita Oficial"),
                new("curated_monica", "Turma da Mônica - Desenhos Animados", "https://i.ytimg.com/vi/Monica12345/mqdefault.jpg", 28, "Turma da Mônica"),
                new("curated_patrulhacanina", "Patrulha Canina Brasil", "https://i.ytimg.com/vi/PatrulhaBR1/mqdefault.jpg", 24, "Patrulha Canina Oficial")
            };
        }
        if (lang.Code.StartsWith("es"))
        {
            return new List<YoutubePlaylistResult>
            {
                new("curated_pocoyo_es", "Pocoyo en Español - Temporada Completa", "https://i.ytimg.com/vi/PocoyoES123/mqdefault.jpg", 30, "Pocoyo en Español"),
                new("curated_cantajuego", "Cantajuego - Grandes Éxitos", "https://i.ytimg.com/vi/Cantajuego1/mqdefault.jpg", 25, "Cantajuego Oficial"),
                new("curated_lagranja", "La Granja de Zenón - Canciones de la Granja", "https://i.ytimg.com/vi/LaGranja123/mqdefault.jpg", 28, "El Reino Infantil"),
                new("curated_peppa_es", "Peppa Pig en Español", "https://i.ytimg.com/vi/PeppaES1234/mqdefault.jpg", 26, "Peppa Pig Español")
            };
        }
        return new List<YoutubePlaylistResult>
        {
            new("curated_cbeebies", "CBeebies Stories & Adventures", "https://i.ytimg.com/vi/CBeebies123/mqdefault.jpg", 30, "CBeebies"),
            new("curated_peppa_en", "Peppa Pig Official Episodes", "https://i.ytimg.com/vi/PeppaEN1234/mqdefault.jpg", 28, "Peppa Pig Official"),
            new("curated_bluey", "Bluey Full Episodes & Clips", "https://i.ytimg.com/vi/Bluey123456/mqdefault.jpg", 25, "Bluey Official Channel"),
            new("curated_cocomelon", "CoComelon Nursery Rhymes", "https://i.ytimg.com/vi/Cocomelon12/mqdefault.jpg", 30, "CoComelon")
        };
    }

    private static void ExtractPlaylistsFromJson(JsonElement element, List<YoutubePlaylistResult> playlists, int maxResults)
    {
        if (playlists.Count >= maxResults) return;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("playlistRenderer", out var playlistRenderer))
            {
                if (playlistRenderer.TryGetProperty("playlistId", out var idProp))
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id) && playlists.All(p => p.Id != id))
                    {
                        var title = "Playlist do YouTube";
                        if (playlistRenderer.TryGetProperty("title", out var titleObj))
                        {
                            if (titleObj.TryGetProperty("simpleText", out var st))
                            {
                                title = st.GetString() ?? title;
                            }
                            else if (titleObj.TryGetProperty("runs", out var runs) && runs.GetArrayLength() > 0)
                            {
                                title = runs[0].GetProperty("text").GetString() ?? title;
                            }
                        }

                        var channelTitle = "";
                        if (playlistRenderer.TryGetProperty("shortBylineText", out var sbl) && sbl.TryGetProperty("runs", out var blRuns) && blRuns.GetArrayLength() > 0)
                        {
                            channelTitle = blRuns[0].GetProperty("text").GetString() ?? "";
                        }
                        else if (playlistRenderer.TryGetProperty("longBylineText", out var lbl) && lbl.TryGetProperty("runs", out var lblRuns) && lblRuns.GetArrayLength() > 0)
                        {
                            channelTitle = lblRuns[0].GetProperty("text").GetString() ?? "";
                        }

                        int videoCount = 20;
                        if (playlistRenderer.TryGetProperty("videoCount", out var vcProp))
                        {
                            if (int.TryParse(vcProp.GetString(), out var count)) videoCount = count;
                        }
                        else if (playlistRenderer.TryGetProperty("videoCountText", out var vctProp))
                        {
                            var text = vctProp.TryGetProperty("simpleText", out var st2) ? st2.GetString() : "";
                            var digits = Regex.Match(text ?? "", @"\d+").Value;
                            if (int.TryParse(digits, out var count2)) videoCount = count2;
                        }

                        var thumb = "";
                        if (playlistRenderer.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array && thumbs.GetArrayLength() > 0)
                        {
                            var lastThumb = thumbs[thumbs.GetArrayLength() - 1];
                            if (lastThumb.TryGetProperty("thumbnails", out var nestedThumbs) && nestedThumbs.ValueKind == JsonValueKind.Array && nestedThumbs.GetArrayLength() > 0)
                            {
                                thumb = nestedThumbs[nestedThumbs.GetArrayLength() - 1].GetProperty("url").GetString() ?? "";
                            }
                            else if (lastThumb.TryGetProperty("url", out var urlProp))
                            {
                                thumb = urlProp.GetString() ?? "";
                            }
                        }

                        if (string.IsNullOrEmpty(thumb))
                        {
                            thumb = "https://i.ytimg.com/vi/0k6gIqY-qjM/mqdefault.jpg";
                        }

                        playlists.Add(new YoutubePlaylistResult(id, title, thumb, videoCount, channelTitle));
                        if (playlists.Count >= maxResults) return;
                    }
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                ExtractPlaylistsFromJson(prop.Value, playlists, maxResults);
                if (playlists.Count >= maxResults) return;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ExtractPlaylistsFromJson(item, playlists, maxResults);
                if (playlists.Count >= maxResults) return;
            }
        }
    }
}

