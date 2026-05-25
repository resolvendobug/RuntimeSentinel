namespace RuntimeSentinel.Scoring;

public sealed record OperationalRiskScoringOptions(
    double ConcurrencyWeight,
    double AsyncWeight,
    double MemoryWeight,
    int LowThreshold,
    int MediumThreshold)
{
    public static OperationalRiskScoringOptions Default { get; } = new(
        ConcurrencyWeight: 0.5,
        AsyncWeight: 0.3,
        MemoryWeight: 0.2,
        LowThreshold: 30,
        MediumThreshold: 70);
}
