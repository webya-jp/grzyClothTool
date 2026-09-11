using grzyClothTool.Helpers;
using System;

namespace grzyClothTool.Constants;

public static class GlobalConstants
{
    // Highest drawable number the game supports; numbering is 0-based (000-255).
    public const int MAX_DRAWABLE_NUMBER_LIMIT = 255;

    // Capacity of one addon. The user setting is the highest allowed drawable number,
    // so the count is that number + 1 (setting 255 -> numbers 000-255 -> 256 drawables).
    public static int MAX_DRAWABLES_IN_ADDON => SettingsHelper.Instance.MaxDrawableNumber + 1;

    // FiveM's extension of the drawable range from 128 to 256 covers COMPONENTS ONLY;
    // props (p_head, p_eyes, ...) are not part of the component sync patch and stay at
    // 128 slots, so the highest allowed prop drawable number is 127 (numbers 000-127).
    public const int MAX_PROP_DRAWABLE_NUMBER_LIMIT = 127;

    // Capacity of one addon for props, same +1 convention as MAX_DRAWABLES_IN_ADDON
    // (setting 127 -> numbers 000-127 -> 128 drawables).
    public static int MAX_PROP_DRAWABLES_IN_ADDON => SettingsHelper.Instance.MaxPropDrawableNumber + 1;

    // Addon capacity for a given drawable kind; props and components have separate limits.
    public static int GetMaxDrawablesInAddon(bool isProp) => isProp ? MAX_PROP_DRAWABLES_IN_ADDON : MAX_DRAWABLES_IN_ADDON;

    public const int MAX_DRAWABLE_TEXTURES = 26;
    public const string ASSETS_FOLDER_NAME = "project_assets";
    public static readonly Uri DISCORD_INVITE_URL = new("https://discord.gg/HCQutNhxWt");
    public static readonly string GRZY_TOOLS_URL = "https://grzy.tools";
}
