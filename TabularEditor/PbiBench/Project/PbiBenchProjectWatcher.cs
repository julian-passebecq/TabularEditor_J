using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PbiBench.Core.Project;

namespace TabularEditor.PbiBench.Project
{
    internal enum ProjectWatcherState { NotStarted, Watching, RootMissing, Disposed }

    internal sealed class ProjectWatchSignal
    {
        internal ProjectWatchSignal(IReadOnlyList<string> paths, int eventCount, bool overflow, bool pathsTruncated, bool rootMissing = false)
        { Paths = paths; EventCount = eventCount; Overflow = overflow; PathsTruncated = pathsTruncated; RootMissing = rootMissing; }
        /// <summary>The watched folder was deleted or moved: the watcher stopped (state <see cref="ProjectWatcherState.RootMissing"/>).</summary>
        internal bool RootMissing { get; }
        /// <summary>Relative paths seen (bounded); a hint only. Consumers always rescan the manifest.</summary>
        internal IReadOnlyList<string> Paths { get; }
        internal int EventCount { get; }
        /// <summary>The OS buffer overflowed or the watcher errored: events were lost, a full rescan is required.</summary>
        internal bool Overflow { get; }
        internal bool PathsTruncated { get; }
    }

    /// <summary>
    /// Debounced FileSystemWatcher over a project folder. Bursts of events (editor saves, git
    /// checkouts) collapse into one <see cref="Settled"/> signal after a quiet period. Overflow or
    /// watcher errors are reported as a rescan signal instead of being silently dropped. Events in
    /// ignored folders (.git, bin, obj, ...) and plan temp files are filtered. The callback runs on a
    /// thread-pool thread; UI consumers marshal it themselves.
    /// </summary>
    internal sealed class PbiBenchProjectWatcher : IDisposable
    {
        internal const int MaxPaths = 200;
        private readonly object _gate = new object();
        private readonly string _root;
        private readonly TimeSpan _debounce;
        private readonly HashSet<string> _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher _watcher;
        private Timer _timer;
        private int _events;
        private bool _overflow;
        private bool _truncated;
        private bool _rootMissing;
        private bool _disposed;
        private ProjectWatcherState _state = ProjectWatcherState.NotStarted;
        private int _restarts;

        internal PbiBenchProjectWatcher(string root, TimeSpan debounce)
        {
            _root = ReviewedFilePlan.NormalizeRoot(root);
            if (!Directory.Exists(_root)) throw new DirectoryNotFoundException("The project folder does not exist.");
            _debounce = debounce < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : debounce;
            _timer = new Timer(OnQuiet, null, Timeout.Infinite, Timeout.Infinite);
        }

        internal event Action<ProjectWatchSignal> Settled;
        internal string Root => _root;
        internal ProjectWatcherState State { get { lock (_gate) return _state; } }
        /// <summary>How many times the underlying FileSystemWatcher was recreated after an error.</summary>
        internal int Restarts { get { lock (_gate) return _restarts; } }

        internal void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PbiBenchProjectWatcher));
                if (_watcher != null) return;
                _watcher = CreateWatcher();
                _state = ProjectWatcherState.Watching;
            }
        }

        private FileSystemWatcher CreateWatcher()
        {
            var watcher = new FileSystemWatcher(_root) {
                IncludeSubdirectories = true, InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += OnEvent; watcher.Created += OnEvent; watcher.Deleted += OnEvent;
            watcher.Renamed += (s, e) => { Record(e.OldFullPath); OnEvent(s, e); };
            watcher.Error += (s, e) => OnError(s as FileSystemWatcher, e.GetException());
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        /// <summary>Same path as a real watcher error (used by the host smoke): non-overflow errors recreate the watcher.</summary>
        internal void InjectError(Exception error) => OnError(null, error);

        /// <summary>
        /// Overflow: events were lost, signal a rescan and keep watching. Any other error (network drop, root deleted or moved,
        /// handle invalidated) leaves the FileSystemWatcher dead, so it is recreated once per error when the root still
        /// exists; when the root is gone the watcher stops with <see cref="ProjectWatcherState.RootMissing"/> and says so.
        /// </summary>
        private void OnError(FileSystemWatcher source, Exception error)
        {
            if (error is InternalBufferOverflowException) { InjectOverflow(); return; }
            lock (_gate)
            {
                if (_disposed) return;
                if (source != null && !ReferenceEquals(source, _watcher)) return; // a late error from a watcher already replaced
                if (_watcher != null) { try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } catch (Exception) { } _watcher = null; }
                bool exists;
                try { exists = Directory.Exists(_root); } catch (Exception) { exists = false; }
                if (exists)
                {
                    try { _watcher = CreateWatcher(); _restarts++; _state = ProjectWatcherState.Watching; }
                    catch (Exception)
                    {
                        // Runs on a watcher thread: never throw. A root pending deletion cannot be watched again: report it as missing.
                        _rootMissing = true; _state = ProjectWatcherState.RootMissing;
                    }
                }
                else { _rootMissing = true; _state = ProjectWatcherState.RootMissing; }
                _overflow = true; _events++;
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Same path as a real buffer overflow (also used by the host smoke).</summary>
        internal void InjectOverflow()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _overflow = true; _events++;
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnEvent(object sender, FileSystemEventArgs e)
        {
            // A directory's LastWrite "Changed" only echoes child events that are reported on their
            // own, and Windows can deliver it late (after Start for writes made just before).
            if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
            lock (_gate)
            {
                if (_disposed || !Record(e.FullPath)) return;
                _events++;
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }

        private bool Record(string fullPath)
        {
            lock (_gate)
            {
                if (fullPath == null || fullPath.Length <= _root.Length + 1 ||
                    !fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
                var relative = fullPath.Substring(_root.Length + 1).Replace('\\', '/');
                if (FileManifest.IsIgnoredPath(relative)) return false;
                if (_paths.Count < MaxPaths) _paths.Add(relative); else _truncated = true;
                return true;
            }
        }

        private void OnQuiet(object state)
        {
            ProjectWatchSignal signal;
            Action<ProjectWatchSignal> handler;
            lock (_gate)
            {
                if (_disposed || _events == 0) return;
                signal = new ProjectWatchSignal(new List<string>(_paths), _events, _overflow, _truncated, _rootMissing);
                _paths.Clear(); _events = 0; _overflow = false; _truncated = false; _rootMissing = false;
                handler = Settled;
            }
            try { handler?.Invoke(signal); }
            catch (Exception) { /* a consumer failure must not kill the timer thread */ }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _state = ProjectWatcherState.Disposed;
                if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); _watcher = null; }
                _timer.Dispose();
                Settled = null;
            }
        }
    }
}
