using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LiveSplit.SNAPTICDemo
{
    internal static class SNAPTICDemoLog
    {
        private static readonly object Sync = new object();
        private static readonly string LogPath = ResolveLogPath();
        private static bool initialized;

        public static string FilePath => LogPath;

        public static void Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return;

                initialized = true;
                try
                {
                    File.WriteAllText(
                        LogPath,
                        "=== SNAPTIC Demo Autosplitter Log ===" + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch
                {
                    // Logging must never be allowed to break the autosplitter.
                }
            }

            Write("Logger initialized. Component version: " +
                Assembly.GetExecutingAssembly().GetName().Version + ".");
            Write("Log path: " + LogPath);
        }

        public static void Write(string message)
        {
            if (!initialized)
                Initialize();

            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " +
                message + Environment.NewLine;

            lock (Sync)
            {
                try
                {
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
                catch
                {
                    // Ignore log I/O errors so timing logic keeps running.
                }
            }
        }

        private static string ResolveLogPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDirectory))
                return Path.Combine(baseDirectory, "_SNAPTICDemo.log");

            string assemblyDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(assemblyDirectory))
                return Path.Combine(assemblyDirectory, "_SNAPTICDemo.log");

            return Path.Combine(Path.GetTempPath(), "_SNAPTICDemo.log");
        }
    }
}
