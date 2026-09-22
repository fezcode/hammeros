using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class SudokuView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly Button[] _cells = new Button[81];
    private readonly TextBlock _status = Ui.Text("", 12, Ui.Phosphor);
    private readonly TextBlock _progress = Ui.Label("", Ui.Phosphor);
    private readonly Button _pencil;
    private readonly ContentControl _notice = new();
    private int _selected;
    private bool _notes;
    public SudokuGame Game => _state.Sudoku;
    public SudokuView(SystemState state)
    {
        _state = state; Background = Ui.Dark; Focusable = true;
        var root = new Grid { RowDefinitions = new RowDefinitions("78,*,48,40,Auto") };
        var title = Ui.Text("Nine by nine.", 29, Ui.Phosphor); title.FontFamily = Ui.Serif;
        root.Children.Add(new Border { Padding = new Thickness(24, 12), Child = Ui.Columns("*,Auto", Ui.Stack(4, Ui.Label("SUDOKU  /  LOGIC STUDIES", Ui.Muted), title), _progress) });
        var board = new Grid { Width = 450, Height = 450, RowDefinitions = new RowDefinitions("*,*,*,*,*,*,*,*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*,*,*"), Background = Brush.Parse("#5A8C80") };
        for (var i = 0; i < 81; i++)
        {
            var index = i; var button = Ui.Button("", () => { _selected = index; Refresh(); Focus(); }, "dark"); button.Name = "SudokuCell" + i; button.Padding = new Thickness(0); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Margin = new Thickness(i % 9 % 3 == 0 ? 2 : .5, i / 9 % 3 == 0 ? 2 : .5, i % 9 == 8 ? 2 : .5, i / 9 == 8 ? 2 : .5); Grid.SetRow(button, i / 9); Grid.SetColumn(button, i % 9); board.Children.Add(button); _cells[i] = button;
        }
        var scaled = new Viewbox { Child = board, Stretch = Stretch.Uniform, Margin = new Thickness(24, 0) }; Grid.SetRow(scaled, 1); root.Children.Add(scaled);
        var keys = Ui.Row(4); keys.HorizontalAlignment = HorizontalAlignment.Center;
        for (var n = 1; n <= 9; n++) { var digit = n; var key = Ui.Button(n.ToString(), () => Enter(digit), "dark"); key.Width = 36; key.FontFamily = Ui.Mono; keys.Children.Add(key); }
        Grid.SetRow(keys, 2); root.Children.Add(keys);
        _pencil = Ui.Button("Notes: off", () => { _notes = !_notes; Refresh(); }, "dark");
        var actions = Ui.Columns("*,Auto", _status, Ui.Row(0, _pencil, Ui.Button("Erase", () => Enter(0), "dark"), Ui.Button("New puzzle", NewPuzzle, "dark"))); actions.Margin = new Thickness(24, 0); Grid.SetRow(actions, 3); root.Children.Add(actions);
        Grid.SetRow(_notice, 4); root.Children.Add(_notice); Content = root; state.Changed += Refresh; Refresh();
        KeyDown += (_, e) => { var digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D0 : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad0 : 0; if (digit > 0) { Enter(digit); e.Handled = true; } else if (e.Key is Key.Delete or Key.Back) { Enter(0); e.Handled = true; } else if (e.Key == Key.N) { _notes = !_notes; Refresh(); e.Handled = true; } else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { _selected = Math.Clamp(_selected + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : e.Key == Key.Up ? -9 : 9), 0, 80); Refresh(); e.Handled = true; } };
    }
    public void Enter(int value) { Game.Enter(_selected, value, _notes); _state.Save(); Focus(); }
    private void NewPuzzle()
    {
        _notice.Content = Ui.Pad(Ui.Row(12, Ui.Text("Replace this puzzle?", 12, Ui.Phosphor), Ui.Button("Keep playing", () => _notice.Content = null, "dark"), Ui.Button("New puzzle", () => { _state.Sudoku = SudokuGame.Create(); _notice.Content = null; _state.Save(); }, "primary")), 12);
    }
    private void Refresh()
    {
        for (var i = 0; i < 81; i++)
        {
            var value = Game.Cells[i]; var selected = i == _selected; var peer = SudokuGame.Peers(_selected).Contains(i);
            _cells[i].Background = Brush.Parse(selected ? "#39625E" : Game.Conflict(i) ? "#633F3C" : peer ? "#1D3C41" : "#102C32");
            if (value > 0) _cells[i].Content = Ui.Text(value.ToString(), 24, Game.Givens[i] > 0 ? Brush.Parse("#DBEADD") : Ui.Phosphor, true);
            else { var notes = Enumerable.Range(1, 9).Select(n => (Game.Notes[i] & 1 << n) != 0 ? n.ToString() : " ").ToArray(); _cells[i].Content = Ui.Text(string.Join('\n', Enumerable.Range(0, 3).Select(r => string.Join(' ', notes.Skip(r * 3).Take(3)))), 9, Ui.Muted, true); }
        }
        _pencil.Content = _notes ? "Notes: on" : "Notes: off"; _pencil.Background = _notes ? Brush.Parse("#31534E") : Brushes.Transparent;
        _progress.Text = $"{Game.Cells.Count(x => x > 0):00} / 81"; _status.Text = Game.Solved ? "Puzzle complete. Well played." : Enumerable.Range(0, 81).Any(Game.Conflict) ? "Check highlighted conflicts." : "1–9 enter  ·  N notes";
    }
    public void Dispose() => _state.Changed -= Refresh;
}
