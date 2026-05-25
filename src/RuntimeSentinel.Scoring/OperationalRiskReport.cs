using System.Text;
using System.Text.Json;

namespace RuntimeSentinel.Scoring;

public sealed record DiagnosticFinding(string Id, string Category, string Severity, string Message, string Location);

public sealed record OperationalRiskReport(
    int ConcurrencyRisk,
    int AsyncRisk,
    int MemoryRisk,
    int OperationalRisk,
    string OperationalRiskLevel,
    IReadOnlyList<DiagnosticFinding> Findings,
    IReadOnlyList<string> Justifications)
{
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# RuntimeSentinel - Operational Risk Report");
        sb.AppendLine();
        sb.AppendLine("## Scores");
        sb.AppendLine();
        sb.AppendLine("| Scope | Score (0-100) |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Concurrency | {ConcurrencyRisk} |");
        sb.AppendLine($"| Async | {AsyncRisk} |");
        sb.AppendLine($"| Memory | {MemoryRisk} |");
        sb.AppendLine($"| Operational Risk | {OperationalRisk} |");
        sb.AppendLine($"| Risk Level | {OperationalRiskLevel} |");
        sb.AppendLine();

        sb.AppendLine("## Justifications");
        foreach (var justification in Justifications)
        {
            sb.AppendLine($"- {justification}");
        }

        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();

        if (Findings.Count == 0)
        {
            sb.AppendLine("No diagnostics found.");
            return sb.ToString();
        }

        sb.AppendLine("| Id | Category | Severity | Location |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var finding in Findings)
        {
            sb.AppendLine($"| {finding.Id} | {finding.Category} | {finding.Severity} | {finding.Location} |");
        }

        return sb.ToString();
    }
}
