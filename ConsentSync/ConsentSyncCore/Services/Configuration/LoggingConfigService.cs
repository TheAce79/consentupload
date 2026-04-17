using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Configuration
{
    public partial class ConfigurationService
    {





        /// <summary>
        /// Get Logging configuration
        /// </summary>
        public static LoggingConfig GetLoggingConfig()
        {
            var config = GetConfiguration();
            return new LoggingConfig
            {
                LogLevel = new LogLevelConfig
                {
                    Default = config["Logging:LogLevel:Default"] ?? "Information",
                    ConsentSync = config["Logging:LogLevel:ConsentSync"] ?? "Debug",
                    Microsoft = config["Logging:LogLevel:Microsoft"] ?? "Warning",
                    System = config["Logging:LogLevel:System"] ?? "Warning"
                },
                Console = new ConsoleLoggingConfig
                {
                    Enabled = config.GetValue<bool>("Logging:Console:Enabled", true),
                    UseColoredOutput = config.GetValue<bool>("Logging:Console:UseColoredOutput", true),
                    ShowTimestamps = config.GetValue<bool>("Logging:Console:ShowTimestamps", true),
                    TimestampFormat = config["Logging:Console:TimestampFormat"] ?? "yyyy-MM-dd HH:mm:ss"
                },
                File = new FileLoggingConfig
                {
                    Enabled = config.GetValue<bool>("Logging:File:Enabled", false),
                    LogPath = ResolvePath(config["Logging:File:LogPath"] ?? "{BaseDirectory}\\Logs"),
                    LogFileName = config["Logging:File:LogFileName"] ?? "ConsentSync_{Date}.log",
                    MaxFileSizeMB = config.GetValue<int>("Logging:File:MaxFileSizeMB", 10),
                    RetainDays = config.GetValue<int>("Logging:File:RetainDays", 30),
                    MinimumLevel = config["Logging:File:MinimumLevel"] ?? "Information"
                }
            };
        }


    }
}
