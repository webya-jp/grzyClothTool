using System.Runtime.CompilerServices;
using grzyClothTool.Localization;

namespace grzyClothTool.UnitTests;

/// <summary>
/// Pins the UI language to English for the whole test run.
/// Without this the language defaults to Auto, which follows the machine's Windows
/// language, so tests asserting on user-facing strings would pass or fail depending
/// on where they run.
/// </summary>
internal static class TestLocalizationSetup
{
    [ModuleInitializer]
    internal static void UseEnglish()
    {
        LocalizationManager.Instance.SetLanguage(AppLanguage.English);
    }
}
