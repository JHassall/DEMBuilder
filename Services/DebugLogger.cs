using System;
using System.IO;
using System.Text;

namespace DEMBuilder.Services
{
    public static class DebugLogger
    {
        private static readonly string LogFilePath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            "DEMBuilder_Debug.log");

        public static void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}";
            
            // Write to console
            Console.WriteLine(logEntry);
            
            // Write to debug log file
            try
            {
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Ignore file write errors to prevent cascading issues
            }
        }

        public static void LogException(string context, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"EXCEPTION in {context}:");
            sb.AppendLine($"  Message: {ex.Message}");
            sb.AppendLine($"  Type: {ex.GetType().FullName}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"  Inner Exception: {ex.InnerException.Message}");
                sb.AppendLine($"  Inner Type: {ex.InnerException.GetType().FullName}");
            }
            sb.AppendLine($"  Stack Trace: {ex.StackTrace}");
            
            Log(sb.ToString());
        }

        public static void ClearLog()
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    File.Delete(LogFilePath);
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        public static string GetLogFilePath() => LogFilePath;
    }
}
