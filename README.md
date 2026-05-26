# 内存排行榜（.NET）

> **AI 使用说明**：本项目全部源代码由 AI 生成，我负责需求拆解、方案论证、架构设计、技术取舍与质量验收。

---

## 项目简介

这是一个 take-home 风格的**内存排行榜**服务，核心规则如下：

- **分数越高，名次越好**（第 1 名为榜首）。
- **同分**时，`customerId` **更小**者名次更好。
- 仅 **score > 0** 的用户出现在榜上（≤ 0 仍保存在分数字典中，但不进入有序索引）。
- 每次更新为 **增量 delta**，且必须在 **[-1000, +1000]** 范围内。

三种可互换的有序索引实现共用同一套 API 与测试：

| 索引 | 思路 | 优势场景 |
|------|------|----------|
| **跳表**（默认） | 带 **span** 的可索引跳表，按名次跳转；思路参考 Redis 有序集合 | 单次更新最快，最均衡（默认实现） |
| **B+ 树** | 叶节点双向链表 + 内部节点分隔键 + **子树计数** | Range 查询最快 |
| **红黑树** | **左倾红黑 BST** + 每节点 **子树大小**（order-statistic tree） | 邻域查询最快 |

并发：`LeaderboardService` 维护 `Dictionary<long, decimal>` 与 `ISortedRankIndex`，由 **`ReaderWriterLockSlim`** 保护（读并发、写互斥）。

**选型结论**：三轮实测显示三种结构各有一个领先场景，无全面领先者。跳表从未最慢、始终在最优者 35% 以内，结合 Redis 工业实践，作为默认实现。详见 [`docs/performance-results.md`](docs/performance-results.md)。

---

## 解题思路

本题的关键不是普通 CRUD，而是：在大量用户和并发请求下，持续维护一个支持**按名次查询**的内存排行榜。PDF 明确提示避免在查询 API 中排序，因此我将问题抽象为：维护一个支持 **order statistics** 的有序索引。

- **核心约束**：`POST /customer/{id}/score/{delta}` 增量更新；`GET /leaderboard?start=&end=` 按 1 起始名次区间查询；`GET /leaderboard/{id}?high=&low=` 查询指定用户邻域。
- **排序规则**：按 `score` 降序、`customerId` 升序；只有 `score > 0` 进入排行榜。
- **数据结构选择**：通过多轮技术论证，确定三种代表性增强有序结构：带 span 的跳表、带子树计数的 B+ 树、带子树大小的红黑树。
- **工程决策**：三种结构都实现到统一接口 `ISortedRankIndex` 下，通过 `appsettings` 切换，用同一套 API、测试和 Benchmark 验证；GitHub Actions 作为补充自动化检查保留。
- **并发边界**：索引内部不分散加锁，由 `LeaderboardService` 用 `ReaderWriterLockSlim` 统一保护「分数字典 + 有序索引」。
- **目标边界**：本项目聚焦 take-home 要求，不扩展持久化、复制、分片、鉴权、限流等生产化能力。

---

## 工程质量控制

接口抽象、统一测试、分层架构和并发控制来自我长期软件开发中的工程习惯；GitHub Actions 作为项目完整性的自动化验证补充。这个项目的质量控制重点如下：

| 工程决策 | 作用 |
|----------|------|
| **Core / Api 分层** | HTTP 层与业务规则、索引结构解耦，便于独立测试 |
| **统一接口 `ISortedRankIndex`** | 三种索引可替换，避免 API 依赖具体数据结构 |
| **同一套测试覆盖三种实现** | 防止每个实现各测各的，降低验证偏差 |
| **服务层集中读写锁** | 一处维护并发语义，避免锁策略散落在数据结构中 |
| **BenchmarkDotNet + NBomber** | 同时覆盖进程内数据结构性能与真实 HTTP 混合负载 |
| **GitHub Actions 自动化验证** | 作为补充配置，在提交或 PR 时运行 test、benchmark smoke、load smoke |

正确性覆盖包括 PDF 样例、同分排序、降分、掉榜、邻域语义、随机对照测试、并发 fuzz 和 API 集成测试。我的工程底线是：**代码可扩展、实现可替换、正确性可验证、性能可度量、交付可复现**。

---

## 工程化补充

| 方向 | 实现 |
|------|------|
| **量化结果** | [`docs/performance-results.md`](docs/performance-results.md) — BenchmarkDotNet Short + NBomber 实测摘要 |
| **自动化验证** | [`.github/workflows/ci.yml`](.github/workflows/ci.yml) — `dotnet test`、Dry benchmark、NBomber 5 s smoke |
| **API 集成测试** | [`tests/Leaderboard.Tests/ApiIntegrationTests.cs`](tests/Leaderboard.Tests/ApiIntegrationTests.cs) — `WebApplicationFactory` 覆盖三个 HTTP 端点 |
| **OpenAPI / Swagger** | 开发环境：`/swagger`（Swashbuckle） |
| **一键脚本** | [`build.ps1`](build.ps1)、[`Makefile`](Makefile) — `restore → build → test → benchmark` |

仍可继续加强的方向：**可观测性**（`/health`、Prometheus）、**持久化扩展点**文档。

### 性能结论（测试环境：约 1 万名种子用户，5 千上榜，Ryzen 7 5700G / .NET 10）

