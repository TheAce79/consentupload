namespace Orchestrator.Phase4.Auditing.ClientIdentity;

internal enum DeterministicNameCandidateKind
{
    Exact,
    Compatible,
    TokenOrderEquivalent,
    Partial,
    ReversedColumns,
    Mismatch
}
