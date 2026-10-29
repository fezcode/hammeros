using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HammerOS.Desktop;

public static class WorkspaceTab
{
    public static Control Create(string title, bool selected, Action activate, Action? close = null)
    {
        var label = Ui.Text(title, 12); label.MaxWidth = 170; label.TextTrimming = TextTrimming.CharacterEllipsis;
        var button = Ui.Button("", activate); button.Content = label; button.Padding = new Thickness(12, 0); button.Height = 32; button.MinHeight = 0; button.Classes.Add("workspace-tab-button");
        var row = Ui.Row(0, button);
        if (close is not null) { var remove = Ui.IconButton("close", "Close tab", close); remove.Classes.Add("workspace-tab-button"); remove.Width = 26; remove.Height = 30; remove.MinHeight = 0; remove.Padding = new Thickness(6); remove.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(remove); }
        var border = new Border { Height = 34, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = selected ? Ui.Ink : Brushes.Transparent, Child = row };
        border.Classes.Add("workspace-tab"); border.Classes.Set("selected", selected); ToolTip.SetTip(border, title); return border;
    }
}
