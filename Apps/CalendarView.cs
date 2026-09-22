using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class CalendarView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly Grid _days = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("28,*,*,*,*,*,*") };
    private readonly TextBlock _month = Ui.Text("", 25), _date = Ui.Text("", 21);
    private readonly StackPanel _agenda = Ui.Stack(12);
    private readonly bool _compact;
    private DateTime _display = new(DateTime.Today.Year, DateTime.Today.Month, 1), _selected = DateTime.Today;
    public CalendarView(SystemState state, bool compact = false, Action? openFull = null)
    {
        _state = state; _compact = compact; Background = Ui.Cream;
        var root = new Grid { RowDefinitions = new RowDefinitions("64,*,Auto"), Margin = new Thickness(compact ? 14 : 24) };
        _month.FontFamily = Ui.Serif; _month.FontSize = compact ? 23 : 30;
        var header = Ui.Columns("*,Auto", _month, Ui.Row(0, Ui.IconButton("back", "Previous month", () => { _display = _display.AddMonths(-1); Refresh(); }), Ui.IconButton("arrow", "Next month", () => { _display = _display.AddMonths(1); Refresh(); }))); root.Children.Add(header);
        _days.MinHeight = compact ? 245 : 350;
        if (compact) { Grid.SetRow(_days, 1); root.Children.Add(_days); var actions = Ui.Row(8, Ui.Button("Today", Today), Ui.Button("Open calendar", () => openFull?.Invoke(), "outline")); actions.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(actions, 2); root.Children.Add(actions); Width = 330; }
        else
        {
            var form = Ui.Stack(14, _date, Ui.Rule(), _agenda); var title = new TextBox { Name = "CalendarEventTitle", Watermark = "Event title", MaxLength = 120 }; var time = new TextBox { Name = "CalendarEventTime", Text = "09:00", Watermark = "HH:mm", Width = 80 }; var error = Ui.Text("", 12, Brush.Parse("#99513D"));
            form.Children.Add(Ui.Rule()); form.Children.Add(title); form.Children.Add(Ui.Row(8, time, Ui.Button("Add event", () => { if (string.IsNullOrWhiteSpace(title.Text) || !TimeOnly.TryParse(time.Text, out var when)) { error.Text = "Enter a title and a valid time."; return; } _state.Calendar.Add(new CalendarEntry { Id = Guid.NewGuid().ToString("N"), Title = title.Text.Trim(), When = _selected.Date + when.ToTimeSpan() }); title.Text = ""; error.Text = ""; _state.Save(); }, "primary"))); form.Children.Add(error);
            var content = Ui.Columns("*,280", _days, Ui.Pad(Ui.Scroll(form), 18)); Grid.SetRow(content, 1); root.Children.Add(content);
            var footer = Ui.Columns("*,Auto", Ui.Label("LOCAL CALENDAR  /  SAVED ON THIS COMPUTER"), Ui.Button("Today", Today, "outline")); footer.Margin = new Thickness(0, 16, 0, 0); Grid.SetRow(footer, 2); root.Children.Add(footer);
        }
        Content = root; state.Changed += Refresh; Refresh();
    }
    private void Today() { _selected = DateTime.Today; _display = new(_selected.Year, _selected.Month, 1); Refresh(); }
    public void SelectDate(DateTime date) { _selected = date.Date; _display = new(date.Year, date.Month, 1); Refresh(); }
    private void Refresh()
    {
        _month.Text = _display.ToString("MMMM yyyy"); _date.Text = _selected.ToString("ddd, d MMM"); _days.Children.Clear();
        for (var i = 0; i < 7; i++) { var text = Ui.Label(new[] { "M", "T", "W", "T", "F", "S", "S" }[i]); text.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(text, i); _days.Children.Add(text); }
        var first = _display.AddDays(-((int)_display.DayOfWeek + 6) % 7);
        for (var i = 0; i < 42; i++)
        {
            var day = first.AddDays(i); var events = _state.Calendar.Count(x => x.When.Date == day.Date); var label = Ui.Text(day.Day.ToString(), _compact ? 13 : 18, day.Date == _selected.Date ? Ui.Cream : day.Month == _display.Month ? Ui.Ink : Ui.Muted, true); label.HorizontalAlignment = HorizontalAlignment.Center;
            var button = Ui.Button("", () => { _selected = day; Refresh(); }); button.Name = "CalendarDay" + day.ToString("yyyyMMdd"); button.Content = Ui.Stack(5, label, new Border { Height = 3, Width = 12, Background = events > 0 ? day.Date == _selected.Date ? Ui.Cream : Ui.Ink : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center });
            button.Margin = new Thickness(2); button.Padding = new Thickness(4); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Background = day.Date == _selected.Date ? Ui.Ink : Brushes.Transparent; button.BorderBrush = day.Date == DateTime.Today ? Ui.Muted : Brushes.Transparent; button.BorderThickness = new Thickness(1); Grid.SetRow(button, i / 7 + 1); Grid.SetColumn(button, i % 7); _days.Children.Add(button);
            if (day.Date == _selected.Date) button.Classes.Add("calendar-selected");
        }
        if (_compact) return; _agenda.Children.Clear();
        foreach (var entry in _state.Calendar.Where(x => x.When.Date == _selected.Date).OrderBy(x => x.When).ToArray())
        { var text = Ui.Text(entry.Title, 13); text.TextWrapping = TextWrapping.Wrap; _agenda.Children.Add(Ui.Columns("*,Auto", Ui.Stack(4, Ui.Label(entry.When.ToString("HH:mm")), text), Ui.IconButton("close", "Delete event", () => { _state.Calendar.Remove(entry); _state.Save(); }))); }
        if (_agenda.Children.Count == 0) _agenda.Children.Add(Ui.Text("A clear day. Make it yours.", 12, Ui.Muted));
    }
    public void Dispose() => _state.Changed -= Refresh;
}
