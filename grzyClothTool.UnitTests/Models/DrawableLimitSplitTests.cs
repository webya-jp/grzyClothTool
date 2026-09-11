using System.Collections.Generic;
using System.Collections.ObjectModel;
using grzyClothTool.Constants;
using grzyClothTool.Helpers;
using grzyClothTool.Models;
using grzyClothTool.Models.Drawable;
using grzyClothTool.Models.Texture;
using static grzyClothTool.Enums;

namespace grzyClothTool.UnitTests.Models;

/// <summary>
/// FiveM's 0-255 drawable range extension covers components only; props stay at 128 slots.
/// These tests pin the split between the component limit and the prop limit.
/// </summary>
public class DrawableLimitSplitTests
{
    [Fact]
    public void GetMaxDrawablesInAddon_UsesSeparateLimitsForComponentsAndProps()
    {
        WithLimits(255, 127, () =>
        {
            Assert.Equal(256, GlobalConstants.MAX_DRAWABLES_IN_ADDON);
            Assert.Equal(128, GlobalConstants.MAX_PROP_DRAWABLES_IN_ADDON);
            Assert.Equal(256, GlobalConstants.GetMaxDrawablesInAddon(false));
            Assert.Equal(128, GlobalConstants.GetMaxDrawablesInAddon(true));
        });
    }

    [Fact]
    public void MaxPropDrawableNumber_IsClampedToPropLimit()
    {
        WithLimits(255, 127, () =>
        {
            SettingsHelper.Instance.MaxPropDrawableNumber = 255;
            Assert.Equal(GlobalConstants.MAX_PROP_DRAWABLE_NUMBER_LIMIT, SettingsHelper.Instance.MaxPropDrawableNumber);

            SettingsHelper.Instance.MaxPropDrawableNumber = -5;
            Assert.Equal(0, SettingsHelper.Instance.MaxPropDrawableNumber);
        });
    }

    [Fact]
    public void Distribution_SplitsComponentsAt256AndPropsAt128()
    {
        WithLimits(255, 127, () =>
        {
            var addons = new List<Addon>();

            // 300 components of one type/sex and 200 props of one type/sex.
            var drawables = new List<GDrawable>();
            for (var i = 0; i < 300; i++)
            {
                drawables.Add(CreateDrawable(SexType.male, isProp: false, typeNumeric: 11));
            }
            for (var i = 0; i < 200; i++)
            {
                drawables.Add(CreateDrawable(SexType.male, isProp: true, typeNumeric: 0));
            }

            foreach (var drawable in drawables)
            {
                Place(addons, drawable);
            }

            Assert.Equal(2, addons.Count);

            var componentsPerAddon = addons
                .Select(a => a.Drawables.Count(d => !d.IsProp))
                .ToList();
            var propsPerAddon = addons
                .Select(a => a.Drawables.Count(d => d.IsProp))
                .ToList();

            Assert.Equal(new[] { 256, 44 }, componentsPerAddon);
            Assert.Equal(new[] { 128, 72 }, propsPerAddon);
        });
    }

    [Fact]
    public void CanFitDrawables_AllowsMoreComponentsThanProps()
    {
        WithLimits(255, 127, () =>
        {
            var componentAddon = new Addon("components");
            for (var i = 0; i < 255; i++)
            {
                componentAddon.Drawables.Add(CreateDrawable(SexType.male, isProp: false, typeNumeric: 11, number: i));
            }

            var propAddon = new Addon("props");
            for (var i = 0; i < 127; i++)
            {
                propAddon.Drawables.Add(CreateDrawable(SexType.male, isProp: true, typeNumeric: 0, number: i));
            }

            // 256th component still fits, 257th does not.
            Assert.True(componentAddon.CanFitDrawables([CreateDrawable(SexType.male, false, 11)]));
            componentAddon.Drawables.Add(CreateDrawable(SexType.male, isProp: false, typeNumeric: 11, number: 255));
            Assert.False(componentAddon.CanFitDrawables([CreateDrawable(SexType.male, false, 11)]));

            // 128th prop still fits, 129th does not.
            Assert.True(propAddon.CanFitDrawables([CreateDrawable(SexType.male, true, 0)]));
            propAddon.Drawables.Add(CreateDrawable(SexType.male, isProp: true, typeNumeric: 0, number: 127));
            Assert.False(propAddon.CanFitDrawables([CreateDrawable(SexType.male, true, 0)]));
        });
    }

    [Fact]
    public void DisplayNumber_WrapsAtTheKindSpecificLimit()
    {
        WithLimits(255, 127, () =>
        {
            Assert.Equal("255", CreateDrawable(SexType.male, false, 11, 255).DisplayNumber);
            Assert.Equal("000", CreateDrawable(SexType.male, false, 11, 256).DisplayNumber);
            Assert.Equal("044", CreateDrawable(SexType.male, false, 11, 300).DisplayNumber);

            Assert.Equal("127", CreateDrawable(SexType.male, true, 0, 127).DisplayNumber);
            Assert.Equal("000", CreateDrawable(SexType.male, true, 0, 128).DisplayNumber);
            Assert.Equal("072", CreateDrawable(SexType.male, true, 0, 200).DisplayNumber);
        });
    }

    /// <summary>
    /// Mirrors the placement loop used when adding drawables: first addon that can still
    /// fit the drawable wins, otherwise a new addon is created.
    /// </summary>
    private static void Place(List<Addon> addons, GDrawable drawable)
    {
        foreach (var addon in addons)
        {
            if (addon.CanFitDrawables([drawable]))
            {
                drawable.Number = addon.Drawables.Count(d =>
                    d.TypeNumeric == drawable.TypeNumeric && d.IsProp == drawable.IsProp && d.Sex == drawable.Sex);
                addon.Drawables.Add(drawable);
                return;
            }
        }

        var newAddon = new Addon($"Addon {addons.Count + 1}");
        drawable.Number = 0;
        newAddon.Drawables.Add(drawable);
        addons.Add(newAddon);
    }

    private static void WithLimits(int componentLimit, int propLimit, Action body)
    {
        var originalComponent = SettingsHelper.Instance.MaxDrawableNumber;
        var originalProp = SettingsHelper.Instance.MaxPropDrawableNumber;
        try
        {
            SettingsHelper.Instance.MaxDrawableNumber = componentLimit;
            SettingsHelper.Instance.MaxPropDrawableNumber = propLimit;
            body();
        }
        finally
        {
            SettingsHelper.Instance.MaxDrawableNumber = originalComponent;
            SettingsHelper.Instance.MaxPropDrawableNumber = originalProp;
        }
    }

    private static GDrawable CreateDrawable(SexType sex, bool isProp, int typeNumeric, int number = 0)
    {
        return new GDrawable(
            Guid.Empty,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ydd"),
            sex,
            isProp,
            typeNumeric,
            number,
            false,
            new ObservableCollection<GTexture>());
    }
}
