using System.IO;
using Companion.ViewModels.Services;

namespace Companion.App.Services;

/// <summary>
/// FileSystemWatcher-backed <see cref="IFileWatcher"/> for the briefing screen's staged
/// custom-AI XML. Watches exactly one file at a time; raises <see cref="Changed"/> with the
/// watched path (change, create, delete, or rename all count as "touched"). Events are
/// marshalled onto the construction thread's SynchronizationContext (the UI thread — the
/// composition root constructs it there) so the viewmodel never sees cross-thread callbacks.
/// </summary>
public sealed class FileSystemFileWatcher : IFileWatcher, IDisposable
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private FileSystemWatcher? _watcher;
    private string? _filePath;

    public event EventHandler<string>? Changed;

    public void Watch(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        Stop();

        string fullPath = Path.GetFullPath(filePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return; // nothing to watch — a vanished directory is not worth crashing over

        _filePath = fullPath;
        var watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        watcher.Changed += OnFileEvent;
        watcher.Created += OnFileEvent;
        watcher.Deleted += OnFileEvent;
        watcher.Renamed += OnFileEvent;
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    public void Stop()
    {
        var watcher = _watcher;
        _watcher = null;
        _filePath = null;
        watcher?.Dispose();
    }

    public void Dispose() => Stop();

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Always report the watched path (renames still mean "the staged file was touched").
        string? path = _filePath;
        if (path is null)
            return;

        if (_context is not null)
            _context.Post(_ => Changed?.Invoke(this, path), null);
        else
            Changed?.Invoke(this, path);
    }
}
