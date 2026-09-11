using grzyClothTool.Localization;
using System;
using System.Globalization;
using System.Windows.Data;

namespace grzyClothTool.Converters
{
    /// <summary>
    /// Display-only converter for GTA component / prop slot codes (jbib, lowr, p_head, ...).
    /// The codes themselves must stay untranslated because they are used for grouping, sorting
    /// and for building file names, so this converter is only applied where the value is shown
    /// to the user. In English it returns the plain code, in Japanese "トップス (jbib)".
    /// </summary>
    public class ComponentDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var code = value as string;
            if (string.IsNullOrEmpty(code))
            {
                return value;
            }

            var localized = LocalizationManager.GetString("Slot_" + code);
            if (string.IsNullOrEmpty(localized) || localized == "Slot_" + code || localized == code)
            {
                return code;
            }

            return $"{localized} ({code})";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Display-only converter for the "male" / "female" values stored on a drawable.
    /// </summary>
    public class SexDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var sex = value as string;
            if (string.IsNullOrEmpty(sex))
            {
                return value;
            }

            return sex.ToLowerInvariant() switch
            {
                "male" => LocalizationManager.GetString("Common_Male"),
                "female" => LocalizationManager.GetString("Common_Female"),
                _ => sex
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
