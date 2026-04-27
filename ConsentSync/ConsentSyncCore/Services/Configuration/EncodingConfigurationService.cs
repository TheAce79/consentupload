using ConsentSyncCore.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ConsentSyncCore.Services.Configuration
{
    /// <summary>
    /// Centralized service to handle character encoding based on appsettings.json priorities.
    /// Ensures consistency across CSV reading (SNB) and writing (Scanned/Validation).
    /// </summary>
    public static class EncodingConfigurationService
    {
        // Cache the encoding once per session if needed, or resolve dynamically
        private static Encoding? _cachedPriorityEncoding;

        /// <summary>
        /// Resolves the primary (Priority 1) encoding defined in CsvProcessing:EncodingsToTry.
        /// Defaults to Windows-1252 (ANSI) if no configuration is found.
        /// </summary>
        public static Encoding GetPriorityEncoding()
        {
            if (_cachedPriorityEncoding != null) return _cachedPriorityEncoding;

            var config = ConfigurationService.GetConfiguration();

            var encodingConfigs = config.GetSection("CsvProcessing:EncodingsToTry")
                                        .Get<List<EncodingConfiguration>>() ?? new List<EncodingConfiguration>();

            // Get the highest priority (lowest number) or default to 1252
            var priority1 = encodingConfigs.OrderBy(e => e.Priority).FirstOrDefault();

            _cachedPriorityEncoding = ResolveEncoding(priority1 ?? new EncodingConfiguration
            {
                Name = "Windows-1252 (ANSI)",
                CodePage = "1252"
            });

            return _cachedPriorityEncoding;
        }

        /// <summary>
        /// Helper to convert our POCO configuration into a real System.Text.Encoding object.
        /// Handles CodePages, UTF-8 with/without BOM, and System Default.
        /// </summary>
        public static Encoding ResolveEncoding(EncodingConfiguration config)
        {
            try
            {
                // Ensure legacy code pages are available (important for 1252)
                // Note: This is safe to call multiple times.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                if (config.CodePage.Equals("default", StringComparison.OrdinalIgnoreCase))
                    return Encoding.Default;

                if (config.CodePage.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                {
                    // true = Emit BOM (Byte Order Mark), crucial for Excel recognition
                    return config.UseBOM ? new UTF8Encoding(true) : Encoding.UTF8;
                }

                if (int.TryParse(config.CodePage, out int codePage))
                    return Encoding.GetEncoding(codePage);

                return Encoding.GetEncoding(config.CodePage);
            }
            catch (Exception)
            {
                // Safety fallback to UTF-8 with BOM to preserve accents even if config fails
                return new UTF8Encoding(true);
            }
        }

        /// <summary>
        /// Clears the cache. Useful if settings are reloaded.
        /// </summary>
        public static void ClearCache() => _cachedPriorityEncoding = null;
    }
}