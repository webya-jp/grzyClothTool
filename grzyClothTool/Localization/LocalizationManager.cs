using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace grzyClothTool.Localization;

/// <summary>
/// Language that the user selected in the settings screen.
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
/// </summary>
public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    private static readonly ResourceManager _resourceManager =
        new("grzyClothTool.Localization.Strings", typeof(LocalizationManager).Assembly);

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja");

    private AppLanguage _currentLanguage = AppLanguage.Auto;
    private CultureInfo _currentCulture = ResolveCulture(AppLanguage.Auto);

    private LocalizationManager()
    {
    }

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Raised after the language changed, so that code-behind which cached strings can refresh itself.
    /// </summary>
    public event EventHandler LanguageChanged;

    public AppLanguage CurrentLanguage => _currentLanguage;

    public CultureInfo CurrentCulture => _currentCulture;

    /// <summary>
    /// Indexer used by XAML bindings: {loc:Loc Some_Key}
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or the key itself when it is missing,
    /// so that a forgotten translation is visible instead of producing an empty label.
    /// </summary>
    public static string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        try
        {
            return _resourceManager.GetString(key, Instance._currentCulture) ?? key;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        var culture = ResolveCulture(language);

        _currentLanguage = language;
        _currentCulture = culture;

        Strings.Culture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // "Item[]" tells WPF to re-evaluate every indexer binding, which refreshes the whole UI.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static CultureInfo ResolveCulture(AppLanguage language)
    {
        return language switch
        {
            AppLanguage.English => EnglishCulture,
            AppLanguage.Japanese => JapaneseCulture,
            _ => IsSystemJapanese() ? JapaneseCulture : EnglishCulture
        };
    }

    private static bool IsSystemJapanese()
    {
        try
        {
            return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName
                .Equals("ja", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static AppLanguage ParseLanguage(string value)
    {
        return Enum.TryParse<AppLanguage>(value, true, out var parsed) ? parsed : AppLanguage.Auto;
    }

    /// <summary>
    /// Display names for the language picker in the settings screen.
    /// The entries are intentionally bilingual so that a user who picked the wrong language can find their way back.
    /// </summary>
    public static IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new LanguageOption(AppLanguage.Auto, "Auto / 自動 (OS)"),
        new LanguageOption(AppLanguage.English, "English"),
        new LanguageOption(AppLanguage.Japanese, "日本語")
    ];
}

public class LanguageOption(AppLanguage language, string displayName)
{
    public AppLanguage Language { get; } = language;
    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;
}
