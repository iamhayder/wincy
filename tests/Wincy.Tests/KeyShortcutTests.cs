using Wincy.Interop;
using Wincy.ViewModels;
using Xunit;

namespace Wincy.Tests;

public class KeyShortcutTests
{
    [Fact]
    public void SplitsModifiersAndKeyIntoSeparateCaps()
    {
        var shortcut = new KeyShortcut("1", HotKeyModifiers.Control);

        Assert.Equal(["Ctrl", "1"], shortcut.Parts);
    }

    [Fact]
    public void OrdersModifiersTheWayWindowsWritesThem()
    {
        var shortcut = new KeyShortcut(
            "b", HotKeyModifiers.Alt | HotKeyModifiers.Shift | HotKeyModifiers.Control);

        Assert.Equal(["Ctrl", "Shift", "Alt", "B"], shortcut.Parts);
    }

    [Fact]
    public void CapitalisesSingleCharacterKeys()
    {
        var shortcut = new KeyShortcut("b", HotKeyModifiers.Alt);

        Assert.Equal(["Alt", "B"], shortcut.Parts);
    }

    [Fact]
    public void LeavesNamedKeysAlone()
    {
        // The footer binds Ctrl+Alt+Backspace; upper-casing would render "BACKSPACE".
        var shortcut = new KeyShortcut("Backspace", HotKeyModifiers.Control | HotKeyModifiers.Alt);

        Assert.Equal(["Ctrl", "Alt", "Backspace"], shortcut.Parts);
    }

    [Fact]
    public void DescriptionJoinsThePartsForTooltips()
    {
        var shortcut = new KeyShortcut("1", HotKeyModifiers.Alt | HotKeyModifiers.Shift);

        Assert.Equal("Shift+Alt+1", shortcut.Description);
    }

    [Fact]
    public void CreateBuildsTheCopyPasteTrio()
    {
        var shortcuts = KeyShortcut.Create("3", pasteByDefault: false);

        Assert.Equal(3, shortcuts.Count);
        Assert.Equal(HotKeyModifiers.Control, shortcuts[0].Modifiers);
        Assert.Equal(HotKeyModifiers.Alt, shortcuts[1].Modifiers);
        Assert.Equal(HotKeyModifiers.Alt | HotKeyModifiers.Shift, shortcuts[2].Modifiers);
    }

    [Fact]
    public void WithNoModifiersHeldTheCtrlCapIsTheVisibleOne()
    {
        var shortcuts = KeyShortcut.Create("3", pasteByDefault: false);

        var visible = shortcuts.Where(s => s.IsVisible(shortcuts, HotKeyModifiers.None)).ToList();

        Assert.Single(visible);
        Assert.Equal(HotKeyModifiers.Control, visible[0].Modifiers);
    }

    [Fact]
    public void HoldingAltSwapsInTheAltCap()
    {
        var shortcuts = KeyShortcut.Create("3", pasteByDefault: false);

        var visible = shortcuts.Where(s => s.IsVisible(shortcuts, HotKeyModifiers.Alt)).ToList();

        Assert.Single(visible);
        Assert.Equal(HotKeyModifiers.Alt, visible[0].Modifiers);
    }

    [Fact]
    public void ExactlyOneCapIsEverVisible()
    {
        // The row reserves space for one shortcut; two showing at once would overlap.
        var shortcuts = KeyShortcut.Create("3", pasteByDefault: false);

        foreach (var held in new[]
                 {
                     HotKeyModifiers.None,
                     HotKeyModifiers.Control,
                     HotKeyModifiers.Alt,
                     HotKeyModifiers.Alt | HotKeyModifiers.Shift,
                     HotKeyModifiers.Control | HotKeyModifiers.Shift,
                     HotKeyModifiers.Shift
                 })
        {
            var count = shortcuts.Count(s => s.IsVisible(shortcuts, held));
            Assert.True(count <= 1, $"{count} shortcuts visible while holding {held}");
        }
    }
}