| 场景 | 跳表 | B+ 树 | 红黑树 | 分场景最优 |
|------|------|-------|--------|------------|
| Top-50 名次区间 | ~496 ns | **~458 ns** | ~663 ns | **B+ 树** |
| 单次增量更新 | **~225 ns** | ~263 ns | ~362 ns | **跳表** |
| 邻域查询（high=2, low=3） | ~173 ns | ~235 ns | **~151 ns** | **红黑树** |

> 数据为 3 轮独立 Short job 均值（同一 job 覆盖三个场景）。原始 benchmark 存在两个 bug——返回值未消费导致 JIT 消除 Neighborhood 调用、被查询 id 的 score ≤ 0 命中快速返回路径——已一并修复；详见 `docs/performance-results.md`。

HTTP 混合压测（100 req/s 目标、30 s）：成功请求 **~86 RPS**，OK 延迟 **p50 ≈ 0.5 ms**、**p99 ≈ 28 ms**；邻域请求对部分未上榜 id 返回 404 属预期。

```bash
# 克隆后一条命令（PowerShell）
.\build.ps1

# 或 Make
make all
```

---

## 仓库结构

```
Leaderboard.sln
src/
  Leaderboard.Core/     # 领域模型、三种索引、LeaderboardService
  Leaderboard.Api/      # Minimal HTTP API
tests/
  Leaderboard.Tests/        # xUnit — Core 正确性、并发 fuzz、API 集成测试
  Leaderboard.Benchmarks/   # BenchmarkDotNet — 跳表 vs B+ 树 vs 红黑树
docs/
  performance-results.md   # 基准与压测量化数据
.github/workflows/
  ci.yml                     # GitHub Actions 自动化验证补充
```

---

## 环境要求

- [.NET SDK](https://dotnet.microsoft.com/download) **10.0**（见根目录 `global.json`，已启用 `rollForward: latestFeature`）。

---

## 快速开始

在仓库根目录执行：

```bash
dotnet restore
dotnet build
dotnet test
```

或使用一键脚本（含 Dry benchmark 冒烟）：

```powershell
.\build.ps1
# 跳过 benchmark： .\build.ps1 -SkipBenchmark
```

```bash
make all          # restore + build + test + benchmark
make test         # 仅测
make benchmark    # 需先 build
```

### 启动 API

```bash
dotnet run --project src/Leaderboard.Api
```

默认地址：**http://localhost:5016**（见 `src/Leaderboard.Api/Properties/launchSettings.json`）。

**开发环境**下打开 Swagger UI：**http://localhost:5016/swagger**

在 `src/Leaderboard.Api/appsettings.json` 中切换索引：

```json
"Leaderboard": {
  "Storage": "SkipList"
}
```

可选值：`"BPlusTree"`（B+ 树）、`"RedBlackTree"`（红黑树）。未识别时回退为跳表。

### API 示例

```bash
# 给用户 1001 加 50 分
curl -X POST http://localhost:5016/customer/1001/score/50

# 第 1～10 名
curl "http://localhost:5016/leaderboard?start=1&end=10"

# 该用户前 2 名（更好）+ 后 3 名（更差）
curl "http://localhost:5016/leaderboard/1001?high=2&low=3"
```

---

## 微基准测试（进程内）

在约 1 万名种子用户数据上对比三种索引：

```bash
dotnet run -c Release --project tests/Leaderboard.Benchmarks
```

快速冒烟（Dry job）：

```bash
dotnet run -c Release --project tests/Leaderboard.Benchmarks -- --job Dry --filter *IndexComparison*
```

报告输出在 `BenchmarkDotNet.Artifacts/`（已 gitignore）。完整数字见 [`docs/performance-results.md`](docs/performance-results.md)。

---

## 自动化验证（GitHub Actions）

仓库保留了一个 GitHub Actions 配置，用于在推送至 `main` / `master` 或 PR 时做基础自动化验证：

1. `dotnet test`（含 API 集成测试）
2. BenchmarkDotNet **Dry** 冒烟（`*IndexComparison*`）

这部分不是本项目的核心展示点，主要用于补充项目完整性。工作流定义： [`.github/workflows/ci.yml`](.github/workflows/ci.yml)。

---

## 设计说明

### 排序与名次

- 排序键：`(score 降序, customerId 升序)`，由 `RankKey.Compare` 定义全序。
- 更新时：若旧分 > 0 则从索引删除旧键；若新分 > 0 则插入新键。

### 复杂度（典型）

| 操作 | 跳表（含 span） | B+ 树（含计数） | 红黑树（含子树大小） |
|------|-----------------|-----------------|----------------------|
| 索引更新 | O(log n) 期望 | O(log n) | O(log n) |
| 按名次查询 / 区间 / 邻域 | O(log n) + 输出规模 | O(log n) + 输出规模 | O(log n) + 输出规模 |

常数因子因实现与硬件而异，以 BenchmarkDotNet 实测为准。

### 有意未覆盖的范围

- 持久化、复制、分片。
- 鉴权、限流。
- score ≤ 0 的用户不上榜（业务规则，非 bug）。

---

## NuGet 还原

根目录 `nuget.config` 清空其它源、仅使用 **nuget.org**，避免私有源不可用时 `dotnet restore` 失败。若你的环境依赖其它 feed，可自行修改或删除该文件。

---

## 许可证

未指定 — 默认视为私有 take-home / 评测用途；如需开源请自行添加 LICENSE 文件。
