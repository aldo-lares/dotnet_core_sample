using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ApplicationInsights;
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

        private readonly TelemetryClient _telemetryClient;

        public HomeController(TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient;
        }

        public IActionResult Index()
        {
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
            string sessionId;
            lock (_rngLock)
            {
                firstName = FirstNames[_rng.Next(FirstNames.Count)];
                lastName = LastNames[_rng.Next(LastNames.Count)];
                sessionId = new string(Enumerable.Range(0, 6)
                    .Select(_ => _sessionChars[_rng.Next(_sessionChars.Length)])
                    .ToArray());
            }

            string fullName = $"{firstName} {lastName}";
            string loginTime = DateTimeOffset.UtcNow.ToString("o");

            _telemetryClient.TrackEvent("UserLogin", new Dictionary<string, string>
            {
                { "userName", fullName },
                { "timestamp", loginTime },
                { "sessionId", sessionId }
            });

            return Json(new { fullName, sessionId });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
