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
        private static IConfiguration? _config;
        private static readonly object _lock = new object();
        private static string? _baseDirectory;



        /// <summary>
        /// Get the configuration instance (singleton)
        /// </summary>
        public static IConfiguration GetConfiguration()
        {
            if (_config == null)
            {
                lock (_lock)
                {
                    if (_config == null)
                    {
                        var environment = Environment.GetEnvironmentVariable("CONSENTSYNC_ENVIRONMENT") ?? "Production";

                        _config = new ConfigurationBuilder()
                            .SetBasePath(AppContext.BaseDirectory)
                            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                            .AddEnvironmentVariables(prefix: "CONSENTSYNC_")
                            .Build();

                        // Load BaseDirectory
                        _baseDirectory = _config["BaseDirectory"] ?? "C:\\PHIS";

                        Console.WriteLine($"✅ Configuration loaded (Environment: {environment})");
                        Console.WriteLine($"📁 Base Directory: {_baseDirectory}");
                    }
                }
            }
            return _config;
        }


        /// <summary>
        /// Reload configuration from disk
        /// </summary>
        public static void ReloadConfiguration()
        {
            lock (_lock)
            {
                _config = null;
                _baseDirectory = null;
                GetConfiguration();
            }
        }



        /// <summary>
        /// Get the base directory
        /// </summary>
        public static string GetBaseDirectory()
        {
            if (_config == null)
            {
                GetConfiguration();
            }
            return _baseDirectory ?? "C:\\PHIS";
        }


        /// <summary>
        /// Resolve path with placeholders
        /// Supported placeholders:
        ///   {BaseDirectory} - Base directory for all operations
        ///   {SchoolName} - Current school name
        ///   {Grade} - Current grade
        ///   {SchoolYear} - Current school year
        /// </summary>
        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            // Ensure configuration is loaded
            if (_config == null)
            {
                GetConfiguration();
            }

            var schoolContext = GetSchoolContextConfig();
            var baseDir = GetBaseDirectory();

            // ✅ DEBUG: Show resolution
            Console.WriteLine($"      ResolvePath Input: '{path}'");
            Console.WriteLine($"      BaseDirectory: '{baseDir}'");
            Console.WriteLine($"      SchoolName: '{schoolContext.SchoolName}'");
            Console.WriteLine($"      Grade: '{schoolContext.Grade}'");
            Console.WriteLine($"      SchoolYear: '{schoolContext.SchoolYear}'");

            var resolved = path
                .Replace("{BaseDirectory}", baseDir)
                .Replace("{SchoolName}", schoolContext.SchoolName)
                .Replace("{Grade}", schoolContext.Grade)
                .Replace("{SchoolYear}", schoolContext.SchoolYear);

            Console.WriteLine($"      ResolvePath Output: '{resolved}'");

            return resolved;
        }



        /// <summary>
        /// Resolve multiple paths with placeholders
        /// </summary>
        private static string[] ResolvePaths(string[] paths)
        {
            return paths?.Select(ResolvePath).ToArray() ?? Array.Empty<string>();
        }




    }
}
