using ConsentSyncCore.Services.Phis;

namespace Orchestrator.Phase4.Auditing.PhisDocumentPresence;

public sealed class PhisDocumentPresenceGateway : IPhisDocumentPresenceGateway
{
    private readonly PhisSearchService _searchService;
    private readonly PhisSessionManager _sessionManager;

    public PhisDocumentPresenceGateway(PhisSearchService searchService, PhisSessionManager sessionManager)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public bool EnsureSessionValid() => _sessionManager.EnsureSessionValid();
    public Task<bool> SetClientContextAsync(string clientId) => _searchService.SearchByClientIdAndSetInContextAsync(clientId);

    public async Task<bool> OpenConsentDocumentListAsync(string phisAntigen)
    {
        bool navigated = await _searchService.NavigateToImmunizationServiceAsync();
        if (!navigated) navigated = await _searchService.NavigateToImmunizationServiceViaMenuAsync();
        return navigated && await _searchService.SelectConsentDirectiveByAntigenAsync(phisAntigen) && await _searchService.ClickDocumentsButtonAsync();
    }

    public Task<bool> OpenFileRoseDocumentListAsync() => _searchService.NavigateToContextDocumentsAsync();
    public Task<PhisDocumentLookupResult> FindConsentDocumentAsync(string documentTitle) => _searchService.CheckIfDocumentExistsDetailedAsync(documentTitle);
    public Task<PhisDocumentLookupResult> FindFileRoseDocumentAsync(string documentTitle) => _searchService.CheckIfContextDocumentExistsDetailedAsync(documentTitle);
    public Task<bool> ReturnToSearchAsync() => _searchService.NavigateBackToSearchPagesAsync();
}
