using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace grzyClothTool.Localization;

/// <summary>
/// The three languages that ship with the application. Kept as an enum for backwards compatibility
/// with the saved setting and with code that only ever needs English or Japanese; every other
/// language is addressed by its culture code (see <see cref="LocalizationManager.SetLanguage(string)"/>).
/// </summary>
public enum AppLanguage
{
    /// <summary>Follow the language of the operating system.</summary>
    Auto,
    English,
    Japanese
}

/// <summary>
/// Central access point for localized strings.
/// Exposes an indexer so XAML can bind to it through the <see cref="LocExtension"/> markup extension,
/// which makes it possible to switch the language without restarting the application.
///
/// The strings themselves live in flat JSON catalogs (<c>Localization\en.json</c>, <c>Localization\ja.json</c>, ...).
/// A catalog next to the executable wins, so the wording can be corrected without rebuilding;
/// when the folder is missing the identical catalog embedded in the executable is used instead.
/// </summary>
public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    /// <summary>Folder next to the executable that holds the editable JSON catalogs.</summary>
    public const string LocalizationFolderName = "Localization";

    /// <summary>Pseudo code of the "follow the operating system" option.</summary>
    public const string AutoCode = "auto";

    /// <summary>Culture code used when nothing else can be resolved.</summary>
    public const string FallbackCode = "en";

    /// <summary>
    /// Optional key inside a catalog holding the name shown in the language picker,
    /// e.g. <c>"_languageName": "日本語"</c>. Falls back to the culture's native name.
    /// </summary>
    public const string LanguageNameKey = "_languageName";

    /// <summary>
    /// Name of the environment variable that forces a UI language, e.g. GRZYCLOTHTOOL_LANG=en.
    /// </summary>
    public const string LanguageEnvironmentVariable = "GRZYCLOTHTOOL_LANG";

    private const string EmbeddedResourcePrefix = "grzyClothTool.Localization.";
    private const string CatalogExtension = ".json";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _catalogLock = new();

    private string _currentCode = AutoCode;
    private CultureInfo _currentCulture = CultureInfo.GetCultureInfo(FallbackCode);
    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _fallbackStrings = new Dictionary<string, string>();

    private LocalizationManager()
    {
        ApplyCode(AutoCode);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Raised after the language changed, so that code-behind which cached strings can refresh itself.
    /// </summary>
    public event EventHandler LanguageChanged;

    /// <summary>
    /// Selected language as an enum. Languages other than the two that ship with the application
    /// report <see cref="AppLanguage.Auto"/>; use <see cref="CurrentLanguageCode"/> to tell them apart.
    /// </summary>
    public AppLanguage CurrentLanguage => ToAppLanguage(_currentCode);

    /// <summary>Culture code of the selected language, or <see cref="AutoCode"/> when following the OS.</summary>
    public string CurrentLanguageCode => _currentCode;

    public CultureInfo CurrentCulture => _currentCulture;

    /// <summary>
    /// True when the language came from the command line or the environment rather than from the
    /// saved setting. Used by automated UI tests, which need a predictable language.
    /// </summary>
    public bool IsLanguageOverridden { get; private set; }

    /// <summary>
    /// Indexer used by XAML bindings: {loc:Loc Some_Key}
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>. Falls back to English and then to the
    /// key itself, so that a forgotten translation is visible instead of producing an empty label.
    /// </summary>
    public static string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        try
        {
            var instance = Instance;
            if (instance._strings.TryGetValue(key, out var value) && value is not null)
            {
                return value;
            }

            if (instance._fallbackStrings.TryGetValue(key, out var fallback) && fallback is not null)
            {
                return fallback;
            }

            return key;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public void SetLanguage(AppLanguage language) => SetLanguage(ToCode(language));

    /// <summary>
    /// Switches to the catalog for <paramref name="code"/> ("en", "ja", "ko", ... or "auto").
    /// </summary>
    public void SetLanguage(string code)
    {
        ApplyCode(code);

        CultureInfo.DefaultThreadCurrentUICulture = _currentCulture;
        CultureInfo.CurrentUICulture = _currentCulture;

        // "Item[]" tells WPF to re-evaluate every indexer binding, which refreshes the whole UI.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageCode)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyCode(string code)
    {
        var normalized = NormalizeCode(code) ?? AutoCode;
        var effective = normalized == AutoCode ? ResolveSystemCode() : normalized;

        _currentCode = normalized;
        _currentCulture = ToCulture(effective);
        _fallbackStrings = GetCatalog(FallbackCode);
        _strings = GetCatalog(effective);
    }

    /// <summary>
    /// Maps user input ("en", "ja-JP", "English", "auto", ...) onto a catalog code, or null when
    /// no catalog matches. The value is not required to be one of the two bundled languages.
    /// </summary>
    public static string NormalizeCode(string value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "auto":
                return AutoCode;
            case "english":
                return "en";
            case "japanese":
                return "ja";
        }

        var lowered = trimmed.ToLowerInvariant();
        if (CatalogExists(lowered))
        {
            return lowered;
        }

        // "ja-JP" and friends: fall back to the neutral language when only that catalog exists.
        var separator = lowered.IndexOfAny(['-', '_']);
        if (separator > 0)
        {
            var neutral = lowered[..separator];
            if (CatalogExists(neutral))
            {
                return neutral;
            }
        }

        return null;
    }

    private static string ResolveSystemCode()
    {
        try
        {
            var system = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
            return CatalogExists(system) ? system.ToLowerInvariant() : FallbackCode;
        }
        catch (Exception)
        {
            return FallbackCode;
        }
    }

    private static CultureInfo ToCulture(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static string ToCode(AppLanguage language) => language switch
    {
        AppLanguage.English => "en",
        AppLanguage.Japanese => "ja",
        _ => AutoCode
    };

    private static AppLanguage ToAppLanguage(string code) => code switch
    {
        "en" => AppLanguage.English,
        "ja" => AppLanguage.Japanese,
        _ => AppLanguage.Auto
    };

    public static AppLanguage ParseLanguage(string value)
    {
        return Enum.TryParse<AppLanguage>(value, true, out var parsed) ? parsed : AppLanguage.Auto;
    }

    /// <summary>
    /// Applies the language to use at startup. A "--lang" argument wins over the
    /// <see cref="LanguageEnvironmentVariable"/> environment variable, which in turn wins over the
    /// saved setting. An override is never written back, so it does not change the user's settings.
    /// </summary>
    public void ApplyStartupLanguage(string savedLanguageSetting)
    {
        var overridden = GetLanguageCodeOverride();

        IsLanguageOverridden = overridden is not null;
        SetLanguage(overridden ?? NormalizeCode(savedLanguageSetting) ?? AutoCode);
    }

    /// <summary>
    /// Returns the language forced through "--lang en" / "--lang=en" or through the
    /// <see cref="LanguageEnvironmentVariable"/> environment variable, or null when neither is set.
    /// </summary>
    public static AppLanguage? GetLanguageOverride()
    {
        var code = GetLanguageCodeOverride();
        return code is null ? null : ToAppLanguage(code);
    }

    /// <summary>
    /// Same as <see cref="GetLanguageOverride"/> but keeps the culture code, so that languages
    /// beyond the two bundled ones can be forced as well.
    /// </summary>
    public static string GetLanguageCodeOverride()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeCode(args[i]["--lang=".Length..]);
                }

                if (string.Equals(args[i], "--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return NormalizeCode(args[i + 1]);
                }
            }

            return NormalizeCode(Environment.GetEnvironmentVariable(LanguageEnvironmentVariable));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Entries for the language picker in the settings screen: the automatic option followed by
    /// every catalog found next to the executable or embedded in it, so dropping a new
    /// <c>Localization\&lt;code&gt;.json</c> into the folder is enough to offer another language.
    /// The first entry is intentionally bilingual so that a user who picked the wrong language can find their way back.
    /// </summary>
    public static IReadOnlyList<LanguageOption> LanguageOptions => _languageOptions ??= BuildLanguageOptions();

    private static IReadOnlyList<LanguageOption> _languageOptions;

    /// <summary>Forgets the cached catalogs and language list, so newly added files are picked up.</summary>
    public static void Reload()
    {
        lock (_catalogLock)
        {
            _catalogs.Clear();
        }

        _languageOptions = null;
        Instance.SetLanguage(Instance._currentCode);
    }

    private static IReadOnlyList<LanguageOption> BuildLanguageOptions()
    {
        var options = new List<LanguageOption>
        {
            new(AutoCode, "Auto / 自動 (OS)")
        };

        foreach (var code in GetAvailableCodes())
        {
            options.Add(new LanguageOption(code, GetDisplayName(code)));
        }

        return options;
    }

    private static string GetDisplayName(string code)
    {
        var catalog = GetCatalog(code);
        if (catalog.TryGetValue(LanguageNameKey, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        try
        {
            return CultureInfo.GetCultureInfo(code).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>
    /// Culture codes of every catalog that can be loaded, external files first, sorted so that the
    /// list in the settings screen is stable.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableCodes()
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var folder = GetLocalizationFolder();
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*" + CatalogExtension))
                {
                    codes.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
        }
        catch (Exception)
        {
            // an unreadable folder must not take the language picker down with it
        }

        foreach (var resource in typeof(LocalizationManager).Assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(EmbeddedResourcePrefix, StringComparison.Ordinal)
                && resource.EndsWith(CatalogExtension, StringComparison.OrdinalIgnoreCase))
            {
                codes.Add(resource[EmbeddedResourcePrefix.Length..^CatalogExtension.Length]);
            }
        }

        return [.. codes];
    }

    /// <summary>Absolute path of the folder the editable catalogs are read from.</summary>
    public static string GetLocalizationFolder()
        => Path.Combine(AppContext.BaseDirectory, LocalizationFolderName);

    private static bool CatalogExists(string code)
        => !string.IsNullOrEmpty(code) && GetCatalog(code).Count > 0;

    private static IReadOnlyDictionary<string, string> GetCatalog(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return new Dictionary<string, string>();
        }

        lock (_catalogLock)
        {
            if (_catalogs.TryGetValue(code, out var cached))
            {
                return cached;
            }

            var catalog = LoadCatalog(code);
            _catalogs[code] = catalog;
            return catalog;
        }
    }

    /// <summary>
    /// Loads a catalog, preferring the editable file next to the executable and falling back to the
    /// identical copy embedded in the assembly, so the application still works when the folder is gone.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadCatalog(string code)
    {
        try
        {
            var file = Path.Combine(GetLocalizationFolder(), code + CatalogExtension);
            if (File.Exists(file))
            {
                using var stream = File.OpenRead(file);
                var parsed = Parse(stream);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
        }
        catch (Exception)
        {
            // a broken or half-written file falls through to the embedded copy
        }

        try
        {
            var assembly = typeof(LocalizationManager).Assembly;
            var resourceName = ResolveEmbeddedName(assembly, code);
            if (resourceName is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is not null)
                {
                    return Parse(stream);
                }
            }
        }
        catch (Exception)
        {
            // fall through to the empty catalog
        }

        return new Dictionary<string, string>();
    }

    private static string ResolveEmbeddedName(Assembly assembly, string code)
    {
        var expected = EmbeddedResourcePrefix + code + CatalogExtension;
        return assembly.GetManifestResourceNames()
            .FirstOrDefault(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, string> Parse(Stream stream)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[property.Name] = property.Value.GetString();
            }
        }

        return result;
    }
}

public class LanguageOption(string code, string displayName)
{
    public LanguageOption(AppLanguage language, string displayName)
        : this(language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Japanese => "ja",
            _ => LocalizationManager.AutoCode
        }, displayName)
    {
    }

    /// <summary>Culture code of the catalog, or <see cref="LocalizationManager.AutoCode"/>.</summary>
    public string Code { get; } = code;

    public AppLanguage Language => Code switch
    {
        "en" => AppLanguage.English,
        "ja" => AppLanguage.Japanese,
        _ => AppLanguage.Auto
    };

    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;
}
