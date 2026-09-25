using ConsentSync.Data;
using ConsentSync.Data.Entities;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Orchestrator.Tests;

public sealed class ConsentSyncDataTests : IDisposable
{
    private readonly string _tempDirectory;
    private const string DbFileName = "test_phis_clients.db";

    public ConsentSyncDataTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ConsentSyncDataTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task InitializeAsync_CreatesExpectedTables()
    {
        var manager = CreateManager();

        await manager.InitializeAsync();

        await using var connection = OpenConnection();
        string[] tables = (await connection.QueryAsync<string>(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('PhisClientCache', 'CohortContexts', 'ClientListHistory', 'LocationLookups', 'PrefixLookups', 'Jurisdictions', 'ImmunizationRules')
            ORDER BY name;
            """)).ToArray();

        Assert.Equal(["ClientListHistory", "CohortContexts", "ImmunizationRules", "Jurisdictions", "LocationLookups", "PhisClientCache", "PrefixLookups"], tables);

        string[] cohortColumns = (await connection.QueryAsync<string>(
            """
            SELECT name
            FROM pragma_table_info('CohortContexts')
            WHERE name IN ('ClientListName', 'CohortDate', 'CustomListName')
            ORDER BY name;
            """)).ToArray();

        Assert.Equal(["ClientListName", "CohortDate"], cohortColumns);

        string[] indexes = (await connection.QueryAsync<string>(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'index'
              AND name IN ('IX_CohortContexts_CohortDate', 'UX_CohortContexts_BusinessKey', 'UX_CohortContexts_ClientListName')
            ORDER BY name;
            """)).ToArray();

        Assert.Equal(["IX_CohortContexts_CohortDate", "UX_CohortContexts_BusinessKey", "UX_CohortContexts_ClientListName"], indexes);
    }

