# RuntimeSentinel

![.NET](https://img.shields.io/badge/.NET-netstandard2.0+-512BD4?logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green)
![Tests](https://img.shields.io/badge/tests-36%20passing-brightgreen)

Portuguese (Brazil) version: [README.pt-BR.md](README.pt-BR.md)

RuntimeSentinel is an open source Roslyn analyzer toolkit focused on **operational stability** for .NET applications.

It was created to answer practical production questions **early in development**, such as:

- Can this code saturate dependencies under high load?
- Can this async flow block threads and reduce throughput?
- Can this pattern increase memory pressure and GC churn?
- Can retry logic amplify failures instead of containing them?

## Why This Library Exists

Most static analyzers focus on style and correctness. Operational risks — concurrency issues, retry storms, memory pressure under load — typically surface late in the cycle: during load tests or production incidents.

RuntimeSentinel shifts that detection earlier by making high-impact runtime risks visible **at compile time**, in your IDE and in CI pipelines.

Focused on:

- Stability under load
- Safe concurrency
- Predictable throughput
- Memory efficiency
- Resilient communication patterns

## Implemented Rules

| Rule | Category | What It Flags | Why It Matters |
|---|---|---|---|
| RS1001 | Concurrency | `Thread.Sleep` inside `async` methods | Blocks real worker threads; under load this starves the thread pool |
| RS1002 | Concurrency | `Task.WhenAll` fan-out without an explicit bound | Unbounded fan-out can flood downstream dependencies |
| RS1003 | Concurrency | `Parallel.ForEach` without `MaxDegreeOfParallelism` | Can oversubscribe CPU and saturate shared resources |
| RS1004 | Async | Blocking calls `.Wait()` / `.Result` in async context | Blocks threads and raises deadlock/starvation risk |
| RS1005 | Memory | `ToList()` materialization on unbounded queries | Can spike memory and trigger GC pressure at scale |
| RS1006 | Communication | `new HttpClient()` created inside a method | Per-request instantiation causes socket exhaustion over time |
| RS1007 | Communication | HTTP call without explicit timeout or `CancellationToken` | Requests can hang indefinitely under latency spikes |
| RS1008 | Communication | Retry loop without an explicit max-attempt limit | Infinite retries amplify load on an already failing dependency |
| RS1009 | Communication | Retry with fixed delay instead of exponential backoff | Fixed interval keeps hammering unstable dependencies at high frequency |
| RS1010 | Communication | Exponential backoff without jitter | Synchronized retries across instances cause coordinated traffic bursts |

## Code Fix — RS1002

For RS1002 (`Task.WhenAll` unbounded fan-out), RuntimeSentinel ships an automatic code fix with two strategies:

**Before:**
```csharp
var results = await Task.WhenAll(items.Select(i => ProcessAsync(i)));
```

**After — option 1: limit with `Take`:**
```csharp
var results = await Task.WhenAll(items.Take(100).Select(i => ProcessAsync(i)));
```

**After — option 2: limit concurrency with `SemaphoreSlim`:**
```csharp
var semaphore = new SemaphoreSlim(10);
var results = await Task.WhenAll(items.Select(async i =>
{
    await semaphore.WaitAsync();
    try { return await ProcessAsync(i); }
    finally { semaphore.Release(); }
}));
```

## Operational Risk Scorer

Beyond individual diagnostics, RuntimeSentinel includes a scoring engine that produces a **composite operational risk report** for a C# source snippet.

```csharp
var scorer = new OperationalRiskScorer();
var report = await scorer.AnalyzeSourceAsync(sourceCode);

Console.WriteLine(report.OperationalRisk);     // 0-100
Console.WriteLine(report.OperationalRiskLevel); // Low / Medium / High
Console.WriteLine(report.ToMarkdown());
Console.WriteLine(report.ToJson());
```

The report includes:

| Field | Description |
|---|---|
| `ConcurrencyRisk` | 0-100 score for concurrency issues |
| `AsyncRisk` | 0-100 score for async blocking issues |
| `MemoryRisk` | 0-100 score for memory pressure issues |
| `OperationalRisk` | Weighted composite score (0-100) |
| `OperationalRiskLevel` | `Low` / `Medium` / `High` classification |
| `Findings` | List of individual diagnostics with location |
| `Justifications` | Human-readable explanations per finding |

Default weights: Concurrency `50%` · Async `30%` · Memory `20%`.  
Thresholds: `Low < 30` · `30 ≤ Medium < 70` · `High ≥ 70`.

Weights and thresholds are configurable via `OperationalRiskScoringOptions`.

## Quick Start

1. Clone and build:

```bash
git clone https://github.com/your-org/RuntimeSentinel
dotnet build RuntimeSentinel.slnx
```

2. Run all tests:

```bash
dotnet test test/RuntimeSentinel.Analyzers.Tests/RuntimeSentinel.Analyzers.Tests.csproj
```

3. Reference the NuGet package in your project (once published).

## Compatibility

- Target framework: `netstandard2.0`
- Compatible with .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5/6/7/8/9/10+
- Analyzer integrates with any Roslyn-powered IDE (Visual Studio, VS Code with C# Dev Kit, Rider)

## Repository Structure

```
src/
  RuntimeSentinel.Analyzers/    Roslyn diagnostic analyzers (RS1001-RS1010)
  RuntimeSentinel.CodeFixes/    Automatic code fixes for selected rules
  RuntimeSentinel.Scoring/      Operational risk scoring engine
test/
  RuntimeSentinel.Analyzers.Tests/  Full test suite (36 tests)
samples/
  RuntimeSentinel.SampleApp/    Practical labs with before/after examples
```

## Scope

RuntimeSentinel is focused on analyzers and operational risk scoring primitives for C#/.NET codebases.

Out of scope for v1:

- Generic style or naming convention linting
- Replacing load tests or runtime monitoring
- Full observability platform features

## Contributing

Contributions are welcome. Please open an issue before submitting a pull request to discuss the proposed change.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).

