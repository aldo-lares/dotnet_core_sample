using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
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

        private const string EnvInstanceId = "WEBSITE_INSTANCE_ID";
        private const string EnvSiteName = "WEBSITE_SITE_NAME";

        public HomeController(TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient;
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

            return Json(new { fullName, sessionId });
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

        [HttpGet("/simulate-load")]
        public async Task<IActionResult> SimulateLoad(
            [FromQuery] int cpu = 50,
            [FromQuery] int memory = 100,
            [FromQuery] int duration = 30)
        {
            cpu = Math.Max(0, Math.Min(100, cpu));
            memory = Math.Max(1, Math.Min(4096, memory));
            duration = Math.Max(1, Math.Min(300, duration));

            var cts = new System.Threading.CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(duration));
            var token = cts.Token;

            int threadCount = Math.Max(1, (int)Math.Ceiling(Environment.ProcessorCount * cpu / 100.0));
            var cpuTasks = new Task[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                cpuTasks[i] = Task.Run(() =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            using (var sha = System.Security.Cryptography.SHA256.Create())
                            {
                                sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                }, token);
            }

            var memoryBlocks = new List<byte[]>();
            for (int i = 0; i < memory; i++)
            {
                memoryBlocks.Add(new byte[1024 * 1024]);
            }

            try
            {
                await Task.WhenAll(cpuTasks);
            }
            catch (OperationCanceledException) { }

            memoryBlocks.Clear();
            cts.Dispose();

            return Json(new
            {
                message = $"Simulation complete: ran for {duration}s using ~{cpu}% CPU on {threadCount} thread(s) and {memory} MB of memory."
            });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
