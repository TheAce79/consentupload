using ConsentSync.Data.Entities;

namespace ConsentSync.Data;

public interface IConsentSyncRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<string?> GetClientIdAsync(
        string cacheKey,
        string? email = null,
        CancellationToken cancellationToken = default);

    Task<int> SaveClientIdAsync(
        PhisClientCacheEntity client,
        CancellationToken cancellationToken = default);

    Task<CohortContextEntity?> GetActiveCohortContextAsync(
        CancellationToken cancellationToken = default);

    Task<CohortContextEntity?> GetCohortContextByListNameAsync(
        string clientListName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRecentClientListNamesAsync(
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CohortContextEntity>> GetRecentSavedListsAsync(
        int daysBack = 30,
        int maxLimit = 20,
        CancellationToken cancellationToken = default);

    Task<bool> SetActiveCohortContextAsync(
        int cohortContextId,
        CancellationToken cancellationToken = default);

    Task<int> SaveCohortContextAsync(
        CohortContextEntity context,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CohortContextEntity>> GetRecentCohortContextsAsync(
        int take = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetLocationsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetPrefixesAsync(
        CancellationToken cancellationToken = default);

    Task<int> LogClientListHistoryAsync(
        int cohortContextId,
        string resolvedListName,
        int clientCount,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientListHistoryEntity>> GetClientListHistoryAsync(
        int cohortContextId,
        CancellationToken cancellationToken = default);
}
