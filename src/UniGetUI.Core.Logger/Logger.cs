using System;
using System.Collections.Generic;
using System.IO;
using Diagnostics = System.Diagnostics;

namespace UniGetUI.Core.Logging
{
    public static class Logger
    {
        private static readonly List<LogEntry> LogContents = [];
        private static readonly object FileLock = new();
        private static readonly string LogFilePath;

        static Logger()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var logDir = Path.Combine(baseDir, "Logs");
                Directory.CreateDirectory(logDir);
                LogFilePath = Path.Combine(logDir, "aiDAPTIVAppStore.log");
            }
            catch (Exception e)
            {
                Diagnostics.Debug.WriteLine($"[Logger] Failed to initialize log file path: {e}");
                LogFilePath = string.Empty;
            }
        }

        private static void WriteToFile(string message, LogEntry.SeverityLevel severity, string caller)
        {
            if (string.IsNullOrEmpty(LogFilePath))
            {
                return;
            }

            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{severity}] [{caller}] {message}{Environment.NewLine}";
                lock (FileLock)
                {
                    File.AppendAllText(LogFilePath, line);
                }
            }
            catch (Exception e)
            {
                Diagnostics.Debug.WriteLine($"[Logger] Failed to write to log file: {e}");
            }
        }

        // String parameter log functions
        public static void ImportantInfo(string s, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + s);
            LogContents.Add(new LogEntry(s, LogEntry.SeverityLevel.Success));
            WriteToFile(s, LogEntry.SeverityLevel.Success, caller);
        }

        public static void Debug(string s, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + s);
            LogContents.Add(new LogEntry(s, LogEntry.SeverityLevel.Debug));
            WriteToFile(s, LogEntry.SeverityLevel.Debug, caller);
        }

        public static void Info(string s, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + s);
            LogContents.Add(new LogEntry(s, LogEntry.SeverityLevel.Info));
            WriteToFile(s, LogEntry.SeverityLevel.Info, caller);
        }

        public static void Warn(string s, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + s);
            LogContents.Add(new LogEntry(s, LogEntry.SeverityLevel.Warning));
            WriteToFile(s, LogEntry.SeverityLevel.Warning, caller);
        }

        public static void Error(string s, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + s);
            LogContents.Add(new LogEntry(s, LogEntry.SeverityLevel.Error));
            WriteToFile(s, LogEntry.SeverityLevel.Error, caller);
        }

        // Exception parameter log functions
        public static void ImportantInfo(Exception e, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + e.ToString());
            LogContents.Add(new LogEntry(e.ToString(), LogEntry.SeverityLevel.Success));
            WriteToFile(e.ToString(), LogEntry.SeverityLevel.Success, caller);
        }

        public static void Debug(Exception e, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + e.ToString());
            LogContents.Add(new LogEntry(e.ToString(), LogEntry.SeverityLevel.Debug));
            WriteToFile(e.ToString(), LogEntry.SeverityLevel.Debug, caller);
        }

        public static void Info(Exception e, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + e.ToString());
            LogContents.Add(new LogEntry(e.ToString(), LogEntry.SeverityLevel.Info));
            WriteToFile(e.ToString(), LogEntry.SeverityLevel.Info, caller);
        }

        public static void Warn(Exception e, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + e.ToString());
            LogContents.Add(new LogEntry(e.ToString(), LogEntry.SeverityLevel.Warning));
            WriteToFile(e.ToString(), LogEntry.SeverityLevel.Warning, caller);
        }

        public static void Error(Exception e, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            Diagnostics.Debug.WriteLine($"[{caller}] " + e.ToString());
            LogContents.Add(new LogEntry(e.ToString(), LogEntry.SeverityLevel.Error));
            WriteToFile(e.ToString(), LogEntry.SeverityLevel.Error, caller);
        }

        public static LogEntry[] GetLogs()
        {
            return LogContents.ToArray();
        }
    }
}
