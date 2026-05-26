# Performance results

Recorded on **Windows 11**, **AMD Ryzen 7 5700G**, **.NET 10.0.8**, with **~10k** seeded users (`IndexComparisonBenchmarks` global setup).

Reproduce locally:

```bash
# In-process (BenchmarkDotNet)
dotnet run -c Release --project tests/Leaderboard.Benchmarks
dotnet run -c Release --project tests/Leaderboard.Benchmarks -- --job Short --filter *IndexComparison*

# HTTP (start API first)
dotnet run --project src/Leaderboard.Api
dotnet run --project tests/Leaderboard.LoadTests -- http://localhost:5016 100 30
```

CI runs a **Dry** benchmark smoke and a **5 s** NBomber smoke at **20 req/s** (see [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)).

## BenchmarkDotNet — Short job (2026-05-26)

| Method | SkipList | B+ tree | Red-black |
|--------|----------|---------|-----------|
| Range top 50 | 577 ns | 521 ns | 738 ns |
| Single update | 239 ns | 270 ns | 351 ns |
| Neighborhood (high=2, low=3) | 788 µs | 811 µs | 970 µs |

Neighborhood row from a **Dry** smoke on the same machine (Short job iterations are too short for stable μs-scale timing on that benchmark). Range/update from **Short** job summary table.

**Takeaway:** With 10k seeded users (about 5k active entries with score > 0 in this benchmark setup), range reads and single updates are sub-microsecond to a few hundred nanoseconds; neighborhood queries cost ~0.8–1.0 ms per call on this hardware. Skip list and B+ tree are closest on updates; B+ tree is slightly fastest on range top-50 in this run.

## NBomber HTTP — mixed workload (2026-05-26)

Setup: API on `http://127.0.0.1:5016`, **SkipList**, **100 req/s** target, **30 s**, ~50% POST / 35% range GET / 15% neighborhood GET.

| Metric | Value |
|--------|-------|
| OK count | 2592 |
| HTTP 404 (off-board neighborhood) | 408 |
| OK RPS | ~86 |
| OK latency p50 | 0.53 ms |
| OK latency p99 | 27.5 ms |

404s are expected when neighborhood GET hits a customer id that is not on the board (random ids in the load script). See HTML/CSV under `load-reports/` after a local run.
