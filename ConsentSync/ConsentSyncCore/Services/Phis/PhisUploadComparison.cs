namespace ConsentSyncCore.Services.Phis;

public sealed record PhisUploadClient(string ClientId, string FullName);
public sealed record PhisUploadComparison(bool IsInitialUpload, IReadOnlyList<PhisUploadClient> Added, IReadOnlyList<PhisUploadClient> Removed)
{
    public bool HasMembershipChanges => Added.Count > 0 || Removed.Count > 0;
}

public static class PhisUploadComparer
{
    public static PhisUploadComparison Compare(IEnumerable<PhisUploadClient> current, IEnumerable<PhisUploadClient>? previous)
    {
        var currentById = Normalize(current);
        if (previous is null) return new(true, currentById.Values.OrderBy(x => x.ClientId, StringComparer.Ordinal).ToList(), []);
        var previousById = Normalize(previous);
        return new(false,
            currentById.Where(x => !previousById.ContainsKey(x.Key)).Select(x => x.Value).OrderBy(x => x.ClientId, StringComparer.Ordinal).ToList(),
            previousById.Where(x => !currentById.ContainsKey(x.Key)).Select(x => x.Value).OrderBy(x => x.ClientId, StringComparer.Ordinal).ToList());
    }

    private static Dictionary<string, PhisUploadClient> Normalize(IEnumerable<PhisUploadClient> clients) => clients
        .Where(x => !string.IsNullOrWhiteSpace(x.ClientId))
        .GroupBy(x => x.ClientId.Trim(), StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => new PhisUploadClient(x.Key, x.Select(y => y.FullName?.Trim()).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)) ?? string.Empty), StringComparer.Ordinal);
}
