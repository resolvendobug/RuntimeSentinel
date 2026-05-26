<p align="center">
  <img src="img/banner_Image.png" alt="RuntimeSentinel banner" />
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/RuntimeSentinel.Analyzers"><img src="https://img.shields.io/nuget/v/RuntimeSentinel.Analyzers?label=NuGet&color=004880&logo=nuget" alt="NuGet" /></a>
  <img src="https://img.shields.io/badge/.NET-netstandard2.0+-512BD4?logo=dotnet" alt=".NET" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
  <img src="https://img.shields.io/badge/tests-49%20passing-brightgreen" alt="Tests" />
</p>

<p align="center">
  <strong>Roslyn analyzer toolkit for .NET operational stability.</strong><br/>
  Detect concurrency, async, memory, and HTTP risks at compile time — before they reach production.
</p>

<p align="center">
  🇧🇷 <a href="README.pt-BR.md">Versão em Português</a>
</p>

---

## Why RuntimeSentinel

Most static analyzers focus on style and correctness. Operational risks — concurrency issues, retry storms, memory pressure under load — typically surface late in the cycle: during load tests or production incidents.

RuntimeSentinel shifts that detection earlier by making high-impact runtime risks visible **at compile time**, in your IDE and in CI pipelines. No extra tooling, no runtime overhead — just warnings where you write code.

| | |
|---|---|
| ⚡ Stability under load | 🔒 Safe concurrency |
| 📡 Resilient HTTP patterns | 🧠 Memory efficiency |
| ⏱ Predictable throughput | 🔁 Retry safety |

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
| RS1011 | Async | `async void` method outside event handler | Exceptions escape the call stack and crash the process; caller cannot await or handle them |
| RS1012 | Memory | `string +=` inside a loop | Each iteration allocates a new string on the heap; use `StringBuilder` to avoid GC pressure |

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

**Install via NuGet (recommended):**

```bash
dotnet add package RuntimeSentinel.Analyzers
```

Or add directly to your `.csproj`:

```xml
<PackageReference Include="RuntimeSentinel.Analyzers" Version="0.1.3" />
```

Warnings appear automatically in Visual Studio, VS Code (C# Dev Kit), Rider, and CI pipelines.

**Build from source:**

```bash
git clone https://github.com/resolvendobug/RuntimeSentinel
dotnet build RuntimeSentinel.slnx
dotnet test test/RuntimeSentinel.Analyzers.Tests/RuntimeSentinel.Analyzers.Tests.csproj
```

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

RuntimeSentinel is purpose-built for operational stability analysis in C#/.NET — not a general-purpose linter.

In scope:
- Roslyn diagnostic analyzers (RS1001–RS1010)
- Automatic code fixes for selected rules
- Operational risk scoring engine with JSON and Markdown output

Out of scope for v1:
- Generic style or naming convention linting
- Replacing load tests or runtime monitoring
- Full observability platform features

## Contributing

Contributions are welcome!

1. Open an issue to discuss the proposed change
2. Fork the repository and create a feature branch
3. Add tests for new analyzers or fixes
4. Submit a pull request referencing the issue

If you have a production scenario that led to an incident and would make a good new rule, that context is especially valuable.

## FAQ

**How do I suppress a specific warning?**

Inline suppression:
```csharp
#pragma warning disable RS1011
public static async void FireAndForget() { ... }
#pragma warning restore RS1011
```
Project-wide suppression in your `.csproj`:
```xml
<PropertyGroup>
  <NoWarn>RS1011</NoWarn>
</PropertyGroup>
```

**Can I configure thresholds (e.g. RS1002 concurrency limit)?**

Yes, via `.editorconfig`:
```ini
[*.cs]
dotnet_diagnostic.RS1002.max_concurrent_tasks = 50
dotnet_diagnostic.RS1005.max_materialized_items = 200
```

**Does it work with .NET Framework projects?**

Yes. The analyzer targets `netstandard2.0` and is compatible with .NET Framework 4.6.1+ and all .NET Core / .NET 5+ versions.

**What is `RuntimeSentinel.Scoring` for?**

It aggregates all diagnostics from the analyzer and produces a single **Operational Risk Score** for the project, useful in CI pipelines to block or warn on high-risk code before deployment.

**Will analyzers run in CI?**

Yes. Roslyn analyzers run as part of `dotnet build`. Any warning can be escalated to an error in CI by adding to your `.csproj`:
```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```
or selectively: `<WarningsAsErrors>RS1011;RS1012</WarningsAsErrors>`

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).

