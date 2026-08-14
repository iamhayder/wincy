using Wincy.Interop;
using Wincy.Services;

namespace Wincy.ViewModels;

/// <summary>
/// One of the per-row shortcut badges. Each row advertises up to three: copy, paste,
/// and paste-without-formatting. Which one is visible depends on the modifiers the
/// user is currently holding, exactly as in Maccy.
///
/// The macOS Command key maps to Ctrl and Option maps to Alt.
/// </summary>
public sealed class KeyShortcut(string character, HotKeyModifiers modifiers)
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Character { get; } = character;

    public HotKeyModifiers Modifiers { get; } = modifiers;

    public string Description
    {
        get
        {
            var text = string.Empty;
            if (Modifiers.HasFlag(HotKeyModifiers.Control)) text += "Ctrl+";
            if (Modifiers.HasFlag(HotKeyModifiers.Shift)) text += "Shift+";
            if (Modifiers.HasFlag(HotKeyModifiers.Alt)) text += "Alt+";
            return text + Character.ToUpperInvariant();
        }
    }

    /// <summary>
    /// The trio of shortcuts for one row. The pairing of modifier to action follows
    /// <see cref="AppSettings.PasteByDefault"/>, so the badge always shows what will
    /// actually happen.
    /// </summary>
    public static List<KeyShortcut> Create(string character, bool pasteByDefault) =>
    [
        new(character, HotKeyModifiers.Control),
        new(character, HotKeyModifiers.Alt),
        new(character, (pasteByDefault ? HotKeyModifiers.Control : HotKeyModifiers.Alt) | HotKeyModifiers.Shift)
    ];

    /// <summary>
    /// Which badge to render for the modifiers currently held. With nothing held the
    /// Ctrl variant shows; holding Alt swaps in the Alt variant, and so on.
    /// </summary>
    public bool IsVisible(IReadOnlyList<KeyShortcut> all, HotKeyModifiers pressed)
    {
        if (all.Count == 1)
        {
            return true;
        }

        if (Modifiers == HotKeyModifiers.Control && pressed == HotKeyModifiers.None)
        {
            return true;
        }

        if (Modifiers == HotKeyModifiers.Control && pressed != HotKeyModifiers.None &&
            !all.Any(other => other.Id != Id && other.Modifiers == pressed))
        {
            return true;
        }

        return Modifiers == pressed;
    }
}

/// <summary>What activating a row should do, given the modifiers held at that moment.</summary>
public enum ItemAction
{
    Unknown,
    Copy,
    Paste,
    PasteWithoutFormatting
}

public static class ItemActions
{
    /// <summary>
    /// The modifier matrix, ported from Maccy's HistoryItemAction. Two settings rotate
    /// it: "paste automatically" and "paste without formatting", which between them
    /// decide whether plain Ctrl means copy or paste.
    /// </summary>
    public static ItemAction FromModifiers(HotKeyModifiers modifiers, AppSettings settings)
    {
        var paste = settings.PasteByDefault;
        var plain = settings.RemoveFormattingByDefault;

        // Ignore Windows key and stray flags; only Ctrl/Alt/Shift take part.
        modifiers &= HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift;

        const HotKeyModifiers ctrl = HotKeyModifiers.Control;
        const HotKeyModifiers alt = HotKeyModifiers.Alt;
        const HotKeyModifiers altShift = HotKeyModifiers.Alt | HotKeyModifiers.Shift;
        const HotKeyModifiers ctrlShift = HotKeyModifiers.Control | HotKeyModifiers.Shift;

        return modifiers switch
        {
            ctrl when !paste => ItemAction.Copy,
            ctrl when paste && !plain => ItemAction.Paste,
            ctrl when paste && plain => ItemAction.PasteWithoutFormatting,

            alt when !paste && !plain => ItemAction.Paste,
            alt when !paste && plain => ItemAction.PasteWithoutFormatting,
            alt when paste => ItemAction.Copy,

            altShift when !paste && !plain => ItemAction.PasteWithoutFormatting,
            altShift when !paste && plain => ItemAction.Paste,

            ctrlShift when paste && !plain => ItemAction.PasteWithoutFormatting,
            ctrlShift when paste && plain => ItemAction.Paste,

            _ => ItemAction.Unknown
        };
    }

    /// <summary>
    /// Which modifiers produce a given action, used to label the footer and the
    /// per-row badges. The inverse of <see cref="FromModifiers"/>.
    /// </summary>
    public static HotKeyModifiers ModifiersFor(ItemAction action, AppSettings settings)
    {
        foreach (var candidate in new[]
                 {
                     HotKeyModifiers.Control,
                     HotKeyModifiers.Alt,
                     HotKeyModifiers.Alt | HotKeyModifiers.Shift,
                     HotKeyModifiers.Control | HotKeyModifiers.Shift
                 })
        {
            if (FromModifiers(candidate, settings) == action)
            {
                return candidate;
            }
        }

        return HotKeyModifiers.None;
    }
}
