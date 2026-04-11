using ConsentSyncCore.Models;
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
                Console.WriteLine($"\n⚠️  Session timeout approaching ({timeUntilTimeout.TotalMinutes:F1} min remaining)");
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
                Console.WriteLine("\n👤 MANUAL LOGIN MODE");
                Console.WriteLine("══════════════════════════════════════════════════════");

                _driver.Navigate().GoToUrl(_phisConfig.LoginUrl);

                Console.WriteLine($"📌 Browser opened to: {_phisConfig.LoginUrl}");
                Console.WriteLine($"\n⏳ Please log in manually within {_phisConfig.ManualLoginWaitSeconds} seconds...");
                Console.WriteLine("   The automation will start once you're logged in.");
                Console.WriteLine("\n💡 TIP: Navigate to the PHIS dashboard after logging in.");
                Console.WriteLine("══════════════════════════════════════════════════════\n");

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
                        Console.WriteLine($"✅ Login detected! Current URL: {currentUrl}");
                        Console.WriteLine("🚀 Starting automation...\n");
                        Thread.Sleep(2000); // Give page time to fully load
                        break;
                    }

                    var remaining = (int)(endTime - DateTime.Now).TotalSeconds;
                    if (remaining % 10 == 0 && remaining > 0)
                    {
                        Console.WriteLine($"   ⏰ {remaining} seconds remaining...");
                    }
                }

                if (!loggedIn)
                {
                    Console.WriteLine($"❌ Login timeout - no login detected within {_phisConfig.ManualLoginWaitSeconds} seconds");
                    Console.WriteLine("   Please restart and log in more quickly, or increase ManualLoginWaitSeconds in config.");
                    return false;
                }

                _isLoggedIn = true;
                _lastSessionActivity = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Manual login failed: {ex.Message}");
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
                Console.WriteLine("\n🔐 Logging into PHIS (Automated)...");

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

                Console.WriteLine("✅ Successfully logged in");

                _isLoggedIn = true;
                _lastSessionActivity = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Login failed: {ex.Message}");
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
            Console.WriteLine($"🔄 Refreshing session...");

            try
            {
                // Navigate to search page to keep session alive
                _driver.Navigate().GoToUrl(_phisConfig.SearchUrl);
                Thread.Sleep(1000);

                // Check if we're still logged in
                if (IsSessionExpired())
                {
                    Console.WriteLine($"❌ Session expired - attempting re-login...");

                    _isLoggedIn = false;

                    // Re-login
                    bool loginSuccess = Login();
                    if (!loginSuccess)
                    {
                        Console.WriteLine($"❌ Re-login failed");
                        return false;
                    }

                    Console.WriteLine($"✅ Session restored successfully");
                }
                else
                {
                    Console.WriteLine($"✅ Session refreshed successfully");
                }

                _lastSessionActivity = DateTime.Now;
                _isLoggedIn = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Session refresh failed: {ex.Message}");
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
                    Console.WriteLine($"⚠️  Redirected to login page - session expired");
                    return false;
                }

                UpdateActivity();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Navigation failed: {ex.Message}");
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
                Console.WriteLine($"❌ Navigation to {url} failed: {ex.Message}");
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

            Console.WriteLine("\n📊 Session Status:");
            Console.WriteLine($"   Logged in: {(_isLoggedIn ? "✅ Yes" : "❌ No")}");
            Console.WriteLine($"   Time since last activity: {timeSinceActivity.TotalMinutes:F1} minutes");
            Console.WriteLine($"   Time until timeout: {timeRemaining.TotalMinutes:F1} minutes");
            Console.WriteLine($"   Auto-refresh: {(_phisConfig.SessionRefreshEnabled ? "✅ Enabled" : "❌ Disabled")}");
            Console.WriteLine($"   Session timeout: {_phisConfig.SessionTimeoutMinutes} minutes\n");
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
