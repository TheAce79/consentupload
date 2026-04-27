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
    public class LoggerService
    {
        private static ILoggerFactory? _loggerFactory;
        private static FileLoggingConfig? _fileConfig;
        private static ConsoleLoggingConfig? _consoleConfig;
        private static StreamWriter? _fileWriter;
        private static readonly object _fileLock = new object();
        private static bool _isInitialized = false;

        public static event EventHandler<LogEventArgs>? LogMessage;

        public static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                var loggingConfig = ConfigurationService.GetLoggingConfig();
                _fileConfig = loggingConfig.File;
                _consoleConfig = loggingConfig.Console;

                if (_fileConfig.Enabled)
                    SetupFileLogging();

                _loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(ParseLogLevel(loggingConfig.LogLevel.Default));

                    if (_consoleConfig.Enabled)
                        builder.AddConsole();

                    builder.AddFilter("ConsentSync", ParseLogLevel(loggingConfig.LogLevel.ConsentSync));
                    builder.AddFilter("Microsoft", ParseLogLevel(loggingConfig.LogLevel.Microsoft));
                    builder.AddFilter("System", ParseLogLevel(loggingConfig.LogLevel.System));
                });

                _isInitialized = true;
                LogInformation("✅ Logging service initialized");
            }
            catch (Exception ex)
            {
                // ✅ Use Console directly — LogInformation would recurse here
                Console.WriteLine($"❌ Failed to initialize logging: {ex.Message}");
                throw;
            }
        }

        public static ILogger<T> GetLogger<T>()
        {
            if (!_isInitialized) Initialize();
            return _loggerFactory!.CreateLogger<T>();
        }

        public static void Log(LogLevel level, string message, string? category = null)
        {
            if (!_isInitialized) Initialize();

            var timestamp = DateTime.Now.ToString(_consoleConfig?.TimestampFormat ?? "yyyy-MM-dd HH:mm:ss");
            var levelStr = level.ToString().ToUpper();

            var formattedMessage = _consoleConfig?.ShowTimestamps == true
                ? $"[{timestamp}] [{levelStr,-11}] {message}"
                : $"[{levelStr,-11}] {message}";

            // ✅ FIX Bug 1: write directly to Console — never call LogInformation here
            if (_consoleConfig?.Enabled == true)
            {
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

                Console.WriteLine(formattedMessage); // ✅ was: LoggerService.LogInformation(...)

                Console.ForegroundColor = originalColor;
            }

            if (_fileConfig?.Enabled == true)
                WriteToFile(formattedMessage);

            LogMessage?.Invoke(null, new LogEventArgs
            {
                Level = level,
                Message = message,
                Timestamp = DateTime.Now,
                FormattedMessage = formattedMessage
            });
        }

        public static void LogInformation(string message) => Log(LogLevel.Information, message);
        public static void LogDebug(string message) => Log(LogLevel.Debug, message);
        public static void LogWarning(string message) => Log(LogLevel.Warning, message);
        public static void LogError(string message) => Log(LogLevel.Error, message);
        public static void LogError(string message, Exception ex) =>
            Log(LogLevel.Error, $"{message}\n   Exception: {ex.GetType().Name}\n   Message: {ex.Message}\n   StackTrace:\n{ex.StackTrace}");
        public static void LogCritical(string message) => Log(LogLevel.Critical, message);
        public static void LogCritical(string message, Exception ex) =>
            Log(LogLevel.Critical, $"{message}\n   Exception: {ex.GetType().Name}\n   Message: {ex.Message}\n   StackTrace:\n{ex.StackTrace}");

        private static void SetupFileLogging()
        {
            try
            {
                if (_fileConfig == null) return;

                Directory.CreateDirectory(_fileConfig.LogPath);

                // ✅ Resolve the same Priority 1 encoding used by your CSVs
                var targetEncoding = EncodingConfigurationService.GetPriorityEncoding();

                var fileName = _fileConfig.LogFileName.Replace("{Date}", DateTime.Now.ToString("yyyyMMdd"));
                var fullPath = Path.Combine(_fileConfig.LogPath, fileName);

                bool fileExists = File.Exists(fullPath);
                long fileSize = fileExists ? new FileInfo(fullPath).Length : 0;

                if (fileExists && fileSize > _fileConfig.MaxFileSizeMB * 1024 * 1024)
                {
                    var archiveName = Path.Combine(
                        _fileConfig.LogPath,
                        $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:HHmmss}.log");

                    File.Move(fullPath, archiveName);
                    // ✅ Safe: _fileWriter not yet open, console write is fine
                    Console.WriteLine($"   📦 Rotated log file: {Path.GetFileName(archiveName)}");
                }

                _fileWriter = new StreamWriter(fullPath, append: true, encoding: targetEncoding)
                {
                    AutoFlush = true
                };

                Console.WriteLine($"   📄 File logging enabled: {fullPath}"); // ✅ direct Console

                _fileWriter.WriteLine();
                _fileWriter.WriteLine($"{'═',60}");
                _fileWriter.WriteLine($"  New Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _fileWriter.WriteLine($"{'═',60}");

                CleanupOldLogs();
            }
            catch (Exception ex)
            {
                // ✅ FIX Bug 2: use Console directly — LogInformation recurses into Initialize here
                Console.WriteLine($"   ⚠️  Failed to setup file logging: {ex.Message}");
            }
        }

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
                    Console.WriteLine($"   ⚠️  Failed to write to log file: {ex.Message}"); // ✅ direct Console
                }
            }
        }

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
                    Console.WriteLine($"   🗑️  Deleted {deletedCount} old log file(s)"); // ✅ direct Console
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Failed to cleanup old logs: {ex.Message}"); // ✅ direct Console
            }
        }

        private static LogLevel ParseLogLevel(string level) => level.ToLower() switch
        {
            "debug" => LogLevel.Debug,
            "information" => LogLevel.Information,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            "trace" => LogLevel.Trace,
            _ => LogLevel.Information
        };

        public static void Dispose()
        {
            lock (_fileLock)
            {
                if (_fileWriter != null)
                {
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

    public class LogEventArgs : EventArgs
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string FormattedMessage { get; set; } = string.Empty;
    }
}