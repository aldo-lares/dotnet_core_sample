using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using pipelines_dotnet_core.Models;

namespace pipelines_dotnet_core.Controllers
{
    public class HomeController : Controller
    {
        private static readonly List<string> FirstNames = new List<string>
        {
            "Alejandro", "Beatriz", "Carlos", "Diana", "Eduardo",
            "Fernanda", "Gonzalo", "Helena", "Ignacio", "Julia",
            "Kevin", "Laura", "Miguel", "Natalia", "Oscar",
            "Patricia", "Rafael", "Sofia", "Tomas", "Valentina"
        };

        private static readonly List<string> LastNames = new List<string>
        {
            "García", "Martínez", "López", "Sánchez", "Rodríguez",
            "Pérez", "González", "Fernández", "Torres", "Ramírez",
            "Flores", "Rivera", "Morales", "Ortega", "Jiménez",
            "Vargas", "Castro", "Romero", "Herrera", "Mendoza"
        };

        private static readonly Random _rng = new Random();
        private static readonly object _rngLock = new object();
        private static readonly char[] _sessionChars =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        private static readonly DistributedCacheEntryOptions _cacheOptions =
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(24)
            };

        private static readonly object _sessionLock = new object();

        private const string ActiveSessionsKey = "active_sessions";

        private readonly TelemetryClient _telemetryClient;
        private readonly IDistributedCache _cache;

        private const string EnvInstanceId = "WEBSITE_INSTANCE_ID";
        private const string EnvSiteName = "WEBSITE_SITE_NAME";

        public HomeController(TelemetryClient telemetryClient, IDistributedCache cache)
        {
            _telemetryClient = telemetryClient;
            _cache = cache;
        }

        public IActionResult Index()
        {
            ViewData["InstanceName"] = Environment.GetEnvironmentVariable(EnvInstanceId)
                ?? Environment.GetEnvironmentVariable(EnvSiteName)
                ?? "localhost";
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Login()
        {
            string firstName, lastName;
            lock (_rngLock)
            {
                firstName = FirstNames[_rng.Next(FirstNames.Count)];
                lastName = LastNames[_rng.Next(LastNames.Count)];
            }

            string fullName = $"{firstName} {lastName}";
            string loginTime = DateTimeOffset.UtcNow.ToString("o");
            string sessionId = GenerateSessionId();

            _telemetryClient.TrackEvent("UserLogin", new Dictionary<string, string>
            {
                { "userName", fullName },
                { "timestamp", loginTime },
                { "sessionId", sessionId }
            });

            int activeCount;
            lock (_sessionLock)
            {
                var sessions = GetActiveSessions();
                sessions.Add(new ActiveSession { FullName = fullName, SessionId = sessionId });
                SaveActiveSessions(sessions);
                activeCount = sessions.Count;
            }

            return Json(new { fullName, sessionId, activeCount });
        }

        private static string GenerateSessionId()
        {
            var chars = new char[6];
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[6];
                rng.GetBytes(bytes);
                for (int i = 0; i < chars.Length; i++)
                {
                    chars[i] = _sessionChars[bytes[i] % _sessionChars.Length];
                }
            }
            return new string(chars);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            ActiveSession user;
            int activeCount;
            lock (_sessionLock)
            {
                var sessions = GetActiveSessions();
                if (sessions.Count == 0)
                {
                    return Json(new { success = false, message = "No active user sessions." });
                }

                int idx;
                lock (_rngLock) { idx = _rng.Next(sessions.Count); }

                user = sessions[idx];
                sessions.RemoveAt(idx);
                SaveActiveSessions(sessions);
                activeCount = sessions.Count;
            }

            _telemetryClient.TrackEvent("UserLogout", new Dictionary<string, string>
            {
                { "userName", user.FullName },
                { "timestamp", DateTimeOffset.UtcNow.ToString("o") },
                { "sessionId", user.SessionId }
            });

            return Json(new { success = true, fullName = user.FullName, activeCount });
        }

        private List<ActiveSession> GetActiveSessions()
        {
            var json = _cache.GetString(ActiveSessionsKey);
            if (string.IsNullOrEmpty(json))
                return new List<ActiveSession>();
            return JsonConvert.DeserializeObject<List<ActiveSession>>(json) ?? new List<ActiveSession>();
        }

        private void SaveActiveSessions(List<ActiveSession> sessions)
        {
            _cache.SetString(ActiveSessionsKey, JsonConvert.SerializeObject(sessions), _cacheOptions);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
