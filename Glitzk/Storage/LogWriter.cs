using System.Collections;
using System.IO;
using System.Text;

namespace Glitzk.Storage
{
    public class LogWriter
    {
        private readonly string filePath;
        public volatile bool _isEnabled = true;

        public LogWriter()
        {
            filePath = Path.Combine(AppContext.BaseDirectory, "Logs");
        }

        public void AppendLog(Exception ex)
        {
            AppendLog(ex, DateTimeOffset.UtcNow);
        }

        public void AppendLog(Exception ex, DateTimeOffset dateTime)
        {
            if (!_isEnabled)
                return;

            if (!Directory.Exists(filePath))
                Directory.CreateDirectory(filePath);

            StringBuilder LogMessage = new();

            LogMessage.Append($"[{dateTime:HH:mm:ss}] ");
            AppendException(LogMessage, ex, 0);
            LogMessage.AppendLine();

            File.AppendAllText(Path.Combine(filePath, $"{dateTime:yyyy-MM-dd}.log"), LogMessage.ToString());
        }

        private static void AppendException(StringBuilder logMessage, Exception ex, int depth)
        {
            string indent = new(' ', depth * 3);

            logMessage.Append($"{ex.GetType().FullName} - {ex.Message}\n");
            logMessage.Append($"{indent}Source : {ex.Source}\n");

            if (ex.Data.Count > 0)
            {
                logMessage.Append($"{indent}Data :\n");
                foreach (DictionaryEntry item in ex.Data)
                {
                    logMessage.Append($"{indent}   {item.Key} : {item.Value}\n");
                }
            }

            // AggregateException.InnerException exposes only the first of several inner exceptions.
            IEnumerable<Exception> innerExceptions = ex is AggregateException aggregate
                ? aggregate.InnerExceptions
                : ex.InnerException is null ? [] : [ex.InnerException];

            foreach (Exception inner in innerExceptions)
            {
                logMessage.Append($"{indent}Inner : ");
                AppendException(logMessage, inner, depth + 1);
            }
        }
    }
}
