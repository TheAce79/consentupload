using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Configuration;
using ConsentSyncCore.Services.ConfigurationPoco;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSyncCore.Services.Phis
{
    public class PhisSessionManager
    {

        private readonly IWebDriver _driver;
        private readonly IConfiguration _config;
        private readonly WebDriverWait _wait;
        private readonly PhisConfig _phisConfig;

        private DateTime _lastSessionActivity;
        private bool _isLoggedIn;

        public PhisSessionManager(IWebDriver driver, IConfiguration config)
        {
            _driver = driver;
            _config = config;
            _phisConfig = ConfigurationService.GetPhisConfig();
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_phisConfig.WebDriverWaitSeconds));
            _lastSessionActivity = DateTime.Now;
            _isLoggedIn = false;
        }




        #region Public API

        /// <summary>
        /// Initiate login - either automated or manual based on configuration
        /// </summary>
        public bool Login()
        {
            if (_phisConfig.ManualLoginMode)
            {
                return LoginManually();
            }
            else
            {
                return LoginAutomated();
            }
        }

        /// <summary>
        /// Check if session is valid and refresh if needed
        /// Returns true if session is valid/refreshed successfully
        /// </summary>
        public bool EnsureSessionValid()
        {
            if (!_phisConfig.SessionRefreshEnabled)
            {
                return _isLoggedIn;
            }

            var timeSinceLastActivity = DateTime.Now - _lastSessionActivity;
            var timeUntilTimeout = TimeSpan.FromMinutes(_phisConfig.SessionTimeoutMinutes) - timeSinceLastActivity;

            // Refresh if less than buffer time remaining
            if (timeUntilTimeout.TotalMinutes < _phisConfig.RefreshBufferMinutes)
            {
                 LoggerService.LogInformation($"\n⚠️  Session timeout approaching ({timeUntilTimeout.TotalMinutes:F1} min remaining)");
                return RefreshSession();
            }

            return _isLoggedIn;
        }

        /// <summary>
        /// Check if session has expired
        /// </summary>
        public bool IsSessionExpired()
        {
            try
            {
                var currentUrl = _driver.Url.ToLowerInvariant();

                // Check if redirected to login page
                if (currentUrl.Contains("login") || currentUrl.Contains("signin"))
                {
                    return true;
                }

                // Try to find a common element that exists when logged in
                try
                {
                    _driver.FindElement(By.Id("form:dataTable:clientSearchId:searchComponentId:clientSearchBasic_dobAgeCriteriaType:clientSearchBasic_dobAgeCriteriaTypeDob:dateInput_input"));
                    return false; // Element found, session is valid
                }
                catch
                {
                    return true; // Element not found
                }
            }
            catch
            {
                return true; // Any error = assume expired
            }
        }

        /// <summary>
        /// Update session activity timestamp
        /// Call this after every successful PHIS interaction
        /// </summary>
        public void UpdateActivity()
        {
            _lastSessionActivity = DateTime.Now;
        }

        /// <summary>
        /// Get time remaining until session timeout
        /// </summary>
        public TimeSpan GetTimeRemaining()
        {
            var timeSinceLastActivity = DateTime.Now - _lastSessionActivity;
            return TimeSpan.FromMinutes(_phisConfig.SessionTimeoutMinutes) - timeSinceLastActivity;
        }

        /// <summary>
        /// Check if currently logged in
        /// </summary>
        public bool IsLoggedIn => _isLoggedIn;


        #endregion  Public API




        #region Login Methods

        /// <summary>
        /// Manual login - wait for user to log in manually
        /// </summary>
        private bool LoginManually()
        {
            try
            {
                 LoggerService.LogInformation("\n👤 MANUAL LOGIN MODE");
                 LoggerService.LogInformation("══════════════════════════════════════════════════════");

                _driver.Navigate().GoToUrl(_phisConfig.LoginUrl);

                 LoggerService.LogInformation($"📌 Browser opened to: {_phisConfig.LoginUrl}");
                 LoggerService.LogInformation($"\n⏳ Please log in manually within {_phisConfig.ManualLoginWaitSeconds} seconds...");
                 LoggerService.LogInformation("   The automation will start once you're logged in.");
                 LoggerService.LogInformation("\n💡 TIP: Navigate to the PHIS dashboard after logging in.");
                 LoggerService.LogInformation("══════════════════════════════════════════════════════\n");

                var endTime = DateTime.Now.AddSeconds(_phisConfig.ManualLoginWaitSeconds);
                bool loggedIn = false;

                while (DateTime.Now < endTime && !loggedIn)
                {
                    Thread.Sleep(2000); // Check every 2 seconds

                    var currentUrl = _driver.Url;

                    // Check if we've moved away from login page
                    if (!currentUrl.Contains("login", StringComparison.OrdinalIgnoreCase) &&
                        !currentUrl.Contains("signin", StringComparison.OrdinalIgnoreCase))
                    {
                        loggedIn = true;
                         LoggerService.LogInformation($"✅ Login detected! Current URL: {currentUrl}");
                         LoggerService.LogInformation("🚀 Starting automation...\n");
                        Thread.Sleep(2000); // Give page time to fully load
                        break;
                    }

                    var remaining = (int)(endTime - DateTime.Now).TotalSeconds;
                    if (remaining % 10 == 0 && remaining > 0)
                    {
                         LoggerService.LogInformation($"   ⏰ {remaining} seconds remaining...");
                    }
                }

                if (!loggedIn)
                {
                     LoggerService.LogInformation($"❌ Login timeout - no login detected within {_phisConfig.ManualLoginWaitSeconds} seconds");
                     LoggerService.LogInformation("   Please restart and log in more quickly, or increase ManualLoginWaitSeconds in config.");
                    return false;
                }

                _isLoggedIn = true;
                _lastSessionActivity = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"❌ Manual login failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Automated login using credentials
        /// </summary>
        private bool LoginAutomated()
        {
            try
            {
                 LoggerService.LogInformation("\n🔐 Logging into PHIS (Automated)...");

                if (string.IsNullOrWhiteSpace(_phisConfig.Username) || string.IsNullOrWhiteSpace(_phisConfig.Password))
                {
                    throw new InvalidOperationException("Username or password not configured for automated login");
                }

                _driver.Navigate().GoToUrl(_phisConfig.LoginUrl);

                _wait.Until(d => d.FindElement(By.Id("username")));

                _driver.FindElement(By.Id("username")).SendKeys(_phisConfig.Username);
                _driver.FindElement(By.Id("password")).SendKeys(_phisConfig.Password);
                _driver.FindElement(By.Id("loginButton")).Click();

                Thread.Sleep(3000);

                 LoggerService.LogInformation("✅ Successfully logged in");

                _isLoggedIn = true;
                _lastSessionActivity = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"❌ Login failed: {ex.Message}");
                return false;
            }
        }

        #endregion Login Methods



        #region Session Refresh

        /// <summary>
        /// Refresh session to prevent timeout
        /// </summary>
        private bool RefreshSession()
        {
             LoggerService.LogInformation($"🔄 Refreshing session...");

            try
            {
                // Navigate to search page to keep session alive
                _driver.Navigate().GoToUrl(_phisConfig.SearchUrl);
                Thread.Sleep(1000);

                // Check if we're still logged in
                if (IsSessionExpired())
                {
                     LoggerService.LogInformation($"❌ Session expired - attempting re-login...");

                    _isLoggedIn = false;

                    // Re-login
                    bool loginSuccess = Login();
                    if (!loginSuccess)
                    {
                         LoggerService.LogInformation($"❌ Re-login failed");
                        return false;
                    }

                     LoggerService.LogInformation($"✅ Session restored successfully");
                }
                else
                {
                     LoggerService.LogInformation($"✅ Session refreshed successfully");
                }

                _lastSessionActivity = DateTime.Now;
                _isLoggedIn = true;
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"❌ Session refresh failed: {ex.Message}");
                _isLoggedIn = false;
                return false;
            }
        }


        #endregion Session Refresh



        #region Navigation Helpers

        /// <summary>
        /// Navigate to search page and verify we're logged in
        /// </summary>
        public async Task<bool> NavigateToSearchPageAsync()
        {
            try
            {
                _driver.Navigate().GoToUrl(_phisConfig.SearchUrl);
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                // Verify we're on the search page (not redirected to login)
                if (IsSessionExpired())
                {
                     LoggerService.LogInformation($"⚠️  Redirected to login page - session expired");
                    return false;
                }

                UpdateActivity();
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"❌ Navigation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Navigate to a specific URL and verify session
        /// </summary>
        public async Task<bool> NavigateToAsync(string url)
        {
            try
            {
                _driver.Navigate().GoToUrl(url);
                await Task.Delay(_phisConfig.PageLoadDelayMs);

                if (IsSessionExpired())
                {
                    return false;
                }

                UpdateActivity();
                return true;
            }
            catch (Exception ex)
            {
                 LoggerService.LogInformation($"❌ Navigation to {url} failed: {ex.Message}");
                return false;
            }
        }

        #endregion Navigation Helpers



        #region Status & Diagnostics


        /// <summary>
        /// Display current session status
        /// </summary>
        public void DisplaySessionStatus()
        {
            var timeRemaining = GetTimeRemaining();
            var timeSinceActivity = DateTime.Now - _lastSessionActivity;

             LoggerService.LogInformation("\n📊 Session Status:");
             LoggerService.LogInformation($"   Logged in: {(_isLoggedIn ? "✅ Yes" : "❌ No")}");
             LoggerService.LogInformation($"   Time since last activity: {timeSinceActivity.TotalMinutes:F1} minutes");
             LoggerService.LogInformation($"   Time until timeout: {timeRemaining.TotalMinutes:F1} minutes");
             LoggerService.LogInformation($"   Auto-refresh: {(_phisConfig.SessionRefreshEnabled ? "✅ Enabled" : "❌ Disabled")}");
             LoggerService.LogInformation($"   Session timeout: {_phisConfig.SessionTimeoutMinutes} minutes\n");
        }

        /// <summary>
        /// Get session statistics
        /// </summary>
        public SessionStatistics GetStatistics()
        {
            return new SessionStatistics
            {
                IsLoggedIn = _isLoggedIn,
                LastActivityTime = _lastSessionActivity,
                TimeSinceLastActivity = DateTime.Now - _lastSessionActivity,
                TimeUntilTimeout = GetTimeRemaining(),
                SessionTimeoutMinutes = _phisConfig.SessionTimeoutMinutes,
                AutoRefreshEnabled = _phisConfig.SessionRefreshEnabled
            };
        }



        #endregion Status & Diagnostics


    }
}
