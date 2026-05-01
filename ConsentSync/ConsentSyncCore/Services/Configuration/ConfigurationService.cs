using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace ConsentSyncCore.Services.Configuration
{
    public partial class ConfigurationService
    {
        private static IConfiguration? _config;
        private static readonly object _lock = new object();
        private static string? _baseDirectory;
        private static string? _resolvedConfigDir;
        private static IDisposable? _changeToken;

        public static bool gDevMode { get; private set; }

        public static IConfiguration GetConfiguration()
        {
            if (_config != null) return _config;
            lock (_lock)
            {
                if (_config != null) return _config;
                LoadConfiguration();
            }
            return _config!;
        }

        public static void ReloadConfiguration()
        {
            lock (_lock)
            {
                _changeToken?.Dispose();
                _changeToken = null;
                _config = null;
                _baseDirectory = null;
                LoadConfiguration();
            }

            LoggerService.LogInformation("🔄 Configuration reloaded from disk.");
            LoggerService.LogInformation($"   📁 Base Directory : {_baseDirectory}");
            LoggerService.LogInformation($"   📄 Config file    : {Path.Combine(_resolvedConfigDir!, "appsettings.json")}");
        }

        public static string GetBaseDirectory()
        {
            if (_config == null) GetConfiguration();
            return _baseDirectory ?? "C:\\PHIS";
        }

        // ── Internal loading ──────────────────────────────────────────

        private static void LoadConfiguration()
        {
            var environment = Environment.GetEnvironmentVariable("CONSENTSYNC_ENVIRONMENT") ?? "Production";

            _resolvedConfigDir ??= FindAppSettingsDirectory();

            var configPath = Path.Combine(_resolvedConfigDir, "appsettings.json");

            Console.WriteLine($"📄 Loading appsettings.json from: {configPath}");

            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    $"appsettings.json not found.\nSearched in: {_resolvedConfigDir}\n" +
                    "Ensure the file is set to 'Copy if newer' in its project properties.",
                    configPath);

            _config = new ConfigurationBuilder()
                .SetBasePath(_resolvedConfigDir)
                // ✅ reloadOnChange: FALSE — eliminates System.IO.IOException flood
                //    caused by MSBuild touching the file during Debug builds.
                //    Use ReloadConfiguration() explicitly after saving from the UI.
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "CONSENTSYNC_")
                .Build();

            _baseDirectory = _config["BaseDirectory"] ?? "C:\\PHIS";

            gDevMode = _config.GetValue<bool>("DevMode");

            // ✅ No file watcher needed — dispose any lingering token from previous loads
            _changeToken?.Dispose();
            _changeToken = null;

            Console.WriteLine($"✅ Configuration loaded  (Environment: {environment})");
            Console.WriteLine($"📁 Base Directory        : {_baseDirectory}");
        }

        // ── Directory discovery ───────────────────────────────────────

        private static string FindAppSettingsDirectory()
        {
            var candidates = new List<string>
            {
                AppContext.BaseDirectory,
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory()
            };

            var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            for (int i = 0; i < 8; i++)
            {
                candidates.Add(dir);
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent == null || parent == dir) break;
                dir = parent;
            }

            foreach (var candidate in candidates.Distinct())
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    File.Exists(Path.Combine(candidate, "appsettings.json")))
                {
                    Console.WriteLine($"   ✅ Found appsettings.json at: {candidate}");
                    return candidate;
                }
            }

            Console.WriteLine($"   ⚠️  appsettings.json not found — defaulting to: {AppContext.BaseDirectory}");
            return AppContext.BaseDirectory;
        }

        // ── Path resolver ─────────────────────────────────────────────

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (_config == null) GetConfiguration();

            var sc = GetSchoolContextConfig();
            return path
                .Replace("{BaseDirectory}", GetBaseDirectory())
                .Replace("{SchoolName}", sc.SchoolName)
                .Replace("{Grade}", sc.Grade)
                .Replace("{SchoolYear}", sc.SchoolYear);
        }

        private static string[] ResolvePaths(string[] paths) =>
            paths?.Select(ResolvePath).ToArray() ?? Array.Empty<string>();
    }
}