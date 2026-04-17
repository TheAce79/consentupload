using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ConsentSyncCore.Services.ConfigurationPoco;

namespace ConsentSyncCore.Services.Configuration
{
    /// <summary>
    /// Centralized logging service that supports both console and file logging
    /// Designed to be easily adaptable to UI TextBox output in the future
    /// </summary>
    public class LoggerService
    {
        private static ILoggerFactory? _loggerFactory;
        private static FileLoggingConfig? _fileConfig;
        private static ConsoleLoggingConfig? _consoleConfig;
        private static StreamWriter? _fileWriter;
        private static readonly object _fileLock = new object();
        private static bool _isInitialized = false; // ✅ Add initialization flag

        /// <summary>
        /// Event that can be subscribed to for UI updates (e.g., TextBox)
        /// </summary>
        public static event EventHandler<LogEventArgs>? LogMessage;

        /// <summary>
        /// Initialize the logging service
        /// </summary>
        public static void Initialize()
        {
            // ✅ Prevent multiple initializations
            if (_isInitialized)
            {
                return;
            }

            try
            {
                var loggingConfig = ConfigurationService.GetLoggingConfig();
                _fileConfig = loggingConfig.File;
                _consoleConfig = loggingConfig.Console;

                // Setup file logging if enabled
                if (_fileConfig.Enabled)
                {
                    SetupFileLogging();
                }

                // Create logger factory
                _loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(ParseLogLevel(loggingConfig.LogLevel.Default));

                    // Add console logging
                    if (_consoleConfig.Enabled)
                    {
                        builder.AddConsole();
                    }

                    // Configure category-specific log levels
                    builder.AddFilter("ConsentSync", ParseLogLevel(loggingConfig.LogLevel.ConsentSync));
                    builder.AddFilter("Microsoft", ParseLogLevel(loggingConfig.LogLevel.Microsoft));
                    builder.AddFilter("System", ParseLogLevel(loggingConfig.LogLevel.System));
                });

                _isInitialized = true;
                Console.WriteLine("✅ Logging service initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to initialize logging: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get a logger for a specific category
        /// </summary>
        public static ILogger<T> GetLogger<T>()
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            return _loggerFactory!.CreateLogger<T>();
        }

        /// <summary>
        /// Log a message with automatic console, file, and event notification
        /// </summary>
        public static void Log(LogLevel level, string message, string? category = null)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            var timestamp = DateTime.Now.ToString(_consoleConfig?.TimestampFormat ?? "yyyy-MM-dd HH:mm:ss");
            var levelStr = level.ToString().ToUpper();

            // ✅ Better formatting with consistent padding
            var formattedMessage = _consoleConfig?.ShowTimestamps == true
                ? $"[{timestamp}] [{levelStr,-11}] {message}"
                : $"[{levelStr,-11}] {message}";

            // Console output
            if (_consoleConfig?.Enabled == true)
            {
                // ✅ Add color coding for different log levels (optional)
                var originalColor = Console.ForegroundColor;

                if (_consoleConfig.UseColoredOutput)
                {
                    Console.ForegroundColor = level switch
                    {
                        LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                        LogLevel.Warning => ConsoleColor.Yellow,
                        LogLevel.Information => ConsoleColor.White,
                        LogLevel.Debug => ConsoleColor.Gray,
                        _ => ConsoleColor.White
                    };
                }

                Console.WriteLine(formattedMessage);
                Console.ForegroundColor = originalColor;
            }

            // File output
            if (_fileConfig?.Enabled == true)
            {
                WriteToFile(formattedMessage);
            }

            // Raise event for UI subscribers
            LogMessage?.Invoke(null, new LogEventArgs
            {
                Level = level,
                Message = message,
                Timestamp = DateTime.Now,
                FormattedMessage = formattedMessage
            });
        }

        /// <summary>
        /// Helper methods for different log levels
        /// </summary>
        public static void LogInformation(string message) => Log(LogLevel.Information, message);
        public static void LogDebug(string message) => Log(LogLevel.Debug, message);
        public static void LogWarning(string message) => Log(LogLevel.Warning, message);
        public static void LogError(string message) => Log(LogLevel.Error, message);
        public static void LogError(string message, Exception ex) =>
            Log(LogLevel.Error, $"{message}\n   Exception: {ex.GetType().Name}\n   Message: {ex.Message}\n   StackTrace:\n{ex.StackTrace}");

