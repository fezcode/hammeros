using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HammerOS.Desktop;

public static class FileDrop
{
    private const string InternalPaths = "HammerOS.FilePaths";
    public static string[] Paths(IDataObject data) => data.Get(InternalPaths) is string[] paths ? paths : data.GetFiles()?.Select(x => x.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
    public static void Attach(Control target, Func<string[], bool> accepts, Action<string[]> open)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) => { var allowed = accepts(Paths(e.Data)); e.DragEffects = allowed ? DragDropEffects.Copy : DragDropEffects.None; target.Classes.Set("file-drop", allowed); e.Handled = true; }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => target.Classes.Remove("file-drop"));
        target.AddHandler(DragDrop.DropEvent, (_, e) => { target.Classes.Remove("file-drop"); var paths = Paths(e.Data); if (accepts(paths)) { e.DragEffects = DragDropEffects.Copy; open(paths); } else e.DragEffects = DragDropEffects.None; e.Handled = true; }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }
    public static async Task Start(Control source, PointerEventArgs trigger, string path)
    {
        var provider = TopLevel.GetTopLevel(source)?.StorageProvider; if (provider is null) return;
        IStorageItem? item = Directory.Exists(path) ? await provider.TryGetFolderFromPathAsync(new Uri(path)) : await provider.TryGetFileFromPathAsync(new Uri(path));
        if (item is null) return;
        using (item) { var data = new DataObject(); data.Set(DataFormats.Files, new[] { item }); data.Set(InternalPaths, new[] { path }); await DragDrop.DoDragDrop(trigger, data, DragDropEffects.Copy); }
    }
}
