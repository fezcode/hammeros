using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class IntranetView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly TextBox _address = new() { Text = "hammer.internal/home", Name = "BrowserAddress" };
    private readonly ContentControl _page = new();
    private readonly List<string> _history = [];
    private int _position = -1;
    private readonly Button _back, _forward;
    public string CurrentPage { get; private set; } = "home";
    public IntranetView(SystemState state)
    {
        _state = state;
        _back = Ui.IconButton("back", "Back", () => History(-1)); _forward = Ui.IconButton("arrow", "Forward", () => History(1));
        var bar = Ui.Columns("Auto,*,Auto", Ui.Row(0, _back, _forward, Ui.IconButton("refresh", "Reload", RenderPage)), _address, Ui.Button("Go", () => Navigate(_address.Text ?? "")));
        bar.Margin = new Thickness(10, 8); var root = new Grid { RowDefinitions = new RowDefinitions("55,*,27") }; root.Children.Add(bar); Grid.SetRow(_page, 1); root.Children.Add(_page);
        var status = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 0), Child = Ui.Label("INTERNAL PAGES ONLY  /  YOUR WINDOW INTO THE COMPANY") }; Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        _address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Navigate(_address.Text ?? ""); e.Handled = true; } };
        state.Changed += RenderPage; Navigate("hammer.internal/home");
    }
    public void Navigate(string address)
    {
        address = address.Trim(); if (!address.Contains('/')) address = address is "home" or "handbook" or "departments" or "announcements" ? "hammer.internal/" + address : address + "/home";
        address = address.Replace("https://", "", StringComparison.OrdinalIgnoreCase).Replace("http://", "", StringComparison.OrdinalIgnoreCase);
        if (_position < _history.Count - 1) _history.RemoveRange(_position + 1, _history.Count - _position - 1);
        _history.Add(address); _position = _history.Count - 1; _address.Text = address; RenderPage();
    }
    private void History(int direction) { var next = _position + direction; if (next < 0 || next >= _history.Count) return; _position = next; _address.Text = _history[next]; RenderPage(); }
    private void RenderPage()
    {
        _back.IsEnabled = _position > 0; _forward.IsEnabled = _position < _history.Count - 1;
        var address = _position >= 0 ? _history[_position] : "hammer.internal/home";
        if (!_state.Online) { _page.Content = Ui.Pad(Ui.Stack(22, Ui.Heading("CONNECTION UNAVAILABLE", "A temporary separation.", "Enable the network adapter and Intranet relay to reconnect."), Ui.Button("Try again", RenderPage, "primary")), 40); return; }
        if (!address.StartsWith("hammer.internal/", StringComparison.OrdinalIgnoreCase)) { _page.Content = Ui.Pad(Ui.Stack(22, Ui.Heading("OUTSIDE THE DEPARTMENT", "Your world is here.", "This browser hosts the fictional company intranet. External sites are not part of this environment."), Ui.Button("Return to the intranet", () => Navigate("home"), "primary")), 40); return; }
        CurrentPage = address.Split('/').ElementAtOrDefault(1)?.ToLowerInvariant() ?? "home";
        var stack = Ui.Stack(25);
        var brand = Ui.Text("Hammer", 27); brand.FontFamily = Ui.Serif; brand.FontWeight = FontWeight.Bold;
        stack.Children.Add(Ui.Columns("*,Auto", brand, Ui.Row(4, Ui.Button("Home", () => Navigate("home")), Ui.Button("Departments", () => Navigate("departments")), Ui.Button("Bulletin", () => Navigate("announcements")))));
        stack.Children.Add(Ui.Rule());
        switch (CurrentPage)
        {
            case "home":
                stack.Children.Add(Ui.Label("THE EMPLOYEE INTRANET"));
                stack.Children.Add(new TextBlock { Text = "The world outside\ncan wait.", FontFamily = Ui.Serif, FontSize = 48, Foreground = Ui.Ink, LineHeight = 54 });
                stack.Children.Add(Paragraph("Here, every detail has a purpose. Every person has a place. Welcome to your corner of Hammer Industries."));
                stack.Children.Add(Ui.Button("Explore your department  →", () => Navigate("departments"), "primary"));
                stack.Children.Add(Ui.Rule());
                stack.Children.Add(Ui.Columns("*,*", Ui.Stack(10, Ui.Label("01 / COMPANY NEWS"), Ui.Text("A productive quarter.", 21), Paragraph("The board extends its appreciation to all refiners.")), Ui.Stack(10, Ui.Label("02 / EMPLOYEE RESOURCES"), Ui.Text("A little guidance.", 21), Ui.Button("Read workspace principles →", () => Navigate("handbook"), "outline"))));
                break;
            case "departments":
                stack.Children.Add(Ui.Heading("OUR COLLECTIVE PURPOSE", "Many hands. One vision.", "Each department contributes in its own important way."));
                foreach (var (name, description) in new[] { ("Refinement", "Finding order. Bringing balance. Your assigned department."), ("Design Studio", "Carefully considered objects for a carefully considered world."), ("Wellness", "The continued harmony of our valued employees."), ("Administration", "A steady hand. An enduring commitment.") }) { stack.Children.Add(Ui.Stack(8, Ui.Text(name, 23), Paragraph(description))); stack.Children.Add(Ui.Rule()); }
                break;
            case "announcements":
                stack.Children.Add(Ui.Heading("THE DEPARTMENT BULLETIN", "Good things are happening.", "Approved updates for the workspace."));
                foreach (var (label, title, body) in new[] { ("MANAGEMENT", "Your contribution matters.", "Quarterly refinement is underway. Please complete your assigned bins at a considered and consistent pace."), ("WELLNESS", "A moment of calm.", "Sixty-second guided breathing sessions are available from the Wellness application."), ("FACILITIES", "Everything in its place.", "Please return all department records to their appropriate directory. An orderly archive is a gift to your colleagues.") }) stack.Children.Add(Ui.Stack(10, Ui.Label(label), Ui.Text(title, 23), Paragraph(body), Ui.Rule()));
                break;
            case "handbook":
                stack.Children.Add(Ui.Heading("WORKSPACE PRINCIPLES", "The foundation of our work.", "Let these qualities guide your every contribution."));
                foreach (var p in new[] { "Curiosity", "Clarity", "Care", "Craft", "Openness", "Patience", "Play" }) stack.Children.Add(Ui.Text(p, 24));
                break;
            default:
                stack.Children.Add(Ui.Heading("RECORD NOT FOUND / 404", "This corridor leads nowhere.", "The requested intranet page does not exist.")); stack.Children.Add(Ui.Button("Return home", () => Navigate("home"), "primary")); break;
        }
        stack.Children.Add(Ui.Rule()); stack.Children.Add(Ui.Label("HAMMER INDUSTRIES  ·  INTERNAL USE  ·  WORKSPACE"));
        _page.Content = Ui.Scroll(Ui.Pad(stack, 32)); Motion.Enter(_page, 7);
    }
    private static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 22, Foreground = Ui.Muted };
    public void Dispose() => _state.Changed -= RenderPage;
}
