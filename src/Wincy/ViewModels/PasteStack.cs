using Wincy.Interop;

namespace Wincy.ViewModels;

/// <summary>
/// A queue of items to paste one after another. Each paste pops the head and puts the
/// next item on the clipboard, so repeated Ctrl+V walks the selection in order.
/// Any copy made outside Wincy cancels the stack.
/// </summary>
public sealed class PasteStack(List<ClipItemViewModel> items, HotKeyModifiers modifiers)
{
    public Guid Id { get; } = Guid.NewGuid();

    public List<ClipItemViewModel> Items { get; } = items;

    public HotKeyModifiers Modifiers { get; } = modifiers;

    public int Remaining => Items.Count;

    public string Summary => Items.Count == 1
        ? "1 item queued"
        : $"{Items.Count} items queued";
}
