using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Kneeboard_Server.Logging
{
    /// <summary>
    /// Centralized Logging System for Kneeboard Server
    /// - Console output (as before)
    /// - File logging: logs/kneeboard_YYYY-MM-DD.log
    /// - Log levels: DEBUG, INFO, WARN, ERROR
    /// - Module-specific methods
    /// </summary>
    public static class KneeboardLogger
    {
        private static readonly string LOG_FOLDER = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "logs");

        private static readonly object _lockObject = new object();
        private static string _currentLogFile;
        private static DateTime _currentLogDate = DateTime.MinValue;
        private static bool _initialized = false;
        private static bool _consoleOutput = true;
        private static bool _fileOutput = true;
        private static Level _minLevel = Level.INFO;

        /// <summary>
        /// Log Levels
        /// </summary>
        public enum Level
        {
            DEBUG = 0,
            INFO = 1,
            WARN = 2,
            ERROR = 3
        }

        /// <summary>
        /// Initialize the logging system
        /// </summary>
        public static void Initialize(bool consoleOutput = true, bool fileOutput = true, Level minLevel = Level.INFO)
        {
            _consoleOutput = consoleOutput;
            _fileOutput = fileOutput;
            _minLevel = minLevel;

            if (_fileOutput)
            {
                try
                {
                    if (!Directory.Exists(LOG_FOLDER))
                    {
                        Directory.CreateDirectory(LOG_FOLDER);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KneeboardLogger] Failed to create log folder: {ex.Message}");
                    _fileOutput = false;
                }
            }

            _initialized = true;
            Info("Logger", "Kneeboard Logger initialized");
        }

        /// <summary>
        /// Core logging method
        /// </summary>
        public static void Log(Level level, string module, string message)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (level < _minLevel)
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var levelStr = level.ToString().PadRight(5);
            var logLine = $"[{timestamp}] [{levelStr}] [{module}] {message}";

            // Console output
            if (_consoleOutput)
            {
                try
                {
                    var originalColor = Console.ForegroundColor;
                    switch (level)
                    {
                        case Level.DEBUG:
                            Console.ForegroundColor = ConsoleColor.Gray;
                            break;
                        case Level.INFO:
                            Console.ForegroundColor = ConsoleColor.White;
                            break;
                        case Level.WARN:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                        case Level.ERROR:
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                    }
                    Console.WriteLine(logLine);
                    Console.ForegroundColor = originalColor;
                }
                catch
                {
                    // Silent fail if console not available
                }
            }

            // File output
            if (_fileOutput)
            {
                WriteToFile(logLine);
            }
        }

        /// <summary>
        /// Write log line to file (thread-safe)
        /// </summary>
        private static void WriteToFile(string logLine)
        {
            lock (_lockObject)
            {
                try
                {
                    var today = DateTime.Today;
                    if (_currentLogDate != today || string.IsNullOrEmpty(_currentLogFile))
                    {
                        _currentLogDate = today;
                        _currentLogFile = Path.Combine(LOG_FOLDER,
                            $"kneeboard_{today:yyyy-MM-dd}.log");
                    }

                    File.AppendAllText(_currentLogFile, logLine + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Silent fail
                }
            }
        }

        // ========================================================================
        // LEVEL-SPECIFIC METHODS
        // ========================================================================

        public static void Debug(string module, string message)
        {
            Log(Level.DEBUG, module, message);
        }

        public static void Info(string module, string message)
        {
            Log(Level.INFO, module, message);
        }

        public static void Warn(string module, string message)
        {
            Log(Level.WARN, module, message);
        }

        public static void Error(string module, string message)
        {
            Log(Level.ERROR, module, message);
        }

        public static void Error(string module, Exception ex)
        {
            Log(Level.ERROR, module, $"{ex.Message}\n{ex.StackTrace}");
        }

        public static void Error(string module, string message, Exception ex)
        {
            Log(Level.ERROR, module, $"{message}: {ex.Message}\n{ex.StackTrace}");
        }

        // ========================================================================
        // MODULE-SPECIFIC CONVENIENCE METHODS
        // ========================================================================

        public static void Navigraph(string message)
        {
            Log(Level.INFO, "Navigraph", message);
        }

        public static void NavigraphDebug(string message)
        {
            Log(Level.DEBUG, "Navigraph", message);
        }

        public static void NavigraphError(string message)
        {
            Log(Level.ERROR, "Navigraph", message);
        }

        public static void NavigraphError(Exception ex)
        {
            Error("Navigraph", ex);
        }

        public static void SimBrief(string message)
        {
            Log(Level.INFO, "SimBrief", message);
        }

        public static void SimBriefDebug(string message)
        {
            Log(Level.DEBUG, "SimBrief", message);
        }

        public static void SimBriefError(string message)
        {
            Log(Level.ERROR, "SimBrief", message);
        }

        public static void SimBriefError(Exception ex)
        {
            Error("SimBrief", ex);
        }

        public static void API(string message)
        {
            Log(Level.INFO, "API", message);
        }

        public static void APIDebug(string message)
        {
            Log(Level.DEBUG, "API", message);
        }

        public static void APIError(string message)
        {
            Log(Level.ERROR, "API", message);
        }

        public static void APIError(Exception ex)
        {
            Error("API", ex);
        }

        public static void Server(string message)
        {
            Log(Level.INFO, "Server", message);
        }

        public static void ServerDebug(string message)
        {
            Log(Level.DEBUG, "Server", message);
        }

        public static void ServerError(string message)
        {
            Log(Level.ERROR, "Server", message);
        }

        public static void ServerError(Exception ex)
        {
            Error("Server", ex);
        }

        public static void SimConnect(string message)
        {
            Log(Level.INFO, "SimConnect", message);
        }

        public static void SimConnectDebug(string message)
        {
            Log(Level.DEBUG, "SimConnect", message);
        }

        public static void SimConnectError(string message)
        {
            Log(Level.ERROR, "SimConnect", message);
        }

        public static void SimConnectError(Exception ex)
        {
            Error("SimConnect", ex);
        }

        // ========================================================================
        // CLIENT LOG FORWARDING
        // ========================================================================

        /// <summary>
        /// Log a message forwarded from the client (JavaScript)
        /// </summary>
        public static void ClientLog(string level, string module, string message)
        {
            Level logLevel;
            switch (level?.ToUpperInvariant())
            {
                case "DEBUG":
                    logLevel = Level.DEBUG;
                    break;
                case "WARN":
                case "WARNING":
                    logLevel = Level.WARN;
                    break;
                case "ERROR":
                    logLevel = Level.ERROR;
                    break;
                default:
                    logLevel = Level.INFO;
                    break;
            }

            Log(logLevel, $"Client:{module}", message);
        }

        // ========================================================================
        // LOG MANAGEMENT
        // ========================================================================

        /// <summary>
        /// Get the current log file path
        /// </summary>
        public static string GetCurrentLogPath()
        {
            return _currentLogFile;
        }

        /// <summary>
        /// Get all log files
        /// </summary>
        public static string[] GetLogFiles()
        {
            try
            {
                if (Directory.Exists(LOG_FOLDER))
                {
                    return Directory.GetFiles(LOG_FOLDER, "kneeboard_*.log");
                }
            }
            catch
            {
            }
            return new string[0];
        }

        /// <summary>
        /// Clean up old log files (keep last N days)
        /// </summary>
        public static void CleanupOldLogs(int keepDays = 7)
        {
            try
            {
                var cutoffDate = DateTime.Today.AddDays(-keepDays);
                var logFiles = GetLogFiles();

                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        try
                        {
                            File.Delete(file);
                            Info("Logger", $"Deleted old log file: {fileInfo.Name}");
                        }
                        catch (Exception ex)
                        {
                            Warn("Logger", $"Could not delete old log file {fileInfo.Name}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Error("Logger", "Error during log cleanup", ex);
            }
        }

        /// <summary>
        /// Set the minimum log level
        /// </summary>
        public static void SetMinLevel(Level level)
        {
            _minLevel = level;
            Info("Logger", $"Log level changed to {level}");
        }

        /// <summary>
        /// Enable or disable file output
        /// </summary>
        public static void SetFileOutput(bool enabled)
        {
            _fileOutput = enabled;
        }

        /// <summary>
        /// Enable or disable console output
        /// </summary>
        public static void SetConsoleOutput(bool enabled)
        {
            _consoleOutput = enabled;
        }
    }
}
