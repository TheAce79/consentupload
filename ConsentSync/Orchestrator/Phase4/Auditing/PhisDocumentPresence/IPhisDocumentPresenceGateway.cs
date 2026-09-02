using ConsentSyncCore.Services.Phis;

namespace Orchestrator.Phase4.Auditing.PhisDocumentPresence;

public interface IPhisDocumentPresenceGateway
{
    bool EnsureSessionValid();
    Task<bool> SetClientContextAsync(string clientId);
    Task<bool> OpenConsentDocumentListAsync(string phisAntigen);
    Task<bool> OpenFileRoseDocumentListAsync();
    Task<PhisDocumentLookupResult> FindConsentDocumentAsync(string documentTitle);
    Task<PhisDocumentLookupResult> FindFileRoseDocumentAsync(string documentTitle);
    Task<bool> ReturnToSearchAsync();
}
