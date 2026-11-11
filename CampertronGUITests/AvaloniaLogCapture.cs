using Avalonia.Logging;

namespace CampertronGUITests
{
    // Records every Avalonia log event at Warning level or above (binding errors are logged in LogArea.Binding).
    // Install it at the start of a test and dispose it at the end; it restores the previous sink.
    public sealed class AvaloniaLogCapture : ILogSink, IDisposable
    {
        private readonly ILogSink? _Previous;
        private readonly List<string> _Entries = new List<string>();

        public AvaloniaLogCapture()
        {
            _Previous = Logger.Sink;
            Logger.Sink = this;
        }

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_Entries)
                {
                    return _Entries.ToList();
                }
            }
        }

        public IReadOnlyList<string> BindingEntries => Entries.Where(e => e.Contains("[" + LogArea.Binding + "]")).ToList();

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            Add(level, area, source, messageTemplate, Array.Empty<object?>());
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            Add(level, area, source, messageTemplate, propertyValues);
        }

        private void Add(LogEventLevel level, string area, object? source, string messageTemplate, object?[] propertyValues)
        {
            if (!IsEnabled(level, area))
            {
                return;
            }
            string Entry = $"{level} [{area}] {source?.GetType().Name}: {messageTemplate} | {string.Join(", ", propertyValues)}";
            lock (_Entries)
            {
                _Entries.Add(Entry);
            }
        }

        public void Dispose()
        {
            Logger.Sink = _Previous;
        }
    }
}
