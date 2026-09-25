using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HammerOS.Desktop;

namespace HammerOS.Apps;

public sealed class EditorView : UserControl
{
    public TextBox Editor { get; } = new() { Name = "TextEditor", AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, FontFamily = Ui.Mono, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(0), Padding = new Thickness(24, 18) };
    private readonly TextBlock _name = Ui.Text("Untitled", 23), _status = Ui.Label("", Ui.Phosphor);
    private readonly ContentControl _notice = new();
    private string _saved = "";
    private Encoding _encoding = new UTF8Encoding(false, true);
    private DateTime _stamp;
    public string? FilePath { get; private set; }
    public bool IsDirty => (Editor.Text ?? "") != _saved;
    public EditorView()
    {
        Background = Ui.Cream; Editor.Classes.Add("document"); _name.FontFamily = Ui.Serif;
        FileDrop.Attach(this, paths => paths.Length == 1 && File.Exists(paths[0]), paths => OpenDroppedFile(paths[0]));
        var root = new Grid { RowDefinitions = new RowDefinitions("60,42,Auto,*,32") };
        root.Children.Add(new Border { Padding = new Thickness(22, 0), Child = Ui.Columns("*,Auto", _name, Ui.Label("TEXT / EDITOR")) });
        var wrap = Ui.Button("Wrap: off", () => { }); wrap.Click += (_, _) => { var enabled = Editor.TextWrapping != TextWrapping.Wrap; Editor.TextWrapping = enabled ? TextWrapping.Wrap : TextWrapping.NoWrap; wrap.Content = enabled ? "Wrap: on" : "Wrap: off"; wrap.Background = enabled ? Brush.Parse("#CEDBCB") : Brushes.Transparent; }; ToolTip.SetTip(wrap, "Toggle word wrap");
        var actions = Ui.Row(2, Ui.Button("New", () => ConfirmClose(NewDocument)), Ui.Button("Open", () => ConfirmClose(async () => await OpenPicker())), Ui.Button("Save", async () => await SaveAsync()), Ui.Button("Save as", async () => await SaveAsync(true)));
        var toolbar = new Border { Padding = new Thickness(10, 3), Background = Brush.Parse("#DFE4D6"), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 1), Child = Ui.Columns("*,Auto", actions, wrap) }; Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        Grid.SetRow(_notice, 2); root.Children.Add(_notice); Grid.SetRow(Editor, 3); root.Children.Add(Editor);
        var status = new Border { Padding = new Thickness(22, 0), Background = Tint.Bar, BorderBrush = Tint.DarkLine, BorderThickness = new Thickness(0, 1, 0, 0), Child = _status }; Grid.SetRow(status, 4); root.Children.Add(status); Content = root;
        Editor.TextChanged += (_, _) => Refresh(); Editor.PropertyChanged += (_, e) => { if (e.Property == TextBox.CaretIndexProperty) Refresh(); };
        AddHandler(KeyDownEvent, async (_, e) => { if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return; if (e.Key == Key.S) { e.Handled = true; await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); } else if (e.Key == Key.O) { e.Handled = true; ConfirmClose(async () => await OpenPicker()); } else if (e.Key == Key.N) { e.Handled = true; ConfirmClose(NewDocument); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel); Refresh();
    }
    private void Refresh()
    {
        _name.Text = (FilePath is null ? "Untitled" : Path.GetFileName(FilePath)) + (IsDirty ? "  •" : ""); _name.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = Editor.Text ?? ""; var before = text[..Math.Min(Editor.CaretIndex, text.Length)]; var line = before.Count(c => c == '\n') + 1; var column = before.Length - before.LastIndexOf('\n');
        _status.Text = $"LN {line}  COL {column}    ·    {text.Length:N0} CHARACTERS    ·    UTF-8";
    }
    private void NewDocument() { FilePath = null; _saved = ""; _encoding = new UTF8Encoding(false, true); Editor.Text = ""; _notice.Content = null; Refresh(); Editor.Focus(); }
    public void OpenDroppedFile(string path) => ConfirmClose(async () => await LoadFileAsync(path));
    public async Task LoadFileAsync(string path)
    {
        try { var info = new FileInfo(path); if (info.Length > 8 * 1024 * 1024) throw new IOException("Choose a text file smaller than 8 MB."); var bytes = await File.ReadAllBytesAsync(path); var bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }); var encoding = new UTF8Encoding(bom, true); var text = encoding.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); if (text.Contains('\0')) throw new IOException("Choose a UTF-8 text file."); FilePath = path; _stamp = File.GetLastWriteTimeUtc(path); _encoding = encoding; _saved = text; Editor.Text = text; _notice.Content = null; Refresh(); }
        catch (Exception ex) { Error(ex.Message); }
    }
    private async Task OpenPicker() { var top = TopLevel.GetTopLevel(this); if (top is null) return; var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open text document", AllowMultiple = false }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadFileAsync(path); }
    public async Task<bool> SaveToAsync(string path)
    {
        try { if (path == FilePath && File.GetLastWriteTimeUtc(path) != _stamp) throw new IOException("The file changed on disk. Save as a new file or reopen it."); var text = Editor.Text ?? ""; await File.WriteAllTextAsync(path, text, _encoding); FilePath = path; _saved = text; _stamp = File.GetLastWriteTimeUtc(path); _notice.Content = null; Refresh(); return true; }
        catch (Exception ex) { Error(ex.Message); return false; }
    }
    private async Task<bool> SaveAsync(bool saveAs = false)
    {
        var path = FilePath;
        if (saveAs || path is null) { var top = TopLevel.GetTopLevel(this); if (top is null) return false; var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save text document", SuggestedFileName = path is null ? "untitled.txt" : Path.GetFileName(path), DefaultExtension = "txt", ShowOverwritePrompt = true }); path = file?.TryGetLocalPath(); }
        return path is not null && await SaveToAsync(path);
    }
    private void Error(string message) { var text = Ui.Text(message, 12, Brush.Parse("#99513D")); text.TextWrapping = TextWrapping.Wrap; _notice.Content = Ui.Pad(text, 12); }
    public void ConfirmClose(Action proceed)
    {
        if (!IsDirty) { proceed(); return; }
        _notice.Content = Ui.Pad(Ui.Stack(10, Ui.Text("Save changes to this document?", 14), Ui.Row(8, Ui.Button("Cancel", () => _notice.Content = null, "outline"), Ui.Button("Discard", () => { _notice.Content = null; proceed(); }), Ui.Button("Save", async () => { if (await SaveAsync()) proceed(); }, "primary"))), 16);
    }
}
