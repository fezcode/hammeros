using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class FilesView : UserControl, IDisposable
{
    private readonly SystemState _state;
    private readonly Action<string> _toast;
    private readonly StackPanel _list = Ui.Stack(0);
    private readonly TextBlock _path = Ui.Text("", 12, Ui.Ink, true), _count = Ui.Label("");
    private readonly ContentControl _body = new();
    private readonly ScrollViewer _fileScroll;
    private readonly TextBox _search = new() { Watermark = "Filter files…", Width = 150 };
    private string _cwd = "/department";
    private string? _editing;
    public FilesView(SystemState state, Action<string> toast)
    {
        _state = state; _toast = toast;
        var nav = Ui.Stack(9, Ui.Label("LOCATIONS")); nav.Margin = new Thickness(18, 26);
        foreach (var (name, path) in new[] { ("Department", "/department"), ("Refinement", "/department/refinement"), ("Handbook", "/department/handbook"), ("Personal", "/personal"), ("System", "/system") })
        { var b = Ui.Button(name, () => Navigate(path)); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; nav.Children.Add(b); }
        nav.Children.Add(Ui.Rule()); nav.Children.Add(Ui.Label("VIRTUAL DRIVE")); nav.Children.Add(Ui.Text("H:  /  MDR-04", 11, Ui.Muted, true));
        var sidebar = new Border { Width = 158, Background = Brush.Parse("#DFE4D6"), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };
        var main = new Grid { RowDefinitions = new RowDefinitions("55,*,42") };
        var toolbar = Ui.Columns("Auto,*,Auto", Ui.IconButton("back", "Parent directory", () => Navigate(SystemState.Parent(_cwd))), _path, _search); toolbar.Margin = new Thickness(12, 6); main.Children.Add(toolbar);
        _fileScroll = Ui.Scroll(_list); _body.Content = _fileScroll; Grid.SetRow(_body, 1); main.Children.Add(_body);
        var actions = Ui.Row(3, Ui.Button("+ File", () => Create(false)), Ui.Button("+ Folder", () => Create(true)));
        var foot = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(17, 0), Child = Ui.Columns("*,Auto", _count, actions) }; Grid.SetRow(foot, 2); main.Children.Add(foot);
        Content = Ui.Columns("158,*", sidebar, main); _search.TextChanged += (_, _) => RenderList(); state.Changed += OnStateChanged; RenderList();
    }
    private void OnStateChanged() { if (_editing is null) RenderList(); }
    public void Navigate(string path) { if (_state.Find(path)?.IsDirectory != true) return; _cwd = path; _editing = null; _search.Text = ""; _body.Content = _fileScroll; RenderList(); Motion.Enter(_list, 5); }
    private void RenderList()
    {
        if (_editing is not null) return;
        _path.Text = "H: " + _cwd; _list.Children.Clear();
        var header = Ui.Columns("*,95,75", Ui.Label("NAME"), Ui.Label("MODIFIED"), Ui.Label("KIND")); header.Margin = new Thickness(24, 12); _list.Children.Add(header); _list.Children.Add(Ui.Rule());
        var files = _state.List(_cwd).Where(x => x.Name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray(); _count.Text = $"{files.Length:00} ITEMS  ·  INTERNAL";
        foreach (var file in files)
        {
            var left = Ui.Row(13, new Glyph(file.IsDirectory ? "folder" : "notes") { Width = 20, Height = 20 }, Ui.Text(file.Name, 12));
            var row = Ui.Columns("*,95,75", left, Ui.Text(file.Modified.ToString("dd MMM"), 11, Ui.Muted, true), Ui.Text(file.IsDirectory ? "Folder" : "Record", 11, Ui.Muted));
            var b = Ui.Button("", () => { if (file.IsDirectory) Navigate(file.Path); else OpenFile(file); }); b.Content = row; b.Padding = new Thickness(24, 16); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Stretch; _list.Children.Add(b); _list.Children.Add(Ui.Rule(Brush.Parse("#D8DECF")));
        }
        if (files.Length == 0) _list.Children.Add(Ui.Pad(Ui.Text("No records in this location.", 13, Ui.Muted), 30));
    }
    private void OpenFile(VirtualFile file)
    {
        _editing = file.Path; _path.Text = "H: " + file.Path;
        var editor = new TextBox { Text = file.Content, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = Ui.Mono, FontSize = 12, IsReadOnly = file.Path.StartsWith("/system"), VerticalAlignment = VerticalAlignment.Stretch };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        var label = Ui.Label(file.Path.StartsWith("/system") ? "PROTECTED SYSTEM RECORD" : "RECORD EDITOR"); label.Margin = new Thickness(0, 0, 0, 12); grid.Children.Add(label); Grid.SetRow(editor, 1); grid.Children.Add(editor);
        var save = Ui.Button("Save record", () => _toast(_state.Write(file.Path, editor.Text ?? "")), "primary"); save.IsEnabled = !editor.IsReadOnly;
        var remove = Ui.Button("Delete record", () => { var confirm = Ui.Stack(18, Ui.Text("Remove this virtual record?", 18), Ui.Text(file.Name, 12), Ui.Row(12, Ui.Button("Keep record", () => OpenFile(file), "outline"), Ui.Button("Delete", () => { _toast(_state.Delete(file.Path)); Navigate(_cwd); }, "primary"))); _body.Content = Ui.Pad(confirm); }); remove.IsEnabled = !editor.IsReadOnly;
        var row = Ui.Row(12, Ui.Button("Back", () => Navigate(_cwd), "outline"), save, remove); row.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(row, 2); grid.Children.Add(row); _body.Content = grid; Motion.Enter(grid, 6);
    }
    private void Create(bool directory)
    {
        var name = new TextBox { Watermark = directory ? "Folder name" : "record.txt" }; var error = Ui.Text("", 12, Brush.Parse("#99513D"));
        var form = Ui.Stack(16, Ui.Heading("ARCHIVE MANAGEMENT", directory ? "A new folder." : "A new record.", "Create an item in " + _cwd), name, error);
        form.Children.Add(Ui.Row(12, Ui.Button("Cancel", () => Navigate(_cwd), "outline"), Ui.Button("Create", () =>
        {
            var value = name.Text?.Trim(); if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['/', '\\', ':', '\0']) >= 0 || value is "." or "..") { error.Text = "Enter a valid name without path separators."; return; }
            var path = SystemState.Normalize(value, _cwd); if (_state.Find(path) is not null) { error.Text = "This name already exists."; return; }
            var result = _state.Write(path, "", directory); if (result != "Saved.") { error.Text = result; return; } Navigate(_cwd); _toast(directory ? "Folder created." : "Record created.");
        }, "primary")));
        _editing = "creating"; _body.Content = Ui.Pad(form); name.Focus();
    }
    public void Dispose() => _state.Changed -= OnStateChanged;
}

public sealed class NotesView : UserControl
{
    public NotesView(SystemState state, Action<string> toast)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(30) };
        var heading = Ui.Heading("PERSONAL ARCHIVE  /  001", "Memoranda", "A place for thoughts that belong at work."); heading.Margin = new Thickness(0, 0, 22, 22); grid.Children.Add(heading);
        var text = new TextBox { Text = state.Find("/personal/notes.txt")?.Content ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = Ui.Serif, FontSize = 19, Padding = new Thickness(20), Name = "NoteEditor" }; Grid.SetRow(text, 1); grid.Children.Add(text);
        var status = Ui.Label("CHANGES ARE SAVED WHEN YOU PRESS SAVE.");
        var save = Ui.Button("Save memorandum", () => { var result = state.Write("/personal/notes.txt", text.Text ?? ""); status.Text = result == "Saved." ? "SAVED AT " + DateTime.Now.ToString("HH:mm") : result; toast(result); }, "primary");
        var footer = Ui.Columns("*,Auto", status, save); footer.Margin = new Thickness(0, 18, 0, 0); Grid.SetRow(footer, 2); grid.Children.Add(footer); Content = grid;
    }
}
