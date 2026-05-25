using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RuntimeSentinel.Analyzers;

namespace RuntimeSentinel.Scoring;

public sealed class OperationalRiskScorer
{
    public async Task<OperationalRiskReport> AnalyzeSourceAsync(
        string source,
        OperationalRiskScoringOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= OperationalRiskScoringOptions.Default;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);

        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            assemblyName: "RuntimeSentinel.Scoring.Input",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new AvoidThreadSleepAnalyzer(),
            new AvoidUnboundedWhenAllAnalyzer(),
            new AvoidUnboundedParallelForEachAnalyzer(),
            new AvoidBlockingTaskResultOrWaitAnalyzer(),
            new AvoidPrematureToListMaterializationAnalyzer());

        var diagnostics = await compilation.WithAnalyzers(analyzers, options: null)
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        var findings = diagnostics
            .OrderBy(d => d.Id)
            .Select(d => new DiagnosticFinding(
                d.Id,
                d.Descriptor.Category,
                d.Severity.ToString(),
                d.GetMessage(),
                d.Location.GetLineSpan().StartLinePosition.ToString()))
            .ToList();

        var concurrencyRisk = ComputeCategoryRisk(diagnostics, "Concurrency");
        var asyncRisk = ComputeCategoryRisk(diagnostics, "Async");
        var memoryRisk = ComputeCategoryRisk(diagnostics, "Memory");

        var operationalRisk = (int)Math.Round(
            (concurrencyRisk * options.ConcurrencyWeight) +
            (asyncRisk * options.AsyncWeight) +
            (memoryRisk * options.MemoryWeight));

        var operationalRiskLevel = ClassifyRiskLevel(
            operationalRisk,
            options.LowThreshold,
            options.MediumThreshold);

        var justifications = BuildJustifications(
            diagnostics,
            concurrencyRisk,
            asyncRisk,
            memoryRisk,
            operationalRisk,
            operationalRiskLevel,
            options);

        return new OperationalRiskReport(
            ConcurrencyRisk: concurrencyRisk,
            AsyncRisk: asyncRisk,
            MemoryRisk: memoryRisk,
            OperationalRisk: operationalRisk,
                OperationalRiskLevel: operationalRiskLevel,
            Findings: findings,
            Justifications: justifications);
    }

    private static int ComputeCategoryRisk(ImmutableArray<Diagnostic> diagnostics, string category)
    {
        var warnings = diagnostics.Count(d => d.Descriptor.Category == category && d.Severity == DiagnosticSeverity.Warning);
        var errors = diagnostics.Count(d => d.Descriptor.Category == category && d.Severity == DiagnosticSeverity.Error);

        var score = (warnings * 20) + (errors * 35);
        return Math.Min(score, 100);
    }

    private static IReadOnlyList<string> BuildJustifications(
        ImmutableArray<Diagnostic> diagnostics,
        int concurrencyRisk,
        int asyncRisk,
        int memoryRisk,
        int operationalRisk,
        string operationalRiskLevel,
        OperationalRiskScoringOptions options)
    {
        var notes = new List<string>
        {
            $"Concurrency risk score = {concurrencyRisk}.",
            $"Async risk score = {asyncRisk}.",
            $"Memory risk score = {memoryRisk}.",
            $"Operational risk score (weighted) = {operationalRisk}.",
            $"Operational risk level = {operationalRiskLevel}.",
            $"Weights used: Concurrency={options.ConcurrencyWeight:0.##}, Async={options.AsyncWeight:0.##}, Memory={options.MemoryWeight:0.##}.",
            $"Thresholds used: Low<{options.LowThreshold}, Medium<{options.MediumThreshold}, High>={options.MediumThreshold}."
        };

        if (diagnostics.Any(d => d.Id == AvoidUnboundedWhenAllAnalyzer.DiagnosticId))
        {
            notes.Add("Detected unbounded Task.WhenAll fan-out risk (RS1002).");
        }

        if (diagnostics.Any(d => d.Id == AvoidBlockingTaskResultOrWaitAnalyzer.DiagnosticId))
        {
            notes.Add("Detected blocking async flow via .Wait/.Result (RS1004).");
        }

        if (diagnostics.Any(d => d.Id == AvoidPrematureToListMaterializationAnalyzer.DiagnosticId))
        {
            notes.Add("Detected potentially heavy ToList materialization (RS1005).");
        }

        if (diagnostics.Length == 0)
        {
            notes.Add("No analyzer findings were detected in the evaluated source.");
        }

        return notes;
    }

    private static string ClassifyRiskLevel(int operationalRisk, int lowThreshold, int mediumThreshold)
    {
        if (operationalRisk < lowThreshold)
        {
            return "Low";
        }

        if (operationalRisk < mediumThreshold)
        {
            return "Medium";
        }

        return "High";
    }
}
