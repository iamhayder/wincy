using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wincy.Interop;

namespace Wincy.Views.Controls;

/// <summary>
/// A shortcut recorder. Click it, press a combination, and it is captured; Backspace or
/// Delete on an empty press clears the binding.
/// </summary>
public sealed class HotKeyBox : Control
{
    public static readonly DependencyProperty HotKeyProperty = DependencyProperty.Register(
        nameof(HotKey), typeof(HotKey), typeof(HotKeyBox),
        new FrameworkPropertyMetadata(Interop.HotKey.None,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotKeyChanged));

    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(HotKeyBox),
        new PropertyMetadata("Click to record"));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsRecordingKey = DependencyProperty.RegisterReadOnly(
        nameof(IsRecording), typeof(bool), typeof(HotKeyBox), new PropertyMetadata(false));

    public static readonly DependencyProperty IsRecordingProperty = IsRecordingKey.DependencyProperty;

    public HotKeyBox()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        MinWidth = 150;
        MinHeight = 26;

        // Built in code so the control needs no generic.xaml, keeping it self-contained.
        Template = BuildTemplate();
        UpdateDisplay();
    }

    public HotKey HotKey
    {
        get => (HotKey)GetValue(HotKeyProperty);
        set => SetValue(HotKeyProperty, value);
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextKey, value);
    }

    public bool IsRecording
    {
        get => (bool)GetValue(IsRecordingProperty);
        private set => SetValue(IsRecordingKey, value);
    }

    private static void OnHotKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HotKeyBox)d).UpdateDisplay();

    private void UpdateDisplay()
    {
        if (IsRecording)
        {
            DisplayText = "Press a shortcut…";
            return;
        }

        DisplayText = HotKey.IsValid ? HotKey.ToString() : "Not set";
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        IsRecording = true;
        UpdateDisplay();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        IsRecording = false;
        UpdateDisplay();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (!IsRecording)
        {
            return;
        }

        // Alt combinations arrive as Key.System.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        if (HotKey.IsModifierKey(key))
        {
            return;
        }

        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            HotKey = Interop.HotKey.None;
            return;
        }

        // A bare key would fire everywhere, so at least one modifier is required.
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            return;
        }

        HotKey = HotKey.FromWpf(Keyboard.Modifiers, key);
        UpdateDisplay();
    }

    private static ControlTemplate BuildTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Root");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetResourceReference(Border.BackgroundProperty, "FieldBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "SeparatorBrush");
        border.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(DisplayText))
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        border.AppendChild(text);

        var template = new ControlTemplate(typeof(HotKeyBox)) { VisualTree = border };

        var recording = new Trigger { Property = IsRecordingProperty, Value = true };
        recording.Setters.Add(new Setter(
            Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "Root"));
        template.Triggers.Add(recording);

        return template;
    }
}
