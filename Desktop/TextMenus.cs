using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace HammerOS.Desktop;

// Installed by the TextBox style, so new dialogs and app inputs share the same menu.
public sealed class TextMenus : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<TextMenus, TextBox, bool>("Enabled");
    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);
    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);
    static TextMenus()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((box, e) => {
            if (e.NewValue is not true) return;
            box.ContextFlyout = null;
            var menu = new ContextMenu { MinWidth = 230 };
            void Populate()
            {
                menu.Items.Clear();
                void Item(string title, Action run, bool enabled, Key? key = null)
                { var item = new MenuItem { Header = title, IsEnabled = enabled, InputGesture = key is { } k ? new KeyGesture(k, KeyModifiers.Control) : null }; item.Click += (_, _) => { run(); box.Focus(); }; menu.Items.Add(item); }
                Item("Undo", box.Undo, box.CanUndo && !box.IsReadOnly, Key.Z);
                Item("Redo", box.Redo, box.CanRedo && !box.IsReadOnly, Key.Y);
                menu.Items.Add(new Separator());
                Item("Cut", box.Cut, box.CanCut && !box.IsReadOnly, Key.X);
                Item("Copy", box.Copy, box.CanCopy, Key.C);
                Item("Paste", box.Paste, !box.IsReadOnly, Key.V);
                Item("Delete", () => box.SelectedText = "", box.SelectionStart != box.SelectionEnd && !box.IsReadOnly);
                menu.Items.Add(new Separator()); Item("Select all", box.SelectAll, !string.IsNullOrEmpty(box.Text), Key.A);
            }
            Populate(); menu.Opened += (_, _) => Populate(); box.ContextMenu = menu;
        });
    }
}
