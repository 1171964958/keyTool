using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyTool.Windows.Models;
using KeyTool.Windows.Services;

namespace KeyTool.Windows.Controls;

public sealed class ShortcutCaptureButton : Button
{
    public static readonly DependencyProperty GestureProperty =
        DependencyProperty.Register(
            nameof(Gesture),
            typeof(ShortcutGesture),
            typeof(ShortcutCaptureButton),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnGestureChanged
            )
        );

    private bool _recording;

    public ShortcutCaptureButton()
    {
        Focusable = true;
        MinWidth = 130;
        Content = "点击录制";
    }

    public ShortcutGesture? Gesture
    {
        get => (ShortcutGesture?)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    protected override void OnClick()
    {
        _recording = true;
        Content = "请按任意键…";
        Focus();
        Keyboard.Focus(this);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            FinishRecording();
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
        {
            Content = $"请按任意键… {Keyboard.Modifiers}";
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            Content = "请按任意键…";
            return;
        }

        Gesture = new ShortcutGesture
        {
            VirtualKey = virtualKey,
            Modifiers = modifiers,
            KeyLabel = VirtualKeyLabel.ForVirtualKey(virtualKey)
        };
        FinishRecording();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (_recording)
        {
            FinishRecording();
        }
        base.OnLostKeyboardFocus(e);
    }

    private void FinishRecording()
    {
        _recording = false;
        Content = Gesture?.DisplayName ?? "点击录制";
    }
    private static void OnGestureChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var control = (ShortcutCaptureButton)dependencyObject;
        if (!control._recording)
        {
            control.Content = (args.NewValue as ShortcutGesture)?.DisplayName
                ?? "点击录制";
        }
    }
}