    [Fact]
    public async Task InitializeAsync_CreatesAndSeedsPrefixLookups()
    {
        var manager = CreateManager();

        await manager.InitializeAsync();

        await using var connection = OpenConnection();
        string[] tables = (await connection.QueryAsync<string>(
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name = 'PrefixLookups';
            """)).ToArray();
        string[] prefixes = (await connection.QueryAsync<string>(
            """
            SELECT Name
            FROM PrefixLookups
            WHERE IsActive = 1
            ORDER BY Name COLLATE NOCASE ASC;
            """)).ToArray();

        Assert.Equal(["PrefixLookups"], tables);
        Assert.Equal(["CIP", "ETS"], prefixes);
    }

    [Fact]
    public async Task InitializeAsync_CreatesAndSeedsJurisdictionsAndImmunizationRules()
    {
        await CreateManager().InitializeAsync();
        await CreateManager().InitializeAsync();

        await using var connection = OpenConnection();
        int nbId = await connection.ExecuteScalarAsync<int>("SELECT JurisdictionId FROM Jurisdictions WHERE Code = 'NB';");
        Assert.Equal("New Brunswick", await connection.ExecuteScalarAsync<string>("SELECT Name FROM Jurisdictions WHERE JurisdictionId = @nbId;", new { nbId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Jurisdictions WHERE Code = 'NB';"));

        await connection.ExecuteAsync("INSERT INTO ImmunizationRules (JurisdictionId, CatalogItemPattern, RuleDescription) VALUES (@nbId, '4 Month%', 'Four month rule');", new { nbId });
        await Assert.ThrowsAsync<SqliteException>(() => connection.ExecuteAsync("INSERT INTO ImmunizationRules (JurisdictionId, CatalogItemPattern, RuleDescription) VALUES (@nbId, '4 Month%', 'Duplicate');", new { nbId }));

        await connection.ExecuteAsync("DELETE FROM Jurisdictions WHERE JurisdictionId = @nbId;", new { nbId });
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ImmunizationRules;"));
    }

    [Fact]
    public async Task GetPrefixesAsync_ReturnsSeededActivePrefixesAlphabetically()
    {
        var manager = CreateManager();

        IReadOnlyList<string> prefixes = await manager.GetPrefixesAsync();

        Assert.Equal(["CIP", "ETS"], prefixes);
    }

    [Fact]
    public async Task GetLocationsAsync_ReturnsSeededActiveLocationsAlphabetically()
    {
        var manager = CreateManager();

        IReadOnlyList<string> locations = await manager.GetLocationsAsync();

        Assert.Equal(["MONCTON", "RICHIBUCTO", "SHEDIAC"], locations);
    }

    [Fact]
    public async Task ClientListHistory_EnforcesForeignKey()
    {
        var manager = CreateManager();
        await manager.InitializeAsync();

        await Assert.ThrowsAsync<SqliteException>(() =>
            manager.LogClientListHistoryAsync(999, "CIPMONCTONSP20260906", 12, "tester"));
    }

    [Fact]
    public async Task ClientCache_LooksUpByCacheKeyAndEmail()
    {
        var manager = CreateManager();
        await manager.SaveClientIdAsync(new PhisClientCacheEntity
        {
            CacheKey = "doe jane_2015-01-02",
            ClientId = "1512481",
            FullName = "Jane Doe",
            DateOfBirth = "2015-01-02",
            Email = "Jane.Doe@example.ca",
            Source = ClientSource.PhisSearch
        });

        Assert.Equal("1512481", await manager.GetClientIdAsync("DOE JANE_2015-01-02"));
        Assert.Equal("1512481", await manager.GetClientIdAsync("", "jane.doe@example.ca"));

        var reloaded = CreateManager();
        await reloaded.InitializeAsync();

        Assert.Equal("1512481", await reloaded.GetClientIdAsync("DOE JANE_2015-01-02"));
        Assert.Equal("1512481", await reloaded.GetClientIdAsync("", "JANE.DOE@EXAMPLE.CA"));
    }

    [Fact]
    public async Task PreloadCacheForDatesAsync_LoadsOnlyNormalizedRosterDatesAndReplacesPreviousSubset()
    {
        var manager = CreateManager();
        await manager.BulkSaveClientCacheAsync([
            Client("ONE", "2020/02/26", "1"), Client("TWO", "2017-12-12", "2"), Client("THREE", "2015/01/01", "3")
        ]);

        await manager.PreloadCacheForDatesAsync(["2020-02-26", "12/12/2017", "not-a-date"]);
        var cache = (Dictionary<string, PhisClientCacheEntity>)typeof(DbManager)
            .GetField("_primaryMemoryCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(manager)!;
        Assert.Equal(2, cache.Count);
        Assert.DoesNotContain(cache.Values, row => row.ClientId == "3");

        await manager.PreloadCacheForDatesAsync(["2015/01/01"]);
        Assert.Single(cache);
        Assert.Equal("3", Assert.Single(cache.Values).ClientId);

        await using var connection = OpenConnection();
        Assert.Contains("IX_PhisClientCache_DateOfBirth", await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_PhisClientCache_DateOfBirth';"));
    }

    [Fact]
    public async Task BulkSaveClientCacheAsync_UpsertsAndNormalizesDates()
    {
        var manager = CreateManager();
        await manager.BulkSaveClientCacheAsync([Client("SAME", "2020-02-26", "001")]);
        await manager.BulkSaveClientCacheAsync([Client("SAME", "26/02/2020", "002")]);
        await using var connection = OpenConnection();
        var row = await connection.QuerySingleAsync<PhisClientCacheEntity>("SELECT * FROM PhisClientCache;");
        Assert.Equal("002", row.ClientId);
        Assert.Equal("2020/02/26", row.DateOfBirth);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PhisClientCache;"));
    }

    [Fact]
    public async Task ClientCache_MedicarePreservesExistingValueUnlessExplicitlyCleared()
    {
        var manager = CreateManager();
        var client = Client("SAME", "2020/02/26", "001");
        client.Medicare = "001234567";
        await manager.BulkSaveClientCacheAsync([client]);

        await manager.BulkSaveClientCacheAsync([Client("SAME", "2020/02/26", "002")]);
        await using var connection = OpenConnection();
        Assert.Equal("001234567", await connection.ExecuteScalarAsync<string>("SELECT Medicare FROM PhisClientCache;"));

        var clear = Client("SAME", "2020/02/26", "003");
        clear.Medicare = string.Empty;
        await manager.BulkSaveClientCacheAsync([clear]);
        Assert.Equal(string.Empty, await connection.ExecuteScalarAsync<string>("SELECT Medicare FROM PhisClientCache;"));
    }

    [Fact]
    public async Task InitializeAsync_AddsMedicareToExistingCacheWithoutLosingRows()
    {
        await using (var connection = OpenConnection())
        {
            await connection.ExecuteAsync("""
                CREATE TABLE PhisClientCache (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, CacheKey TEXT NOT NULL UNIQUE, ClientId TEXT NOT NULL,
                    FullName TEXT NOT NULL, DateOfBirth TEXT NOT NULL, Email TEXT NULL, Source INTEGER NOT NULL, UpdatedOn TEXT NOT NULL);
                INSERT INTO PhisClientCache (CacheKey, ClientId, FullName, DateOfBirth, Source, UpdatedOn)
                VALUES ('PERSON_2020/02/26', '001', 'Person', '2020/02/26', 0, '2026-01-01');
                """);
        }

        await CreateManager().InitializeAsync();

        await using var upgraded = OpenConnection();
        Assert.Contains("Medicare", await upgraded.QueryAsync<string>("SELECT name FROM pragma_table_info('PhisClientCache');"));
        Assert.Equal("001", await upgraded.ExecuteScalarAsync<string>("SELECT ClientId FROM PhisClientCache;"));
        Assert.Null(await upgraded.ExecuteScalarAsync<string?>("SELECT Medicare FROM PhisClientCache;"));
    }

    private static PhisClientCacheEntity Client(string name, string dob, string id) => new()
    {
        CacheKey = DbManager.BuildCacheKey(name, dob), ClientId = id, FullName = name, DateOfBirth = dob,
        Source = ClientSource.ManualReview
    };

    [Fact]
    public async Task SaveCohortContextAsync_ActivatesLatestAndDeactivatesPrevious()
    {
        var manager = CreateManager();

        int firstId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260908",
            CohortDate = new DateTime(2026, 9, 8)
        });

        int secondId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "DIEPPE",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "DIEPPE_CUSTOM",
            CohortDate = new DateTime(2026, 9, 15)
        });

        CohortContextEntity? active = await manager.GetActiveCohortContextAsync();
        IReadOnlyList<CohortContextEntity> recent = await manager.GetRecentCohortContextsAsync();

        Assert.Equal(secondId, active?.CohortContextId);
        Assert.Equal(new DateTime(2026, 9, 15), active?.CohortDate.Date);
        Assert.Equal("DIEPPE_CUSTOM", active?.ClientListName);
        Assert.Contains(recent, context => context.CohortContextId == firstId && !context.IsActive);
        Assert.Contains(recent, context => context.CohortContextId == secondId && context.IsActive);
    }

    [Fact]
    public async Task RecentContextsAndHistory_AreReturnedNewestFirst()
    {
        var manager = CreateManager();
        int firstId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "FIRST",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "FIRST_LIST",
            CohortDate = new DateTime(2026, 9, 8),
            CreatedOn = DateTime.UtcNow.AddMinutes(-10)
        });

        int secondId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "SECOND",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "SECOND_LIST",
            CohortDate = new DateTime(2026, 9, 15),
            CreatedOn = DateTime.UtcNow
        });

        await manager.LogClientListHistoryAsync(secondId, "SECOND", 2, "tester");
        await Task.Delay(10);
        await manager.LogClientListHistoryAsync(secondId, "SECOND", 3, "tester");

        IReadOnlyList<CohortContextEntity> recent = await manager.GetRecentCohortContextsAsync(2);
        IReadOnlyList<ClientListHistoryEntity> history = await manager.GetClientListHistoryAsync(secondId);

        Assert.Equal([secondId, firstId], recent.Select(context => context.CohortContextId).ToArray());
        Assert.Equal(new DateTime(2026, 9, 15), recent[0].CohortDate.Date);
        Assert.Equal("SECOND_LIST", recent[0].ClientListName);
        Assert.Equal([3, 2], history.Select(item => item.ClientCount).ToArray());
    }

    [Fact]
    public async Task PhisUploadSnapshot_RoundTripsOnlyForMatchingDestination()
    {
        var manager = CreateManager();
        var context = new CohortContextEntity { Prefix = "CIP", Location = "MONCTON", Type = "SP", Jurisdiction = "Jurisdiction", EncounterGroup = "Immunization", ClientListName = "LIST", CohortDate = new DateTime(2026, 9, 17), PhisCohortId = 10, PhisClientListId = 20 };
        await manager.SaveCohortContextAsync(context);

        await manager.SaveSuccessfulPhisUploadAsync(context, "[{\"ClientId\":\"100\",\"FullName\":\"One\"}]");

        PhisUploadSnapshotEntity? found = await manager.GetLatestPhisUploadSnapshotAsync(context.CohortContextId, 10, 20, "LIST");
        PhisUploadSnapshotEntity? otherList = await manager.GetLatestPhisUploadSnapshotAsync(context.CohortContextId, 10, 21, "LIST");
        Assert.NotNull(found);
        Assert.Contains("100", found!.ClientSnapshotJson);
        Assert.Null(otherList);
    }

    [Fact]
    public async Task SaveCohortContextAsync_DerivesClientListNameWhenBlank()
    {
        var manager = CreateManager();

        int id = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            CohortDate = new DateTime(2026, 9, 15)
        });

        CohortContextEntity? active = await manager.GetActiveCohortContextAsync();

        Assert.Equal(id, active?.CohortContextId);
        Assert.Equal("CIPMONCTONSP20260915", active?.ClientListName);
        Assert.Equal(active?.ClientListName, active?.ResolvedListName);
    }

    [Fact]
    public async Task SaveCohortContextAsync_RejectsDuplicateBusinessKey()
    {
        var manager = CreateManager();

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260915",
            CohortDate = new DateTime(2026, 9, 15)
        });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SaveCohortContextAsync(new CohortContextEntity
            {
                Prefix = "CIP",
                Location = "MONCTON",
                Type = "SP",
                Jurisdiction = "Jurisdiction",
                EncounterGroup = "Immunization",
                ClientListName = "MANUAL_MONCTON_LIST",
                CohortDate = new DateTime(2026, 9, 15)
            }));

        Assert.Contains("MANUAL_MONCTON_LIST", exception.Message);
        Assert.IsType<SqliteException>(exception.InnerException);
    }

    [Fact]
    public async Task SaveCohortContextAsync_RejectsDuplicateClientListName()
    {
        var manager = CreateManager();

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "SHARED_LIST",
            CohortDate = new DateTime(2026, 9, 15)
        });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SaveCohortContextAsync(new CohortContextEntity
            {
                Prefix = "ETS",
                Location = "SHEDIAC",
                Type = "SP",
                Jurisdiction = "Jurisdiction",
                EncounterGroup = "Immunization",
                ClientListName = "SHARED_LIST",
                CohortDate = new DateTime(2026, 9, 22)
            }));

        Assert.Contains("SHARED_LIST", exception.Message);
        Assert.IsType<SqliteException>(exception.InnerException);
    }

    [Fact]
    public async Task GetCohortContextByBusinessKeyAsync_FindsInactiveContextUsingNormalizedValues()
    {
        var manager = CreateManager();
        int id = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260915",
            CohortDate = new DateTime(2026, 9, 15),
            PhisCohortId = 123,
            PhisClientListId = 456,
            CreatedOn = new DateTime(2026, 9, 1)
        });

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "ETS",
            Location = "SHEDIAC",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "ETSSHEDIACSP20260916",
            CohortDate = new DateTime(2026, 9, 16)
        });

        CohortContextEntity? found = await manager.GetCohortContextByBusinessKeyAsync(
            " cip ", " moncton ", " sp ", new DateTime(2026, 9, 15));

        Assert.Equal(id, found?.CohortContextId);
        Assert.False(found?.IsActive);
        Assert.Equal(123, found?.PhisCohortId);
        Assert.Equal(456, found?.PhisClientListId);
        Assert.Equal(new DateTime(2026, 9, 1), found?.CreatedOn);
    }

    [Fact]
    public async Task SaveCohortContextAsync_UpdatesBusinessKeyMatchWithUniqueNameAndRetainsPhisFields()
    {
        var manager = CreateManager();
        int originalId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Original jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260915",
            CohortDate = new DateTime(2026, 9, 15),
            PhisCohortId = 123,
            PhisClientListId = 456,
            CreatedOn = new DateTime(2026, 9, 1)
        });

        CohortContextEntity match = (await manager.GetCohortContextByBusinessKeyAsync(
            "CIP", "MONCTON", "SP", new DateTime(2026, 9, 15)))!;
        var renamed = new CohortContextEntity
        {
            CohortContextId = match.CohortContextId,
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Updated jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260915_UPDATED",
            CohortDate = new DateTime(2026, 9, 15),
            PhisCohortId = match.PhisCohortId,
            PhisClientListId = match.PhisClientListId,
            CreatedOn = match.CreatedOn
        };

        int updatedId = await manager.SaveCohortContextAsync(renamed);
        CohortContextEntity? saved = await manager.GetCohortContextByListNameAsync("cipmonctonsp20260915_updated");

        Assert.Equal(originalId, updatedId);
        Assert.Equal(originalId, saved?.CohortContextId);
        Assert.Equal(123, saved?.PhisCohortId);
        Assert.Equal(456, saved?.PhisClientListId);
        Assert.Equal(new DateTime(2026, 9, 1), saved?.CreatedOn);
        Assert.Equal("Updated jurisdiction", saved?.Jurisdiction);
        Assert.Single(await manager.GetRecentCohortContextsAsync());
    }

    [Fact]
    public async Task SaveCohortContextAsync_AllowsUpdatingSameContext()
    {
        var manager = CreateManager();

        int id = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260915",
            CohortDate = new DateTime(2026, 9, 15)
        });

        int updatedId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            CohortContextId = id,
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Updated Jurisdiction",
            EncounterGroup = "Updated Immunization",
            ClientListName = "CIPMONCTONSP20260915",
            CohortDate = new DateTime(2026, 9, 15)
        });

        CohortContextEntity? active = await manager.GetActiveCohortContextAsync();

        Assert.Equal(id, updatedId);
        Assert.Equal("Updated Jurisdiction", active?.Jurisdiction);
        Assert.Equal("Updated Immunization", active?.EncounterGroup);
    }

    [Fact]
    public async Task SaveCohortContextAsync_WhenInsertedAsNewRunPreservesPreviousListName()
    {
        var manager = CreateManager();

        int firstId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260908",
            CohortDate = new DateTime(2026, 9, 8)
        });

        int secondId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CIPMONCTONSP20260910",
            CohortDate = new DateTime(2026, 9, 10)
        });

        CohortContextEntity? first = await manager.GetCohortContextByListNameAsync("CIPMONCTONSP20260908");
        CohortContextEntity? second = await manager.GetCohortContextByListNameAsync("CIPMONCTONSP20260910");
        CohortContextEntity? active = await manager.GetActiveCohortContextAsync();

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(firstId, first?.CohortContextId);
        Assert.Equal(secondId, second?.CohortContextId);
        Assert.Equal(new DateTime(2026, 9, 8), first?.CohortDate.Date);
        Assert.Equal(new DateTime(2026, 9, 10), second?.CohortDate.Date);
        Assert.Equal(secondId, active?.CohortContextId);
    }

    [Fact]
    public async Task GetCohortContextByListNameAsync_ReturnsMatchingContextCaseInsensitively()
    {
        var manager = CreateManager();
        int id = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "ETS",
            Location = "SHEDIAC",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "ETSSHEDIACSP20261001",
            CohortDate = new DateTime(2026, 10, 1)
        });

        CohortContextEntity? context = await manager.GetCohortContextByListNameAsync(" etsshediacsp20261001 ");

        Assert.Equal(id, context?.CohortContextId);
        Assert.Equal("ETS", context?.Prefix);
        Assert.Equal("SHEDIAC", context?.Location);
        Assert.Equal(new DateTime(2026, 10, 1), context?.CohortDate.Date);
    }

    [Fact]
    public async Task GetCohortContextByListNameAsync_ReturnsNullWhenMissing()
    {
        var manager = CreateManager();

        CohortContextEntity? context = await manager.GetCohortContextByListNameAsync("UNKNOWN_LIST");

        Assert.Null(context);
    }

    [Fact]
    public async Task GetRecentClientListNamesAsync_ReturnsDistinctNewestFirst()
    {
        var manager = CreateManager();

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "FIRST_LIST",
            CohortDate = new DateTime(2026, 10, 1),
            CreatedOn = DateTime.UtcNow.AddMinutes(-20)
        });

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "ETS",
            Location = "SHEDIAC",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "SECOND_LIST",
            CohortDate = new DateTime(2026, 10, 8),
            CreatedOn = DateTime.UtcNow.AddMinutes(-10)
        });

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "RICHIBUCTO",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "THIRD_LIST",
            CohortDate = new DateTime(2026, 10, 15),
            CreatedOn = DateTime.UtcNow
        });

        IReadOnlyList<string> clientListNames = await manager.GetRecentClientListNamesAsync(2);

        Assert.Equal(["THIRD_LIST", "SECOND_LIST"], clientListNames);
    }

    [Fact]
    public async Task GetRecentSavedListsAsync_ReturnsActiveContextOutsideDateWindow()
    {
        var manager = CreateManager();
        int id = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "ACTIVE_OLD_LIST",
            CohortDate = DateTime.Today.AddDays(-90)
        });

        IReadOnlyList<CohortContextEntity> savedLists = await manager.GetRecentSavedListsAsync();

        Assert.Contains(savedLists, context => context.CohortContextId == id);
    }

    [Fact]
    public async Task PhisIdentifiersAndManualName_RoundTripOnSameContext()
    {
        var manager = CreateManager();
        var context = new CohortContextEntity
        {
            ClientListName = "ORIGINAL_LIST", Prefix = "CIP", Location = "MONCTON", Type = "SP",
            Jurisdiction = "Jurisdiction", EncounterGroup = "Immunization", CohortDate = DateTime.Today
        };
        int id = await manager.SaveCohortContextAsync(context);
        context.PhisCohortId = 24260;
        await manager.SaveCohortContextAsync(context);
        context.PhisClientListId = 24189;
        context.ClientListName = "UPDATED_LIST";
        await manager.SaveCohortContextAsync(context);
        var loaded = await manager.GetCohortContextByListNameAsync("UPDATED_LIST");
        Assert.NotNull(loaded);
        Assert.Equal(id, loaded.CohortContextId);
        Assert.Equal(24260, loaded.PhisCohortId);
        Assert.Equal(24189, loaded.PhisClientListId);
        Assert.Null(await manager.GetCohortContextByListNameAsync("ORIGINAL_LIST"));
    }

    [Fact]
    public async Task GetRecentSavedListsAsync_ReturnsInactiveContextInsideDateWindow()
    {
        var manager = CreateManager();
        int id = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "RECENT_INACTIVE_LIST",
            CohortDate = DateTime.Today.AddDays(-10)
        });

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "ETS",
            Location = "SHEDIAC",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "ACTIVE_NEW_LIST",
            CohortDate = DateTime.Today
        });

        IReadOnlyList<CohortContextEntity> savedLists = await manager.GetRecentSavedListsAsync();

        Assert.Contains(savedLists, context => context.CohortContextId == id && !context.IsActive);
    }

    [Fact]
    public async Task GetRecentSavedListsAsync_ExcludesInactiveContextOutsideDateWindow()
    {
        var manager = CreateManager();
        int oldId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "OLD_INACTIVE_LIST",
            CohortDate = DateTime.Today.AddDays(-45)
        });

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "ETS",
            Location = "SHEDIAC",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CURRENT_ACTIVE_LIST",
            CohortDate = DateTime.Today
        });

        IReadOnlyList<CohortContextEntity> savedLists = await manager.GetRecentSavedListsAsync();

        Assert.DoesNotContain(savedLists, context => context.CohortContextId == oldId);
    }

    [Fact]
    public async Task GetRecentSavedListsAsync_RespectsMaxLimit()
    {
        var manager = CreateManager();

        for (int i = 0; i < 25; i++)
        {
            await manager.SaveCohortContextAsync(new CohortContextEntity
            {
                Prefix = i % 2 == 0 ? "CIP" : "ETS",
                Location = $"LIMIT{i:D2}",
                Type = "SP",
                Jurisdiction = "Jurisdiction",
                EncounterGroup = "Immunization",
                ClientListName = $"LIMIT_LIST_{i:D2}",
                CohortDate = DateTime.Today.AddDays(-(i % 5))
            });
        }

        IReadOnlyList<CohortContextEntity> savedLists = await manager.GetRecentSavedListsAsync(30, 20);

        Assert.Equal(20, savedLists.Count);
        Assert.Equal("LIMIT_LIST_24", savedLists[0].ClientListName);
    }

    [Fact]
    public async Task GetCohortContextByListNameAsync_FindsOlderInactiveArchivedContext()
    {
        var manager = CreateManager();
        int archivedId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "ARCHIVED_LIST",
            CohortDate = DateTime.Today.AddDays(-60)
        });

        await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "ETS",
            Location = "SHEDIAC",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "CURRENT_LIST",
            CohortDate = DateTime.Today
        });

        CohortContextEntity? archived = await manager.GetCohortContextByListNameAsync("archived_list");

        Assert.Equal(archivedId, archived?.CohortContextId);
        Assert.False(archived?.IsActive);
    }

    [Fact]
    public async Task SetActiveCohortContextAsync_ActivatesSelectedContextAndDeactivatesOthers()
    {
        var manager = CreateManager();
        int firstId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "FIRST_LIST",
            CohortDate = new DateTime(2026, 10, 1)
        });

        int secondId = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "ETS",
            Location = "SHEDIAC",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "SECOND_LIST",
            CohortDate = new DateTime(2026, 10, 8)
        });

        bool activated = await manager.SetActiveCohortContextAsync(firstId);
        CohortContextEntity? active = await manager.GetActiveCohortContextAsync();
        IReadOnlyList<CohortContextEntity> recent = await manager.GetRecentCohortContextsAsync();

        Assert.True(activated);
        Assert.Equal(firstId, active?.CohortContextId);
        Assert.Contains(recent, context => context.CohortContextId == firstId && context.IsActive);
        Assert.Contains(recent, context => context.CohortContextId == secondId && !context.IsActive);
    }

    [Fact]
    public async Task SetActiveCohortContextAsync_ReturnsFalseForMissingContext()
    {
        var manager = CreateManager();
        int id = await manager.SaveCohortContextAsync(new CohortContextEntity
        {
            Prefix = "CIP",
            Location = "MONCTON",
            Type = "SP",
            Jurisdiction = "Jurisdiction",
            EncounterGroup = "Immunization",
            ClientListName = "ACTIVE_LIST",
            CohortDate = new DateTime(2026, 10, 1)
        });

        bool activated = await manager.SetActiveCohortContextAsync(999);
        CohortContextEntity? active = await manager.GetActiveCohortContextAsync();

        Assert.False(activated);
        Assert.Equal(id, active?.CohortContextId);
    }

    [Fact]
    public async Task ConfigurationConstructor_UsesDatabaseFolderUnderBaseDirectory()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseDirectory"] = _tempDirectory,
                ["Database:Provider"] = "SQLite",
                ["Database:FileName"] = "phis_clients.db",
                ["Database:ConnectionString"] = "Data Source={BaseDirectory}\\Database\\{FileName};"
            })
            .Build();

        var manager = new DbManager(configuration);

        await manager.InitializeAsync();

        Assert.True(File.Exists(Path.Combine(_tempDirectory, "Database", "phis_clients.db")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            SqliteConnection.ClearAllPools();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    private DbManager CreateManager() => new(_tempDirectory, DbFileName);

    private SqliteConnection OpenConnection()
    {
        string dbPath = Path.Combine(_tempDirectory, DbFileName);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON;");
        return connection;
    }
}
