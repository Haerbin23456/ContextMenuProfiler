using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ContextMenuProfiler.UI.Core.Services
{
    public class LogService
    {
        private static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ContextMenuProfiler", "app.log");
        private static readonly object LockObj = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };
        private readonly AsyncLocal<IReadOnlyDictionary<string, object?>?> _scopeFields = new AsyncLocal<IReadOnlyDictionary<string, object?>?>();

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

        public void Info(string message) => WriteEvent("INFO", "app.info", message, null, null);

        public void Warning(string message, Exception? ex = null)
        {
            WriteEvent("WARN", "app.warning", message, ex, null);
        }

        public void Error(string message, Exception? ex = null)
        {
            WriteEvent("ERROR", "app.error", message, ex, null);
        }

        public void InfoEvent(string eventName, IReadOnlyDictionary<string, object?>? fields = null, string? message = null)
        {
            WriteEvent("INFO", eventName, message, null, fields);
        }

        public void WarningEvent(string eventName, string? message = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? fields = null)
        {
            WriteEvent("WARN", eventName, message, ex, fields);
        }

        public void ErrorEvent(string eventName, string? message = null, Exception? ex = null, IReadOnlyDictionary<string, object?>? fields = null)
        {
            WriteEvent("ERROR", eventName, message, ex, fields);
        }

        public IDisposable BeginScope(IReadOnlyDictionary<string, object?> fields)
        {
            var previous = _scopeFields.Value;
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (previous != null)
            {
                foreach (var kv in previous)
                {
                    merged[kv.Key] = kv.Value;
                }
            }

            foreach (var kv in fields)
            {
                merged[kv.Key] = kv.Value;
            }

            _scopeFields.Value = merged;
            return new ScopeToken(this, previous);
        }

        private void WriteEvent(
            string level,
            string eventName,
            string? message,
            Exception? ex,
            IReadOnlyDictionary<string, object?>? fields)
        {
            try
            {
                var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["level"] = level,
                    ["event"] = eventName
                };

                if (!string.IsNullOrWhiteSpace(message))
                {
                    payload["message"] = message;
                }

                if (fields != null)
                {
                    foreach (var kv in fields)
                    {
                        payload[kv.Key] = NormalizeValue(kv.Value);
                    }
                }

                if (_scopeFields.Value != null)
                {
                    foreach (var kv in _scopeFields.Value)
                    {
                        if (!payload.ContainsKey(kv.Key))
                        {
                            payload[kv.Key] = NormalizeValue(kv.Value);
                        }
                    }
                }

                if (ex != null)
                {
                    payload["exception"] = BuildExceptionObject(ex);
                }

                string logEntry = JsonSerializer.Serialize(payload, JsonOptions);

                lock (LockObj)
                {
                    File.AppendAllText(LogFile, logEntry + Environment.NewLine);
                }

                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch (Exception)
            {
            }
        }

        private static Dictionary<string, object?> BuildExceptionObject(Exception ex)
        {
            var chain = new List<Dictionary<string, object?>>();
            Exception? current = ex;

            while (current != null)
            {
                chain.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = current.GetType().FullName,
                    ["message"] = current.Message,
                    ["stack_trace"] = current.StackTrace
                });
                current = current.InnerException;
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["message"] = ex.Message,
                ["type"] = ex.GetType().FullName,
                ["chain"] = chain
            };
        }

        private static object? NormalizeValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            Type type = value.GetType();
            if (type.IsPrimitive || value is decimal || value is string || value is Guid || value is DateTime || value is DateTimeOffset || value is TimeSpan)
            {
                return value;
            }

            if (value is Enum)
            {
                return value.ToString();
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(NormalizeValue(item));
                }
                return list;
            }

            return value.ToString();
        }

        private sealed class ScopeToken : IDisposable
        {
            private readonly LogService _owner;
            private readonly IReadOnlyDictionary<string, object?>? _previous;
            private bool _disposed;

            public ScopeToken(LogService owner, IReadOnlyDictionary<string, object?>? previous)
            {
                _owner = owner;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _owner._scopeFields.Value = _previous;
                _disposed = true;
            }
        }
    }
}
