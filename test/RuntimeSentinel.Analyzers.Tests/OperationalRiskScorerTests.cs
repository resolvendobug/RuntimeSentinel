using RuntimeSentinel.Scoring;

namespace RuntimeSentinel.Analyzers.Tests;

public class OperationalRiskScorerTests
{
    [Fact]
    public async Task Should_Return_Zero_Scores_When_No_Findings_Are_Detected()
    {
        const string source = """
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync()
    {
        await Task.Delay(1);
    }
}
""";

        var scorer = new OperationalRiskScorer();
        var report = await scorer.AnalyzeSourceAsync(source);

        Assert.Equal(0, report.ConcurrencyRisk);
        Assert.Equal(0, report.AsyncRisk);
        Assert.Equal(0, report.MemoryRisk);
        Assert.Equal(0, report.OperationalRisk);
        Assert.Equal("Low", report.OperationalRiskLevel);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task Should_Compose_OperationalRisk_From_Multiple_Categories()
    {
        const string source = """
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync(int[] items)
    {
        await Task.WhenAll(items.Select(async item =>
        {
            await Task.Delay(1);
            _ = item;
        }));

        var task = Task.FromResult(42);
        _ = task.Result;

        _ = items.Where(x => x > 10).Select(x => x * 2).ToList();

        Thread.Sleep(5);
    }
}
""";

        var scorer = new OperationalRiskScorer();
        var report = await scorer.AnalyzeSourceAsync(source);

        Assert.True(report.ConcurrencyRisk > 0);
        Assert.True(report.AsyncRisk > 0);
        Assert.True(report.MemoryRisk > 0);
        Assert.True(report.OperationalRisk > 0);
        Assert.Equal("Medium", report.OperationalRiskLevel);
        Assert.NotEmpty(report.Findings);

        var markdown = report.ToMarkdown();
        var json = report.ToJson();

        Assert.Contains("Operational Risk", markdown);
        Assert.Contains("\"OperationalRisk\"", json);
        Assert.Contains("\"OperationalRiskLevel\"", json);
    }

    [Fact]
    public async Task Should_Allow_Custom_Weights_For_Calibration()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync()
    {
        Thread.Sleep(5);
        var task = Task.FromResult(42);
        _ = task.Result;
    }
}
""";

        var scorer = new OperationalRiskScorer();

        var defaultReport = await scorer.AnalyzeSourceAsync(source);
        var calibratedReport = await scorer.AnalyzeSourceAsync(
            source,
            new OperationalRiskScoringOptions(
                ConcurrencyWeight: 0.2,
                AsyncWeight: 0.7,
                MemoryWeight: 0.1,
                LowThreshold: 15,
                MediumThreshold: 50));

        Assert.NotEqual(defaultReport.OperationalRisk, calibratedReport.OperationalRisk);
        Assert.Equal("Medium", calibratedReport.OperationalRiskLevel);
    }

    [Fact]
    public async Task Should_Classify_High_Risk_With_Custom_Thresholds()
    {
        const string source = """
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Worker
{
    public async Task RunAsync(int[] items)
    {
        await Task.WhenAll(items.Select(async item =>
        {
            await Task.Delay(1);
            _ = item;
        }));

        var task = Task.FromResult(42);
        _ = task.Result;

        _ = items.Where(x => x > 10).Select(x => x * 2).ToList();

        Thread.Sleep(5);
    }
}
""";

        var scorer = new OperationalRiskScorer();
        var report = await scorer.AnalyzeSourceAsync(
            source,
            new OperationalRiskScoringOptions(
                ConcurrencyWeight: 0.5,
                AsyncWeight: 0.3,
                MemoryWeight: 0.2,
                LowThreshold: 10,
                MediumThreshold: 30));

        Assert.Equal("High", report.OperationalRiskLevel);
    }
}
