using ConsentSyncCore.Models;

namespace ConsentSyncCore.Services.Phis;

public interface IPhisClientSearch
{
    Task<SearchResult> SearchByDobAsync(string dateOfBirth, string? expectedFirstName = null, string? expectedLastName = null, string? expectedMedicare = null);
    Task<SearchResult> SearchByMedicareAsync(string medicareNumber);
}
