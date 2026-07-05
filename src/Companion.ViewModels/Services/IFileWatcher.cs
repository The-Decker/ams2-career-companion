namespace Companion.ViewModels.Services;

/// <summary>
/// Abstraction over file-change monitoring so viewmodels stay WPF- and IO-free. The App
/// layer provides a FileSystemWatcher-backed implementation; tests raise
/// <see cref="Changed"/> directly. The briefing screen watches the staged custom-AI XML and
/// flags "modified outside the app" when it changes after staging.
/// </summary>
public interface IFileWatcher
{
    /// <summary>Raised with the full path of a file that changed while being watched.</summary>
    event EventHandler<string>? Changed;

    /// <summary>Start (or re-target) watching one file. Replaces any previous watch.</summary>
    void Watch(string filePath);

    void Stop();
}
