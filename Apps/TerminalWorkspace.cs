using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class TerminalTab(string title, Control content, string kind)
{
    public string Title { get; set; } = title;
    public Control Content { get; } = content;
    public string Kind { get; } = kind;
}

public sealed class TerminalView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly Action<string> _open;
    private readonly StackPanel _strip = Ui.Row(3);
    private readonly ContentControl _body = new();
    private readonly TextBlock _mode = Ui.Label("", Ui.Muted);
    private readonly ContextMenu _sessionMenu = TerminalMenu();
    public List<TerminalTab> Tabs { get; } = [];
    public TerminalTab? ActiveTab { get; private set; }
    public TerminalView(SystemState state, Action<string> open)
    {
        _state = state; _open = open; Background = Ui.Dark;
        var add = ToolbarButton("plus", "New PowerShell · Ctrl+Shift+T", () => AddHost()); add.Name = "NewTerminal";
        var more = ToolbarButton("chevron-down", "Terminal sessions", () => { }); more.Name = "TerminalMenu";
        var menu = _sessionMenu; menu.Placement = PlacementMode.BottomEdgeAlignedRight; menu.VerticalOffset = 6;
        void Item(string title, string icon, Action action, KeyGesture? gesture = null)
        { var item = new MenuItem { Header = title, Icon = new Glyph(icon) { Width = 16, Height = 16, Color = Ui.Ink }, InputGesture = gesture }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        void Section(string title) { var item = new MenuItem { Header = Ui.Label(title), IsEnabled = false }; item.Classes.Add("menu-section"); menu.Items.Add(item); }
        more.Click += (_, _) =>
        {
            menu.Items.Clear(); Section("NEW SESSION");
            Item("PowerShell", "terminal", () => AddHost(), new KeyGesture(Key.T, KeyModifiers.Control | KeyModifiers.Shift));
            Item("Command Prompt", "terminal", () => AddHost(shell: "cmd")); Item("Department shell", "refinement", () => AddDepartment());
            menu.Items.Add(new Separator()); Section("OPEN SESSIONS");
            foreach (var tab in Tabs) Item(tab.Title, tab == ActiveTab ? "check" : "terminal", () => Activate(tab));
            menu.Open(more);
        };
        menu.Opened += (_, _) => { more.Classes.Add("menu-open"); Motion.Enter(menu, 4); };
        menu.Closed += (_, _) => more.Classes.Remove("menu-open");
        var scroller = new ScrollViewer { Content = _strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var controls = Ui.Row(2, add, more); controls.VerticalAlignment = VerticalAlignment.Center; controls.Margin = new Thickness(8, 0, 0, 0);
        _strip.VerticalAlignment = VerticalAlignment.Center;
        var toolbar = new Border { Name = "TerminalToolbar", Background = Brush.Parse("#18363D"), BorderBrush = Brush.Parse("#35545A"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0), Child = Ui.Columns("*,Auto", scroller, controls) };
        var root = new Grid { RowDefinitions = new RowDefinitions("48,*,29") }; root.Children.Add(toolbar); Grid.SetRow(_body, 1); root.Children.Add(_body);
        var status = new Border { Background = Brush.Parse("#18363D"), Padding = new Thickness(16, 0), Child = Ui.Columns("*,Auto", _mode, Ui.Label("CTRL+TAB  SWITCH", Ui.Muted)) }; Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        AddDepartment(); AddHost(activate: false);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.T) { AddHost(); e.Handled = true; }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.W && ActiveTab is { } tab) { CloseTab(tab); e.Handled = true; }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Tab && Tabs.Count > 0) { var i = Tabs.IndexOf(ActiveTab!); Activate(Tabs[(i + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Tabs.Count - 1 : 1)) % Tabs.Count]); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    public TerminalTab AddDepartment() { var tab = new TerminalTab("Department", new DepartmentTerminalView(_state, _open), "Department"); Tabs.Add(tab); Activate(tab); return tab; }
    public TerminalTab AddHost(string? directory = null, string shell = "powershell", bool activate = true, string? initialCommand = null)
    {
        var name = shell == "cmd" ? "Command Prompt" : "PowerShell"; var count = Tabs.Count(x => x.Kind == shell) + 1;
        var tab = new TerminalTab(count > 1 ? name + " " + count : name, new HostTerminalView(directory, shell, initialCommand), shell);
        Tabs.Add(tab); if (activate) Activate(tab); else RenderTabs(); return tab;
    }
    public void Select(bool host) => Activate(Tabs.First(x => (x.Content is HostTerminalView) == host));
    public void Activate(TerminalTab tab)
    {
        if (!Tabs.Contains(tab)) return; ActiveTab = tab; _body.Content = tab.Content; RenderTabs(); Motion.Enter(_body, 5);
        if (tab.Content is HostTerminalView host) host.Start(); else if (tab.Content is DepartmentTerminalView department) department.Input.Focus();
    }
    public void CloseTab(TerminalTab tab)
    {
        var index = Tabs.IndexOf(tab); if (index < 0) return;
        if (ActiveTab == tab) _body.Content = null; (tab.Content as IDisposable)?.Dispose(); Tabs.Remove(tab);
        if (Tabs.Count == 0) { ActiveTab = null; _body.Content = Ui.Pad(Ui.Stack(18, Ui.Text("A new line of work.", 25, Ui.Phosphor), Ui.Button("New PowerShell terminal", () => AddHost(), "primary")), 32); RenderTabs(); }
        else if (ActiveTab == tab) Activate(Tabs[Math.Min(index, Tabs.Count - 1)]); else RenderTabs();
    }
    public void RenameTab(TerminalTab tab, string name) { if (!string.IsNullOrWhiteSpace(name)) { tab.Title = name.Trim()[..Math.Min(name.Trim().Length, 40)]; RenderTabs(); } }
    private static Button ToolbarButton(string glyph, string tip, Action action)
    {
        var button = Ui.Button("", action, "dark"); button.Classes.Add("terminal-action");
        button.Content = new Glyph(glyph) { Width = 15, Height = 15, Color = Ui.Phosphor, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        button.Width = 32; button.Height = 32; button.MinHeight = 0; button.Padding = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center; ToolTip.SetTip(button, tip); return button;
    }
    private static ContextMenu TerminalMenu() => new() { MinWidth = 264 };
    private void Rename(TerminalTab tab)
    {
        var input = new TextBox { Text = tab.Title, MaxLength = 40, MinWidth = 220 };
        var popup = new Flyout(); popup.Content = Ui.Stack(12, Ui.Label("SESSION NAME"), input, Ui.Button("Rename", () => { RenameTab(tab, input.Text ?? ""); popup.Hide(); }, "primary")); popup.ShowAt(this); input.Focus(); input.SelectAll();
    }
    private void RenderTabs()
    {
        _strip.Children.Clear();
        foreach (var tab in Tabs)
        {
            var label = Ui.Text(tab.Title, 12, Ui.Phosphor); label.MaxWidth = 145; label.TextTrimming = TextTrimming.CharacterEllipsis;
            var select = Ui.Button("", () => Activate(tab), "dark"); select.Content = label; select.Padding = new Thickness(12, 0, 8, 0); select.Height = 34; select.MinHeight = 0; select.VerticalContentAlignment = VerticalAlignment.Center;
            var close = ToolbarButton("close", "Close session · Ctrl+Shift+W", () => CloseTab(tab)); close.Classes.Add("terminal-close"); close.Width = 22; close.Height = 22; close.Margin = new Thickness(0, 0, 5, 0); ((Glyph)close.Content!).Width = 10; ((Glyph)close.Content!).Height = 10;
            select.Classes.Add("terminal-tab-button"); close.Classes.Add("terminal-tab-button");
            var strip = new Border { Name = "TerminalTabChrome", Height = 36, BorderBrush = tab == ActiveTab ? Ui.Phosphor : Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2), Child = Ui.Row(0, select, close) };
            strip.Classes.Add("terminal-tab"); if (tab == ActiveTab) strip.Classes.Add("selected");
            var context = TerminalMenu(); var rename = new MenuItem { Header = "Rename session" }; rename.Click += (_, _) => Rename(tab); context.Items.Add(rename);
            var move = new MenuItem { Header = "Move tab left", IsEnabled = Tabs.IndexOf(tab) > 0 }; move.Click += (_, _) => { var i = Tabs.IndexOf(tab); Tabs.RemoveAt(i); Tabs.Insert(i - 1, tab); RenderTabs(); }; context.Items.Add(move);
            var remove = new MenuItem { Header = "Close session" }; remove.Click += (_, _) => CloseTab(tab); context.Items.Add(remove); strip.ContextMenu = context; _strip.Children.Add(strip);
        }
        _mode.Text = $"{Tabs.Count:00} TABS  /  {(ActiveTab?.Content is HostTerminalView ? "● HOST MACHINE" : "DEPARTMENT ENVIRONMENT")}";
        _mode.Foreground = ActiveTab?.Content is HostTerminalView ? Brush.Parse("#D8BA82") : Ui.Muted;
    }
    public void Dispose() { foreach (var tab in Tabs) (tab.Content as IDisposable)?.Dispose(); Tabs.Clear(); }
}
