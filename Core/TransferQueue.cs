namespace HammerOS.Core;

public sealed class TransferJob
{
    public List<FileUndo> UndoItems { get; } = [];
    public bool Undone { get; set; }
    public string Label { get; init; } = ""; public string Status { get; set; } = "Queued"; public int Done { get; set; } public int Total { get; init; } public bool Finished { get; set; } public CancellationTokenSource Cancellation { get; } = new();
}
public sealed class TransferQueue
{
    public bool IsBusy => _gate.CurrentCount == 0 || Jobs.Any(x => !x.Finished);
    public bool CanUndo => Jobs.Any(x => x.Finished && !x.Undone && x.UndoItems.Count > 0);
    public async Task UndoLast()
    {
        var job = Jobs.FirstOrDefault(x => x.Finished && !x.Undone && x.UndoItems.Count > 0); if (job is null) return;
        await _gate.WaitAsync();
        try { await Task.Run(() => { foreach (var item in job.UndoItems) item.Verify(); foreach (var item in job.UndoItems.AsEnumerable().Reverse().ToArray()) { item.Undo(); job.UndoItems.Remove(item); } }); job.Undone = true; job.Status = "Undone"; }
        finally { _gate.Release(); Changed?.Invoke(); }
    }
    private readonly SemaphoreSlim _gate = new(1); public List<TransferJob> Jobs { get; } = []; public event Action? Changed;
    public async Task Enqueue(string[] sources, string destination, bool move)
    {
        var job = new TransferJob { Label = (move ? "Move" : "Copy") + " to " + destination, Total = sources.Length }; Jobs.Insert(0, job); Changed?.Invoke();
        try
        {
            await _gate.WaitAsync(job.Cancellation.Token);
            try
            {
                job.Status = "Working"; Changed?.Invoke();
                foreach (var path in sources) { job.Cancellation.Token.ThrowIfCancellationRequested(); await Task.Run(() => { var folder = Directory.Exists(path); FileSystemInfo info = folder ? new DirectoryInfo(path) : new FileInfo(path); LocalFiles.Transfer(new(path, info.Name, folder, folder ? 0 : ((FileInfo)info).Length, info.LastWriteTime, info.Attributes.HasFlag(FileAttributes.ReparsePoint)), destination, move); try { job.UndoItems.Add(new(path, Path.Combine(destination, info.Name), move)); } catch (IOException) { /* Transfer succeeded, but a changed or unavailable item cannot safely be undone. */ } }); job.Done++; Changed?.Invoke(); }
                job.Status = "Complete";
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { job.Status = "Cancelled · completed items kept"; }
        catch (Exception ex) { job.Status = "Stopped: " + ex.Message; }
        finally { job.Finished = true; Changed?.Invoke(); }
    }
}
