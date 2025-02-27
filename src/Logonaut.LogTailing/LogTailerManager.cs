using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Logonaut.LogTailing
{
    public class LogTailerManager : IDisposable
    {
        private static LogTailerManager _instance;
        public static LogTailerManager Instance => _instance ??= new LogTailerManager();

        private LogTailer _currentTailer;
        private readonly Subject<string> _logLinesSubject = new Subject<string>();

        public IObservable<string> LogLines => _logLinesSubject.AsObservable();

        private LogTailerManager() { }

        public void ChangeFile(string newFilePath)
        {
            // Dispose the current tailer, if any.
            _currentTailer?.Dispose();

            // Create and start a new tailer for the selected file.
            _currentTailer = new LogTailer(newFilePath);
            _currentTailer.LogLines.Subscribe(
                line => _logLinesSubject.OnNext(line),
                ex => _logLinesSubject.OnError(ex),
                () => _logLinesSubject.OnCompleted());
            _currentTailer.Start();
        }

        public void Dispose()
        {
            _currentTailer?.Dispose();
            _logLinesSubject?.OnCompleted();
            _logLinesSubject?.Dispose();
        }
    }
}
