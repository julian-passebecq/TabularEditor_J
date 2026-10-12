using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PbiBench.Core.Project;

namespace TabularEditor.PbiBench.Project
{
    internal sealed class ProjectWatchSignal
    {
        internal ProjectWatchSignal(IReadOnlyList<string> paths, int eventCount, bool overflow, bool pathsTruncated)
        { Paths = paths; EventCount = eventCount; Overflow = overflow; PathsTruncated = pathsTruncated; }
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
        private bool _disposed;

        internal PbiBenchProjectWatcher(string root, TimeSpan debounce)
        {
            _root = ReviewedFilePlan.NormalizeRoot(root);
            if (!Directory.Exists(_root)) throw new DirectoryNotFoundException("The project folder does not exist.");
            _debounce = debounce < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : debounce;
            _timer = new Timer(OnQuiet, null, Timeout.Infinite, Timeout.Infinite);
        }

        internal event Action<ProjectWatchSignal> Settled;
        internal string Root => _root;

        internal void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PbiBenchProjectWatcher));
                if (_watcher != null) return;
                _watcher = new FileSystemWatcher(_root) {
                    IncludeSubdirectories = true, InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Changed += OnEvent; _watcher.Created += OnEvent; _watcher.Deleted += OnEvent;
                _watcher.Renamed += (s, e) => { Record(e.OldFullPath); OnEvent(s, e); };
                _watcher.Error += (s, e) => InjectOverflow();
                _watcher.EnableRaisingEvents = true;
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
                signal = new ProjectWatchSignal(new List<string>(_paths), _events, _overflow, _truncated);
                _paths.Clear(); _events = 0; _overflow = false; _truncated = false;
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
                if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); _watcher = null; }
                _timer.Dispose();
                Settled = null;
            }
        }
    }
}
