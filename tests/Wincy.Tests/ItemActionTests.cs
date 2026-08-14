using Wincy.Interop;
using Wincy.Services;
using Wincy.ViewModels;
using Xunit;

namespace Wincy.Tests;

public class ItemActionTests
{
    // The matrix Maccy defines: what each modifier does depends on the two
    // "by default" settings. Plain Ctrl is always the primary action.
    [Fact]
    public void CtrlCopiesByDefault()
    {
        var settings = new AppSettings();

        Assert.Equal(
            ItemAction.Copy,
            ItemActions.FromModifiers(HotKeyModifiers.Control, settings));
    }

    [Fact]
    public void AltPastesByDefault()
    {
        var settings = new AppSettings();

        Assert.Equal(
            ItemAction.Paste,
            ItemActions.FromModifiers(HotKeyModifiers.Alt, settings));
    }

    [Fact]
    public void AltShiftPastesWithoutFormatting()
    {
        var settings = new AppSettings();

        Assert.Equal(
            ItemAction.PasteWithoutFormatting,
            ItemActions.FromModifiers(
                HotKeyModifiers.Alt | HotKeyModifiers.Shift, settings));
    }

    [Fact]
    public void PasteByDefaultSwapsCtrlAndAlt()
    {
        var settings = new AppSettings { PasteByDefault = true };

        Assert.Equal(ItemAction.Paste, ItemActions.FromModifiers(HotKeyModifiers.Control, settings));
        Assert.Equal(ItemAction.Copy, ItemActions.FromModifiers(HotKeyModifiers.Alt, settings));
    }

    [Fact]
    public void EveryActionIsReachableInEveryConfiguration()
    {
        foreach (var paste in new[] { false, true })
        {
            foreach (var plain in new[] { false, true })
            {
                var settings = new AppSettings
                {
                    PasteByDefault = paste,
                    RemoveFormattingByDefault = plain
                };

                foreach (var action in new[] { ItemAction.Copy, ItemAction.Paste, ItemAction.PasteWithoutFormatting })
                {
                    var modifiers = ItemActions.ModifiersFor(action, settings);

                    Assert.True(
                        modifiers != HotKeyModifiers.None,
                        $"{action} unreachable with PasteByDefault={paste}, RemoveFormatting={plain}");
                    Assert.Equal(action, ItemActions.FromModifiers(modifiers, settings));
                }
            }
        }
    }

    [Fact]
    public void UnrecognisedCombinationsDoNothing()
    {
        var settings = new AppSettings();

        Assert.Equal(
            ItemAction.Unknown,
            ItemActions.FromModifiers(HotKeyModifiers.Shift, settings));
    }
}
