using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using HammerOS.Core;
using HammerOS.Desktop;
using Microsoft.Web.WebView2.Core;

namespace HammerOS.Apps;

public static class PickFile
{
    public static async Task<string?> Open(Control view, string title, params string[] patterns)
    { var top = TopLevel.GetTopLevel(view); if (top is null) return null; var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = title, FileTypeFilter = patterns.Length == 0 ? null : [new(title) { Patterns = patterns }] }); return files.FirstOrDefault()?.TryGetLocalPath(); }
    public static async Task<string?> Save(Control view, string name)
    { var top = TopLevel.GetTopLevel(view); if (top is null) return null; return (await top.StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = name, ShowOverwritePrompt = true }))?.TryGetLocalPath(); }
    public static async Task<string?> Folder(Control view)
    { var top = TopLevel.GetTopLevel(view); if (top is null) return null; return (await top.StorageProvider.OpenFolderPickerAsync(new() { Title = "Choose folder" })).FirstOrDefault()?.TryGetLocalPath(); }
}
public sealed class CalculatorView : UserControl
{
    public TextBox Expression { get; } = new() { Watermark = "(12 + 8) * 3", Name = "CalculatorExpression", FontSize = 26 };
    private readonly TextBlock _result = Ui.Text("0", 36, Ui.Phosphor, true);
    public CalculatorView()
    {
        void Evaluate() => _result.Text = Calculator.TryEvaluate(Expression.Text ?? "", out var result) ? result : "Check expression";
        Expression.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Evaluate(); e.Handled = true; } };
        var keys = new Avalonia.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var key in new[] { "7", "8", "9", "/", "4", "5", "6", "*", "1", "2", "3", "-", "0", ".", "=", "+", "(", ")", "^", "C" }) { var b = Ui.Button(key, () => { if (key == "=") Evaluate(); else if (key == "C") { Expression.Text = ""; _result.Text = "0"; } else Expression.Text += key; }, key == "=" ? "primary" : "outline"); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.FontSize = 18; b.HorizontalContentAlignment = HorizontalAlignment.Center; b.Height = 48; b.Margin = new Thickness(3); keys.Children.Add(b); }
        Content = Ui.Scroll(Ui.Pad(Ui.Stack(18, Ui.Heading("CALCULATOR", "Make it add up.", "Arithmetic, parentheses, powers and remainder."), Expression, new Border { Background = Ui.Dark, Padding = new Thickness(20), Child = _result }, keys)));
    }
}
public sealed class ImageView : UserControl, IDisposable
{
    public static bool Supports(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = Ui.Text("Open an image or drop it here.", 12, Ui.Muted);
    private Bitmap? _bitmap; private double _zoom = 1; private int _rotation;
    public string? FilePath { get; private set; }
    public ImageView()
    {
        var root = new Grid { RowDefinitions = new("52,*,32") };
        root.Children.Add(Ui.Row(5, Ui.Button("Open image", async () => { if (await PickFile.Open(this, "Images", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp") is { } path) Load(path); }), Ui.Button("−", () => Zoom(.8)), Ui.Button("+", () => Zoom(1.25)), Ui.Button("Fit", () => { _image.Width = double.NaN; _image.Height = double.NaN; _zoom = 1; }), Ui.Button("Rotate", () => { _rotation = (_rotation + 90) % 360; _image.RenderTransform = new RotateTransform(_rotation); })));
        var scroll = new ScrollViewer { Content = _image, Background = Ui.Dark, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); Grid.SetRow(_status, 2); root.Children.Add(_status); Content = root;
        FileDrop.Attach(this, p => p.Length == 1 && Supports(p[0]), p => Load(p[0]));
    }
    private void Zoom(double factor) { if (_bitmap is null) return; _zoom = Math.Clamp(_zoom * factor, .1, 8); _image.Width = _bitmap.PixelSize.Width * _zoom; _image.Height = _bitmap.PixelSize.Height * _zoom; }
    public void Load(string path) { try { var bitmap = new Bitmap(path); var old = _bitmap; _bitmap = bitmap; _image.Source = bitmap; old?.Dispose(); FilePath = path; _status.Text = $"{Path.GetFileName(path)}  ·  {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}"; } catch (Exception ex) { _status.Text = ex.Message; } }
    public void Dispose() { _image.Source = null; _bitmap?.Dispose(); }
}
public sealed class PdfView : UserControl, INativeWorkspace, IDisposable
{
    public WebSurface Web { get; } = new(); public string? FilePath { get; private set; }
    private readonly TextBlock _status = Ui.Text("Open a PDF to read it here.", 12);
    public PdfView()
    {
        var root = new Grid { RowDefinitions = new("48,*,30") }; root.Children.Add(Ui.Row(10, Ui.Button("Open PDF", async () => { if (await PickFile.Open(this, "PDF document", "*.pdf") is { } path) Load(path); }), _status)); Grid.SetRow(Web, 1); root.Children.Add(Web); Content = root;
        Web.Ready += _ => { if (FilePath is { } path) Load(path); }; Web.Failed += error => _status.Text = error;
        FileDrop.Attach(this, p => p.Length == 1 && Path.GetExtension(p[0]).Equals(".pdf", StringComparison.OrdinalIgnoreCase), p => Load(p[0]));
    }
    public void Load(string path) { if (!File.Exists(path)) { _status.Text = "File not found."; return; } FilePath = Path.GetFullPath(path); _status.Text = Path.GetFileName(path); if (Web.Core is not { } core) return; core.SetVirtualHostNameToFolderMapping("pdf.hammer.local", Path.GetDirectoryName(FilePath)!, CoreWebView2HostResourceAccessKind.DenyCors); core.Navigate("https://pdf.hammer.local/" + Uri.EscapeDataString(Path.GetFileName(FilePath))); }
    public void SetWorkspaceActive(bool active) => Web.SetActive(active); public void Dispose() => Web.Dispose();
}
public sealed class ArchiveView : UserControl
{
    private readonly ListBox _entries = new(); private readonly TextBlock _status = Ui.Text("ZIP archives · no files are extracted until you choose a destination.", 12);
    public string? FilePath { get; private set; }
    public ArchiveView()
    {
        var root = new Grid { RowDefinitions = new("52,*,40") }; root.Children.Add(Ui.Row(8, Ui.Button("Open ZIP", async () => { if (await PickFile.Open(this, "ZIP archives", "*.zip") is { } path) Load(path); }), Ui.Button("Extract all", async () => { if (FilePath is null || await PickFile.Folder(this) is not { } folder) return; try { IsEnabled = false; await Task.Run(() => ArchiveFiles.Extract(FilePath, folder)); _status.Text = "Extracted to " + folder; } catch (Exception ex) { _status.Text = ex.Message; } finally { IsEnabled = true; } }), Ui.Button("Create from folder", async () => { if (await PickFile.Folder(this) is not { } folder || await PickFile.Save(this, Path.GetFileName(folder) + ".zip") is not { } path) return; try { IsEnabled = false; await Task.Run(() => ArchiveFiles.Create(path, [folder])); Load(path); } catch (Exception ex) { _status.Text = ex.Message; } finally { IsEnabled = true; } })));
        Grid.SetRow(_entries, 1); root.Children.Add(_entries); Grid.SetRow(_status, 2); root.Children.Add(_status); Content = root; FileDrop.Attach(this, p => p.Length == 1 && Path.GetExtension(p[0]).Equals(".zip", StringComparison.OrdinalIgnoreCase), p => Load(p[0]));
    }
    public void Load(string path) { try { using var zip = ZipFile.OpenRead(path); _entries.ItemsSource = zip.Entries.Select(x => $"{x.FullName}    {x.Length:N0} bytes").ToArray(); FilePath = path; _status.Text = Path.GetFileName(path) + "  ·  " + zip.Entries.Count + " entries"; } catch (Exception ex) { _status.Text = ex.Message; } }
}
public sealed class ScreenshotView : UserControl, IDisposable
{
    private readonly Image _preview = new() { Stretch = Stretch.Uniform }; private Bitmap? _image; private readonly TextBlock _status = Ui.Text("Capture the HammerOS window, including native apps.", 12);
    public ScreenshotView(Action<Action<Bitmap>> capture)
    {
        var root = new Grid { RowDefinitions = new("52,*,32") }; root.Children.Add(Ui.Row(8, Ui.Button("Capture in 3 seconds", () => capture(bitmap => { _image?.Dispose(); _image = bitmap; _preview.Source = bitmap; _status.Text = "Captured · choose Save PNG."; })), Ui.Button("Save PNG", async () => { if (_image is null || await PickFile.Save(this, "HammerOS-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png") is not { } path) return; try { _image.Save(path); _status.Text = "Saved to " + path; } catch (Exception ex) { _status.Text = ex.Message; } })));
        Grid.SetRow(_preview, 1); root.Children.Add(_preview); Grid.SetRow(_status, 2); root.Children.Add(_status); Content = root;
    }
    public void Dispose() => _image?.Dispose();
}
