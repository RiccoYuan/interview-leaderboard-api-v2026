# Leaderboard.LoadTests (NBomber)

In-process **.NET** HTTP load test for `Leaderboard.Api` using [NBomber](https://nbomber.com/) and [NBomber.Http](https://www.nuget.org/packages/NBomber.Http).

## Prerequisites

1. .NET 10 SDK  
2. Start the API (default `http://localhost:5016` from `launchSettings.json`):

   ```bash
   dotnet run --project src/Leaderboard.Api
   ```

## Run

From the repository root:

```bash
dotnet run --project tests/Leaderboard.LoadTests
```

Optional arguments: **`baseUrl` `injectPerSec` `durationSeconds`**

```bash
dotnet run --project tests/Leaderboard.LoadTests -- http://localhost:5016 200 60
```

Or set the base URL via environment variable:

```powershell
$env:LEADERBOARD_URL = "http://localhost:5016"
dotnet run --project tests/Leaderboard.LoadTests
```

## Traffic mix

The `leaderboard_mixed` scenario picks endpoints at random with the approximate mix below:

- ~50% `POST /customer/{id}/score/{delta}` with `delta` in `[-1000, 1000]`
- ~35% `GET /leaderboard?start=&end=`
- ~15% `GET /leaderboard/{id}?high=2&low=2`

## Reports

NBomber writes reports under **`load-reports/`** (relative to the process working directory, usually the repo root when you `dotnet run` from there).
