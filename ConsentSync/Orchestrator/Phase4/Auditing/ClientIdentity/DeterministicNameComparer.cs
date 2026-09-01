using System.Globalization;
using System.Text;

namespace Orchestrator.Phase4.Auditing.ClientIdentity;

public sealed class DeterministicNameComparer
{
    public NameComparisonResult Compare(string uploadFirstName, string uploadLastName, string rosterClientName)
    {
        DeterministicNameCandidateKind candidate = CompareCandidate(uploadFirstName, uploadLastName, rosterClientName);
        return candidate switch
        {
            DeterministicNameCandidateKind.Exact => NameComparisonResult.Exact,
            DeterministicNameCandidateKind.Compatible => NameComparisonResult.Compatible,
            DeterministicNameCandidateKind.TokenOrderEquivalent => NameComparisonResult.TokenOrderEquivalent,
            DeterministicNameCandidateKind.ReversedColumns => NameComparisonResult.ReversedColumns,
            _ => NameComparisonResult.Mismatch
        };
    }

    internal DeterministicNameCandidateKind CompareCandidate(string uploadFirstName, string uploadLastName, string rosterClientName)
    {
        var (rosterLastName, rosterFirstName) = SplitRosterName(rosterClientName);
        return CompareCandidate(uploadFirstName, uploadLastName, rosterFirstName, rosterLastName);
    }

    internal DeterministicNameCandidateKind CompareCandidate(string uploadFirstName, string uploadLastName, string rosterFirstName, string rosterLastName)
    {
        NameComponent uploadFirst = ParseComponent(uploadFirstName);
        NameComponent uploadLast = ParseComponent(uploadLastName);
        NameComponent rosterFirst = ParseComponent(rosterFirstName);
        NameComponent rosterLast = ParseComponent(rosterLastName);

        if (ComponentsEqual(uploadFirst, rosterFirst) && ComponentsEqual(uploadLast, rosterLast))
        {
            return DeterministicNameCandidateKind.Exact;
        }

        if (ComponentsEqual(uploadFirst, rosterLast) && ComponentsEqual(uploadLast, rosterFirst))
        {
            return DeterministicNameCandidateKind.ReversedColumns;
        }

        bool lastNamesEqual = ComponentsEqual(uploadLast, rosterLast);
        if (lastNamesEqual && HaveEqualTokenMultisets(uploadFirst.Tokens, rosterFirst.Tokens))
        {
            return DeterministicNameCandidateKind.TokenOrderEquivalent;
        }

        if (AreComponentsCompatible(uploadFirst, rosterFirst) && AreComponentsCompatible(uploadLast, rosterLast))
        {
            return DeterministicNameCandidateKind.Compatible;
        }

        if (!AreComponentsCompatible(uploadLast, rosterLast) && HasStrongGivenNamePartial(uploadFirst.Tokens, rosterFirst.Tokens))
        {
            return DeterministicNameCandidateKind.Partial;
        }

        return DeterministicNameCandidateKind.Mismatch;
    }

    public string NormalizeFullName(string firstName, string lastName) =>
        $"{ParseComponent(lastName).Normalized}|{ParseComponent(firstName).Normalized}";

    internal static bool IsAutomaticCandidate(DeterministicNameCandidateKind candidate) =>
        candidate is DeterministicNameCandidateKind.Exact or
            DeterministicNameCandidateKind.Compatible or
            DeterministicNameCandidateKind.TokenOrderEquivalent or
            DeterministicNameCandidateKind.Partial;

    private static (string lastName, string firstName) SplitRosterName(string clientName)
    {
        var parts = (clientName ?? string.Empty).Split(',', 2, StringSplitOptions.TrimEntries);
        return (parts.ElementAtOrDefault(0) ?? string.Empty, parts.ElementAtOrDefault(1) ?? string.Empty);
    }

    private static bool ComponentsEqual(NameComponent left, NameComponent right) =>
        left.Tokens.Length > 0 && right.Tokens.Length > 0 && left.Tokens.SequenceEqual(right.Tokens, StringComparer.Ordinal);

    private static bool AreComponentsCompatible(NameComponent left, NameComponent right) =>
        left.Tokens.Length > 0 && right.Tokens.Length > 0 &&
        (IsTokenMultisetSubset(left.Tokens, right.Tokens) || IsTokenMultisetSubset(right.Tokens, left.Tokens));

    private static bool HasStrongGivenNamePartial(IReadOnlyList<string> uploadTokens, IReadOnlyList<string> rosterTokens)
    {
        if (uploadTokens.Count == 0 || rosterTokens.Count == 0)
        {
            return false;
        }

        IReadOnlyList<string> shorter = uploadTokens.Count <= rosterTokens.Count ? uploadTokens : rosterTokens;
        IReadOnlyList<string> longer = uploadTokens.Count <= rosterTokens.Count ? rosterTokens : uploadTokens;
        return shorter.Distinct(StringComparer.Ordinal).Count() >= 2 && IsTokenMultisetSubset(shorter, longer);
    }

    private static bool HaveEqualTokenMultisets(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count > 0 && right.Count > 0 && left.Count == right.Count &&
        IsTokenMultisetSubset(left, right) && IsTokenMultisetSubset(right, left) &&
        !left.SequenceEqual(right, StringComparer.Ordinal);

    private static bool IsTokenMultisetSubset(IReadOnlyList<string> subset, IReadOnlyList<string> superset)
    {
        var availableCounts = superset.GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (string token in subset)
        {
            if (!availableCounts.TryGetValue(token, out int count) || count == 0)
            {
                return false;
            }

            availableCounts[token] = count - 1;
        }

        return true;
    }

    private static NameComponent ParseComponent(string value)
    {
        var decomposed = (value ?? string.Empty).Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
            }
        }

        string[] tokens = builder.ToString().Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new NameComponent(string.Join(' ', tokens), tokens);
    }

    private sealed record NameComponent(string Normalized, string[] Tokens);
}
