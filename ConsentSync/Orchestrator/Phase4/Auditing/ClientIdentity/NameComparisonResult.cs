namespace Orchestrator.Phase4.Auditing.ClientIdentity;

public enum NameComparisonResult
{
    NotChecked,
    Exact,
    Compatible,
    TokenOrderEquivalent,
    UniquePartial,
    ReversedColumns,
    Ambiguous,
    Mismatch
}