        // ✅ Add Critical level
        public static void LogCritical(string message) => Log(LogLevel.Critical, message);
        public static void LogCritical(string message, Exception ex) =>
            Log(LogLevel.Critical, $"{message}\n   Exception: {ex.GetType().Name}\n   Message: {ex.Message}\n   StackTrace:\n{ex.StackTrace}");

        /// <summary>
        /// Setup file logging
        /// </summary>
        private static void SetupFileLogging()
        {
            try
            {
                if (_fileConfig == null) return;

                // Create log directory
                Directory.CreateDirectory(_fileConfig.LogPath);

                // Generate log filename with date
                var fileName = _fileConfig.LogFileName.Replace("{Date}", DateTime.Now.ToString("yyyyMMdd"));
                var fullPath = Path.Combine(_fileConfig.LogPath, fileName);

                // ✅ Check if file exists and get size
                bool fileExists = File.Exists(fullPath);
                long fileSize = fileExists ? new FileInfo(fullPath).Length : 0;

                // ✅ Rotate if file exceeds max size
                if (fileExists && fileSize > _fileConfig.MaxFileSizeMB * 1024 * 1024)
                {
                    var archiveName = Path.Combine(
                        _fileConfig.LogPath,
                        $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:HHmmss}.log");

                    File.Move(fullPath, archiveName);
                    Console.WriteLine($"   📦 Rotated log file: {Path.GetFileName(archiveName)}");
                }

                // Open file writer (append mode)
                _fileWriter = new StreamWriter(fullPath, append: true, encoding: Encoding.UTF8)
                {
                    AutoFlush = true
                };

                Console.WriteLine($"   📄 File logging enabled: {fullPath}");

                // ✅ Write session separator
                _fileWriter.WriteLine();
                _fileWriter.WriteLine($"{'═',60}");
                _fileWriter.WriteLine($"  New Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _fileWriter.WriteLine($"{'═',60}");

                // Cleanup old logs
                CleanupOldLogs();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Failed to setup file logging: {ex.Message}");
            }
        }

        /// <summary>
        /// Write message to log file
        /// </summary>
        private static void WriteToFile(string message)
        {
            if (_fileWriter == null) return;

            lock (_fileLock)
            {
                try
                {
                    _fileWriter.WriteLine(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️  Failed to write to log file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Cleanup old log files based on RetainDays setting
        /// </summary>
        private static void CleanupOldLogs()
        {
            if (_fileConfig == null) return;

            try
            {
                var logFiles = Directory.GetFiles(_fileConfig.LogPath, "ConsentSync_*.log");
                var cutoffDate = DateTime.Now.AddDays(-_fileConfig.RetainDays);

                int deletedCount = 0;
                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        File.Delete(logFile);
                        deletedCount++;
                    }
                }

                if (deletedCount > 0)
                {
                    Console.WriteLine($"   🗑️  Deleted {deletedCount} old log file(s)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Failed to cleanup old logs: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse log level from string
        /// </summary>
        private static LogLevel ParseLogLevel(string level)
        {
            return level.ToLower() switch
            {
                "debug" => LogLevel.Debug,
                "information" => LogLevel.Information,
                "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                "critical" => LogLevel.Critical,
                "trace" => LogLevel.Trace, // ✅ Add trace level
                _ => LogLevel.Information
            };
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public static void Dispose()
        {
            lock (_fileLock)
            {
                if (_fileWriter != null)
                {
                    // ✅ Write session end marker
                    _fileWriter.WriteLine($"{'═',60}");
                    _fileWriter.WriteLine($"  Session Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    _fileWriter.WriteLine($"{'═',60}");
                    _fileWriter.WriteLine();

                    _fileWriter.Flush();
                    _fileWriter.Dispose();
                    _fileWriter = null;
                }
            }

            _loggerFactory?.Dispose();
            _loggerFactory = null;
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Event args for log messages (useful for UI binding)
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string FormattedMessage { get; set; } = string.Empty;
    }
}