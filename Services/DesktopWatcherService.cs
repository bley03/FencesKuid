using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace FencesWPF.Services
{
    /// <summary>
    /// Watches the Windows Desktop folder(s) for shortcut changes.
    /// When a .lnk is renamed, deleted, or added, notifies FenceManager
    /// so affected panels can refresh their icon/name.
    /// </summary>
    public sealed class DesktopWatcherService : IDisposable
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static DesktopWatcherService? _instance;
        public static DesktopWatcherService Instance =>
            _instance ??= new DesktopWatcherService();
        private DesktopWatcherService() { }

        // ── State ──────────────────────────────────────────────────────────────
        private FileSystemWatcher? _userWatcher;
        private FileSystemWatcher? _publicWatcher;
        private bool _disposed;

        // Debounce — desktop events can fire in bursts
        private System.Threading.Timer? _debounce;
        private const int DebounceMs = 1500;

        // ── Public API ─────────────────────────────────────────────────────────
        public void Start()
        {
            WatchFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                ref _userWatcher);

            // Also watch the public desktop (shared shortcuts)
            string publicDesktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            if (Directory.Exists(publicDesktop))
                WatchFolder(publicDesktop, ref _publicWatcher);
        }

        public void Stop()
        {
            _userWatcher?.Dispose();
            _publicWatcher?.Dispose();
            _debounce?.Dispose();
            _userWatcher   = null;
            _publicWatcher = null;
            _debounce      = null;
        }

        // ── Internal ──────────────────────────────────────────────────────────
        private void WatchFolder(string path, ref FileSystemWatcher? watcher)
        {
            try
            {
                watcher = new FileSystemWatcher(path)
                {
                    Filter                = "*.lnk",
                    NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents   = true
                };

                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error   += OnError;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DesktopWatcher] Could not watch {path}: {ex.Message}");
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e) =>
            ScheduleRefresh(e.FullPath, e.ChangeType);

        private void OnRenamed(object sender, RenamedEventArgs e) =>
            ScheduleRefresh(e.FullPath, WatcherChangeTypes.Renamed, e.OldFullPath);

        private void ScheduleRefresh(string path, WatcherChangeTypes type,
                                     string? oldPath = null)
        {
            _debounce ??= new System.Threading.Timer(_ =>
                Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => ProcessChange(path, type, oldPath))),
                null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            _debounce.Change(DebounceMs, System.Threading.Timeout.Infinite);
        }

        private static void ProcessChange(string path, WatcherChangeTypes type,
                                          string? oldPath)
        {
            foreach (var fence in FenceManager.Instance.Fences)
            {
                foreach (var sc in fence.Shortcuts.ToList())
                {
                    bool affected =
                        sc.Path.Equals(path,    StringComparison.OrdinalIgnoreCase) ||
                        sc.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase);

                    if (!affected) continue;

                    switch (type)
                    {
                        case WatcherChangeTypes.Deleted:
                            // Mark with a warning icon but don't auto-delete
                            // (user may have temporarily moved the file)
                            sc.Name = $"⚠ {sc.Name}";
                            fence.RefreshUI();
                            break;

                        case WatcherChangeTypes.Renamed when oldPath != null:
                            // Update path to new location
                            sc.Path = path;
                            sc.Name = Path.GetFileNameWithoutExtension(path);
                            fence.RefreshUI();
                            break;

                        case WatcherChangeTypes.Created:
                        case WatcherChangeTypes.Changed:
                            // Reload icon (the .ico may have updated)
                            fence.ReloadShortcutIcon(sc);
                            break;
                    }
                }
            }
        }

        private static void OnError(object sender, ErrorEventArgs e) =>
            System.Diagnostics.Debug.WriteLine(
                $"[DesktopWatcher] Error: {e.GetException().Message}");

        // ── Dispose ───────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
