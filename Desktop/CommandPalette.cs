using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HammerOS.Desktop;

public sealed record PaletteAction(string Title, string Detail, string Icon, string Keywords, Action Run);

public sealed class CommandPalette : Border
{
    public TextBox Search { get; } = new() { Name = "SpotlightSearch", Watermark = "Search apps, windows, commands…", FontSize = 21, MaxLength = 4096 };
    public ListBox Results { get; } = new() { Name = "SpotlightResults", Background = Brushes.Transparent, BorderThickness = new Thickness(0), MaxHeight = 336 };
    private readonly Func<string, IEnumerable<PaletteAction>> _search;
    private readonly Action _close;
    public CommandPalette(Func<string, IEnumerable<PaletteAction>> search, Action close)
    {
        _search = search; _close = close; Name = "Spotlight"; Width = 650; Padding = new Thickness(0); Background = Ui.Cream; BorderBrush = Ui.Line; BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Top; Margin = new Thickness(24, 115, 24, 24);
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 60, OffsetY = 18, Color = Color.Parse("#9008171D") });
        Search.Classes.Add("search"); Search.Height = 58; Search.Padding = new Thickness(12, 8);
        var header = new Border { Padding = new Thickness(20, 8), Child = Ui.Columns("25,*,Auto", new Glyph("search") { Width = 23, Height = 23, VerticalAlignment = VerticalAlignment.Center }, Search, Ui.Button("Esc", close)) };
        Results.ItemTemplate = new FuncDataTemplate<PaletteAction>((item, _) => {
            if (item is null) return new Border();
            var title = Ui.Text(item.Title, 14); title.TextTrimming = TextTrimming.CharacterEllipsis;
            var detail = Ui.Text(item.Detail, 11, Ui.Muted); detail.TextTrimming = TextTrimming.CharacterEllipsis;
            return new Border { Padding = new Thickness(9, 10), Child = Ui.Columns("36,*", new Glyph(item.Icon) { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center }, Ui.Stack(5, title, detail)) };
        });
        var footer = new Border { Padding = new Thickness(20, 14), Child = Ui.Columns("*,Auto", Ui.Label("> COMMAND   /   SWITCH WINDOW"), Ui.Label("↑ ↓ SELECT   ↵ OPEN")) };
        Child = Ui.Stack(0, header, Ui.Rule(), Results, Ui.Rule(), footer);
        Search.TextChanged += (_, _) => Refresh();
        AddHandler(KeyDownEvent, (_, e) => {
            if (e.Key == Key.Escape) { close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Execute(); e.Handled = true; }
            else if (e.Key is Key.Down or Key.Up && Results.ItemCount > 0) { Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.ItemCount - 1); Results.ScrollIntoView(Results.SelectedItem!); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Results.DoubleTapped += (_, _) => Execute(); Refresh();
    }
    public void Refresh() { Results.ItemsSource = _search(Search.Text ?? "").Take(30).ToArray(); Results.SelectedIndex = Results.ItemCount > 0 ? 0 : -1; }
    private void Execute() { if (Results.SelectedItem is PaletteAction action) { _close(); action.Run(); } }
}
