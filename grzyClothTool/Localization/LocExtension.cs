using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace grzyClothTool.Localization;

/// <summary>
/// XAML markup extension for localized strings: <c>{loc:Loc Some_Key}</c>.
/// It produces a binding to <see cref="LocalizationManager"/>, so the text updates
/// immediately when the language is changed at runtime.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay
        };

        // Inside templates or on plain CLR properties a BindingExpression cannot be applied,
        // so fall back to the resolved string for those cases.
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target
            && target.TargetObject is not null
            && target.TargetObject is not DependencyObject
            && target.TargetObject.GetType().FullName != "System.Windows.SharedDp")
        {
            return LocalizationManager.GetString(Key);
        }

        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Short helper for localized strings in code-behind: <c>Loc.T("Some_Key")</c>.
/// </summary>
public static class Loc
{
    public static string T(string key) => LocalizationManager.GetString(key);

    public static string T(string key, params object[] args)
    {
        var format = LocalizationManager.GetString(key);
        try
        {
            return string.Format(LocalizationManager.Instance.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
