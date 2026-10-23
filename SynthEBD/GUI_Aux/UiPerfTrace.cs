using System.Diagnostics;
using System.IO;

namespace SynthEBD;

/// <summary>
/// Lightweight timing trace for UI handlers that can go slow on large profiles (Body Type Profile editor:
/// live-value refresh, measurement highlight, rule filtering...). Wrap a block in
/// <c>using var _ = UiPerfTrace.Measure("Name");</c>; when the outermost measured block takes at least
/// <see cref="ThresholdMs"/> it appends one line to <see cref="LogFilePath"/> with its own time and the time
/// of every nested measured block, plus how often each ran, so a laggy click names the step that cost it.
/// Off until a log path is configured; UI thread only (nesting is tracked in a [ThreadStatic] stack).
/// </summary>
public static class UiPerfTrace
{
    /// <summary>File the slow-operation lines go to; null disables tracing.</summary>
    public static string? LogFilePath { get; set; }

    /// <summary>Outermost blocks faster than this are not logged.</summary>
    public static double ThresholdMs { get; set; } = 100;

    [ThreadStatic] private static Frame? _current;

    private sealed class Frame
    {
        public string Name = "";
        public Frame? Parent;
        public readonly Stopwatch Watch = Stopwatch.StartNew();
        public Dictionary<string, (int Count, double Ms)>? Children;
    }

    public static IDisposable Measure(string name)
    {
        if (LogFilePath == null) return NoOp.Instance;
        var frame = new Frame { Name = name, Parent = _current };
        _current = frame;
        return new Scope(frame);
    }

    /// <summary>Logs how long it takes from now until the UI thread next goes idle (ContextIdle priority runs
    /// after layout and render), i.e. the lag the user actually sees after an input event -- including the WPF
    /// layout / template work no <see cref="Measure"/> block can wrap. Call at the start of an input handler.</summary>
    public static void MeasureUntilIdle(string name)
    {
        if (LogFilePath == null) return;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var watch = Stopwatch.StartNew();
        dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            double ms = watch.Elapsed.TotalMilliseconds;
            if (ms < ThresholdMs || LogFilePath == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, $"{DateTime.Now:HH:mm:ss.fff} {name} -> idle {ms:F0} ms (incl. layout/render){Environment.NewLine}");
            }
            catch { }
        }));
    }

    private sealed class Scope : IDisposable
    {
        private Frame? _frame;
        public Scope(Frame frame) => _frame = frame;

        public void Dispose()
        {
            var f = _frame;
            if (f == null) return;
            _frame = null;
            f.Watch.Stop();
            double ms = f.Watch.Elapsed.TotalMilliseconds;
            _current = f.Parent;
            if (f.Parent != null)
            {
                // Fold into the parent: per-name totals, plus our own children (flattened, so the line names
                // every measured step under the outermost block).
                var map = f.Parent.Children ??= new();
                Add(map, f.Name, 1, ms);
                if (f.Children != null)
                    foreach (var kv in f.Children) Add(map, kv.Key, kv.Value.Count, kv.Value.Ms);
                return;
            }
            if (ms < ThresholdMs) return;
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} {f.Name} {ms:F0} ms";
                if (f.Children != null && f.Children.Count > 0)
                    line += " | " + string.Join(", ", f.Children
                        .OrderByDescending(kv => kv.Value.Ms)
                        .Select(kv => $"{kv.Key} x{kv.Value.Count} {kv.Value.Ms:F0} ms"));
                var path = LogFilePath;
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never break the UI.
            }
        }

        private static void Add(Dictionary<string, (int Count, double Ms)> map, string name, int count, double ms)
        {
            map.TryGetValue(name, out var cur);
            map[name] = (cur.Count + count, cur.Ms + ms);
        }
    }

    private sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();
        public void Dispose() { }
    }
}
