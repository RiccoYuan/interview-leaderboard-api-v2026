# Performance results

Recorded on **Windows 11**, **AMD Ryzen 7 5700G**, **.NET 10.0.8**, with **~10k** seeded users (`IndexComparisonBenchmarks` global setup, ~5k active entries with score > 0).

Reproduce locally:

```bash
# In-process (BenchmarkDotNet)
dotnet run -c Release --project tests/Leaderboard.Benchmarks -- --job Short --filter *IndexComparison*
```

CI runs a **Dry** benchmark smoke on every push (see [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)).

## BenchmarkDotNet — Short job (2026-05-26, 3-run average)

All three scenarios measured with the same **Short** job (IterationCount=3, WarmupCount=3), run **3 independent times**; figures below are the mean of the three per-run means.

> **Note on earlier (pre-fix) results:** The original benchmark had two bugs that made neighborhood numbers meaningless: (1) `void`-returning methods let JIT dead-code-eliminate the entire call (0 allocations, ~13 ns); (2) the queried customer id `1_000_500` had score ≤ 0 in the seed data, always hitting the fast-path null return instead of traversing the index. Fixed: neighborhood methods write `result?.Count` to a `volatile` field; id changed to `1_001_500` (seed delta = +500, score = 500 > 0, confirmed on-board with 464 B allocation per call).

| Scenario | SkipList | B+ tree | Red-black | Winner |
|----------|----------|---------|-----------|--------|
| Range top-50 | ~496 ns | **~458 ns** | ~663 ns | **B+ tree** |
| Single update | **~225 ns** | ~263 ns | ~362 ns | **SkipList** |
| Neighborhood (high=2, low=3) | ~173 ns | ~235 ns | **~151 ns** | **Red-black** |

Raw per-run means (ns):

| Scenario | Run 1 | Run 2 | Run 3 |
|----------|-------|-------|-------|
| SkipList Range | 470 | 567 | 450 |
| BPlusTree Range | 462 | 459 | 454 |
| RedBlackTree Range | 596 | 763 | 629 |
| SkipList Update | 239 | 226 | 209 |
| BPlusTree Update | 265 | 260 | 264 |
| RedBlackTree Update | 341 | 410 | 336 |
| SkipList Neighborhood | 166 | 188 | 165 |
| BPlusTree Neighborhood | 225 | 245 | 234 |
| RedBlackTree Neighborhood | 165 | 145 | 143 |

**Takeaway:** Each structure leads in a different scenario. B+ tree wins Range top-50 consistently (~8% faster than skip list). Skip list wins single update consistently (~14% faster than B+ tree, ~37% faster than red-black). Red-black wins neighborhood consistently (~13% faster than skip list, ~36% faster than B+ tree). No structure dominates all three. Skip list is the most **balanced** choice: never the slowest, always within ~35% of the per-scenario winner, and its update advantage matters most in write-heavy leaderboard workloads. Combined with established industrial practice (Redis sorted sets), skip list is the default implementation.

