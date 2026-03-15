using System;
using System.IO;
using System.Text;

namespace ContextMenuProfiler.UI.Core.Services
{
    public class LogService
    {
        private static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ContextMenuProfiler", "app.log");
        private static readonly object LockObj = new object();

        public static LogService Instance { get; } = new LogService();

        private LogService() 
        {
            try 
            {
                var dir = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { /* Best effort */ }
        }

        public void Info(string message) => Log("INFO", message);
        public void Warning(string message, Exception? ex = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine(message);
            if (ex != null)
            {
                sb.AppendLine($"Exception: {ex.Message}");
                sb.AppendLine($"Stack Trace: {ex.StackTrace}");
            }
            Log("WARN", sb.ToString());
        }

        public void Error(string message, Exception? ex = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine(message);
            
            Exception? currentEx = ex;
            int level = 0;
            while (currentEx != null)
            {
                if (level > 0) sb.AppendLine($"Inner Exception [{level}]: {currentEx.Message}");
                else sb.AppendLine($"Exception: {currentEx.Message}");
                
                sb.AppendLine($"Stack Trace: {currentEx.StackTrace}");
                currentEx = currentEx.InnerException;
                level++;
            }
            
            Log("ERROR", sb.ToString());
        }

        private void Log(string level, string message)
        {
            try
            {
                lock (LockObj)
                {
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                    File.AppendAllText(LogFile, logEntry + Environment.NewLine);
                    
                    // Also write to Debug output
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
            }
            catch (Exception)
            {
                // Last resort: fail silently if logging fails
            }
        }
    }
}
