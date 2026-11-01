using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Glitzk;

class LogWriter : IDisposable
{
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "Logs");
    private readonly BlockingCollection<(DateTimeOffset Time, string Text)> queue = [];
    private readonly Action<string> onError;
    private readonly Thread writer;

    public LogWriter(Action<string> onError)
    {
        this.onError = onError;

        writer = new Thread(WriteQueued) { Name = nameof(LogWriter), IsBackground = true };
        writer.Start();
    }

    private void WriteQueued()
    {
        bool isFailing = false;

        foreach (var (time, text) in queue.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"{time:yyyy-MM-dd}.log"), $"[{time:HH:mm:ss}] {text}\n");
                isFailing = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!isFailing)
                    onError($"로그를 저장할 수 없습니다: {ex.Message}");

                isFailing = true;
            }
        }
    }

    public void Write(string message)
    {
        try
        {
            queue.Add((DateTimeOffset.Now, message));
        }
        catch (InvalidOperationException) {}
    }

    public void Write(Exception ex)
    {
        StringBuilder logMessage = new();
        AppendException(logMessage, ex, 0);
        Write(logMessage.ToString());
    }

    private static void AppendException(StringBuilder logMessage, Exception ex, int depth)
    {
        string indent = new(' ', depth * 3);

        logMessage.Append($"{ex.GetType().FullName} - {ex.Message}\n");
        logMessage.Append($"{indent}Source : {ex.Source}\n");
        logMessage.Append($"{indent}Stack : {ex.StackTrace}\n");

        if (ex.Data.Count > 0)
        {
            logMessage.Append($"{indent}Data :\n");
            foreach (DictionaryEntry item in ex.Data)
            {
                logMessage.Append($"{indent}   {item.Key} : {item.Value}\n");
            }
        }

        IEnumerable<Exception> innerExceptions = ex is AggregateException aggregate
            ? aggregate.InnerExceptions
            : ex.InnerException is null ? [] : [ex.InnerException];

        foreach (Exception inner in innerExceptions)
        {
            logMessage.Append($"{indent}Inner : ");
            AppendException(logMessage, inner, depth + 1);
        }
    }

    public void Dispose()
    {
        queue.CompleteAdding();
        writer.Join();
    }
}
