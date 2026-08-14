using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Wincy.Interop;

[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>A modifier + key combination, serialisable to settings and printable for the UI.</summary>
public readonly record struct HotKey(HotKeyModifiers Modifiers, Key Key)
{
    public static readonly HotKey None = new(HotKeyModifiers.None, Key.None);

    [JsonIgnore]
    public bool IsValid => Key != Key.None;

    [JsonIgnore]
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    [JsonIgnore]
    public uint NativeModifiers
    {
        get
        {
            uint value = 0;
            if (Modifiers.HasFlag(HotKeyModifiers.Alt)) value |= NativeMethods.MOD_ALT;
            if (Modifiers.HasFlag(HotKeyModifiers.Control)) value |= NativeMethods.MOD_CONTROL;
            if (Modifiers.HasFlag(HotKeyModifiers.Shift)) value |= NativeMethods.MOD_SHIFT;
            if (Modifiers.HasFlag(HotKeyModifiers.Windows)) value |= NativeMethods.MOD_WIN;
            return value;
        }
    }

    public static HotKey FromWpf(ModifierKeys modifiers, Key key)
    {
        var result = HotKeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotKeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotKeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotKeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= HotKeyModifiers.Windows;
        return new HotKey(result, key);
    }

    public override string ToString()
    {
        if (!IsValid)
        {
            return "None";
        }

        var builder = new StringBuilder();
        if (Modifiers.HasFlag(HotKeyModifiers.Control)) builder.Append("Ctrl+");
        if (Modifiers.HasFlag(HotKeyModifiers.Shift)) builder.Append("Shift+");
        if (Modifiers.HasFlag(HotKeyModifiers.Alt)) builder.Append("Alt+");
        if (Modifiers.HasFlag(HotKeyModifiers.Windows)) builder.Append("Win+");
        builder.Append(Describe(Key));
        return builder.ToString();
    }

    public static string Describe(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.Delete => "Del",
        Key.Prior => "PgUp",
        Key.Next => "PgDn",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.Oem6 => "]",
        Key.Oem1 => ";",
        Key.OemQuotes => "'",
        Key.OemBackslash or Key.Oem5 => "\\",
        Key.Space => "Space",
        _ => key.ToString()
    };

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;
}
