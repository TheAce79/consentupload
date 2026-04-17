using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.ConfigurationPoco
{

    /// <summary>
    /// Logging configuration
    /// </summary>
    public class LoggingConfig
    {
        public LogLevelConfig LogLevel { get; set; } = new();
        public ConsoleLoggingConfig Console { get; set; } = new();
        public FileLoggingConfig File { get; set; } = new();
    }

    /// <summary>
    /// Log level configuration
    /// </summary>
    public class LogLevelConfig
    {
        public string Default { get; set; } = "Information";
        public string ConsentSync { get; set; } = "Debug";
        public string Microsoft { get; set; } = "Warning";
        public string System { get; set; } = "Warning";
    }

    /// <summary>
    /// Console logging configuration
    /// </summary>
    public class ConsoleLoggingConfig
    {
        public bool Enabled { get; set; } = true;
        public bool UseColoredOutput { get; set; } = true;
        public bool ShowTimestamps { get; set; } = true;
        public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    }

    /// <summary>
    /// File logging configuration
    /// </summary>
    public class FileLoggingConfig
    {
        public bool Enabled { get; set; } = false;
        public string LogPath { get; set; } = string.Empty;
        public string LogFileName { get; set; } = "ConsentSync_{Date}.log";
        public int MaxFileSizeMB { get; set; } = 10;
        public int RetainDays { get; set; } = 30;
        public string MinimumLevel { get; set; } = "Information";
    }

}
