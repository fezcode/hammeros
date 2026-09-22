using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class DepartmentTerminalView : UserControl
{
    public TerminalSession Session { get; }
    public TextBox Input { get; } = new() { Watermark = "Type help to begin", Name = "TerminalInput" };
    private readonly TextBlock _output = Ui.Text($"HAMMEROS [VERSION {Product.Version}]\n(c) Hammer Industries. All work is valued.\n\nSecure session established.\nWelcome back, refiner.\n", 12, Ui.Phosphor, true);
    private readonly TextBlock _prompt = Ui.Text("", 12, Ui.Phosphor, true);
    private readonly ScrollViewer _scroll;
    private int _history;
    public DepartmentTerminalView(SystemState state, Action<string> open)
    {
        Session = new(state) { OpenApp = open }; Background = Ui.Dark;
        _output.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _output.VerticalAlignment = VerticalAlignment.Top;
        Input.Classes.Add("terminal"); Input.FontSize = 13; Input.Height = 38; Input.MinHeight = 38; Input.Padding = new Thickness(8, 6); Input.VerticalContentAlignment = VerticalAlignment.Center;
        _prompt.FontSize = 13; _prompt.MaxWidth = 180; _prompt.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,26") };
        _scroll = Ui.Scroll(_output); _scroll.Margin = new Thickness(20, 17, 20, 7); grid.Children.Add(_scroll);
        var command = Ui.Columns("Auto,*", _prompt, Input); command.Margin = new Thickness(18, 0, 16, 8); command.MinHeight = 42; Grid.SetRow(command, 1); grid.Children.Add(command);
        var footer = new Border { Padding = new Thickness(20, 0), Background = Avalonia.Media.Brush.Parse("#193A40"), Child = Ui.Columns("*,Auto", Ui.Label("HSH  /  VIRTUAL SHELL", Ui.Muted), Ui.Label("HMR-04   ●", Ui.Phosphor)) }; Grid.SetRow(footer, 2); grid.Children.Add(footer);
        Content = grid; Prompt();
        Input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Submit(Input.Text ?? ""); Input.Text = ""; e.Handled = true; }
            else if (e.Key == Key.Up && Session.History.Count > 0) { _history = Math.Max(0, _history - 1); Input.Text = Session.History[_history]; Input.CaretIndex = Input.Text.Length; e.Handled = true; }
            else if (e.Key == Key.Down && Session.History.Count > 0) { _history = Math.Min(Session.History.Count, _history + 1); Input.Text = _history == Session.History.Count ? "" : Session.History[_history]; Input.CaretIndex = Input.Text.Length; e.Handled = true; }
            else if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _output.Text = ""; e.Handled = true; }
        };
        PointerPressed += (_, _) => Input.Focus();
    }
    private void Prompt() => _prompt.Text = $"hmr-04:{Session.Cwd.Replace("/department", "~")} $";
    public void Submit(string command)
    {
        var prompt = _prompt.Text; var result = Session.Execute(command);
        _output.Text = result == "\f" ? "" : _output.Text + $"\n{prompt} {command}\n{result}\n";
        if (_output.Text.Length > 60000) _output.Text = _output.Text[^45000..];
        _history = Session.History.Count; Prompt(); _scroll.ScrollToEnd();
    }
}
