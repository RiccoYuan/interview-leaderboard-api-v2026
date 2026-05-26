using NBomber.CSharp;
using NBomber.Http.CSharp;

// HTTP load test for Leaderboard.Api (start the API first).
// Usage:
//   dotnet run --project loadtests/Leaderboard.LoadTests -- [baseUrl] [injectPerSec] [durationSeconds]
//   $env:LEADERBOARD_URL="http://localhost:5016"; dotnet run --project loadtests/Leaderboard.LoadTests
// Defaults: http://localhost:5016, 100 req/s, 30 s

var baseUrl = Environment.GetEnvironmentVariable("LEADERBOARD_URL");
if (string.IsNullOrWhiteSpace(baseUrl) && args.Length > 0)
    baseUrl = args[0];
baseUrl = (baseUrl ?? "http://localhost:5016").TrimEnd('/');

var injectPerSec = args.Length > 1 && int.TryParse(args[1], out var r) ? r : 100;
var durationSec = args.Length > 2 && int.TryParse(args[2], out var d) ? d : 30;

Console.WriteLine($"NBomber HTTP load -> {baseUrl} | inject {injectPerSec}/s | {durationSec}s");

using var http = Http.CreateDefaultClient();

var scenario = Scenario.Create("leaderboard_mixed", async context =>
    {
        var roll = Random.Shared.Next(100);
        if (roll < 50)
        {
            var id = Random.Shared.Next(1, 8001);
            var delta = Random.Shared.Next(-1000, 1001);
            var req = Http.CreateRequest("POST", $"{baseUrl}/customer/{id}/score/{delta}");
            return await Http.Send(http, req);
        }

        if (roll < 85)
        {
            var start = Random.Shared.Next(1, 501);
            var span = Random.Shared.Next(1, 81);
            var req = Http.CreateRequest("GET", $"{baseUrl}/leaderboard?start={start}&end={start + span}");
            return await Http.Send(http, req);
        }

        {
            var id = Random.Shared.Next(1, 8001);
            var req = Http.CreateRequest("GET", $"{baseUrl}/leaderboard/{id}?high=2&low=2");
            return await Http.Send(http, req);
        }
    })
    .WithoutWarmUp()
    .WithLoadSimulations(
        Simulation.Inject(
            rate: injectPerSec,
            interval: TimeSpan.FromSeconds(1),
            during: TimeSpan.FromSeconds(durationSec)));

NBomberRunner
    .RegisterScenarios(scenario)
    .WithTestName("leaderboard_http")
    .WithReportFolder("load-reports")
    .Run();
