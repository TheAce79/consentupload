using System.Data;
using ConsentSync.Data.Entities;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace ConsentSync.Data;

public sealed class DbManager : IConsentSyncRepository
{
    private readonly string _provider;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly Dictionary<string, PhisClientCacheEntity> _primaryMemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PhisClientCacheEntity> _emailMemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public DbManager(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _provider = configuration["Database:Provider"] ?? "SQLite";
        _connectionString = BuildConnectionString(configuration);
    }

    public DbManager(string baseDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Base directory is required.", nameof(baseDirectory));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Database file name is required.", nameof(fileName));
        }

        _provider = "SQLite";
        string dbPath = Path.IsPathRooted(fileName) ? fileName : Path.Combine(baseDirectory, fileName);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            EnsureSupportedProvider();
            EnsureDatabaseDirectoryExists();

            await using SqliteConnection connection = OpenSqliteConnection();
            await CreateSchemaAsync(connection);
            await LoadMemoryCachesAsync(connection);

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<string?> GetClientIdAsync(
        string cacheKey,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        string normalizedKey = NormalizeCacheKey(cacheKey);
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            _primaryMemoryCache.TryGetValue(normalizedKey, out PhisClientCacheEntity? primaryClient))
        {
            return primaryClient.ClientId;
        }

        string normalizedEmail = NormalizeEmail(email);
        if (!string.IsNullOrWhiteSpace(normalizedEmail) &&
            _emailMemoryCache.TryGetValue(normalizedEmail, out PhisClientCacheEntity? emailClient))
        {
            return emailClient.ClientId;
        }

        return null;
    }

    public async Task<int> SaveClientIdAsync(
        PhisClientCacheEntity client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        await EnsureInitializedAsync(cancellationToken);

        string normalizedKey = NormalizeCacheKey(client.CacheKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ArgumentException("CacheKey is required.", nameof(client));
        }

        client.CacheKey = normalizedKey;
        client.Email = string.IsNullOrWhiteSpace(client.Email) ? null : client.Email.Trim();
        client.UpdatedOn = client.UpdatedOn == default ? DateTime.UtcNow : client.UpdatedOn;

        const string sql = """
            INSERT INTO PhisClientCache
                (CacheKey, ClientId, FullName, DateOfBirth, Email, Source, UpdatedOn)
            VALUES
                (@CacheKey, @ClientId, @FullName, @DateOfBirth, @Email, @Source, @UpdatedOn)
            ON CONFLICT(CacheKey) DO UPDATE SET
                ClientId = excluded.ClientId,
                FullName = excluded.FullName,
                DateOfBirth = excluded.DateOfBirth,
                Email = excluded.Email,
                Source = excluded.Source,
                UpdatedOn = excluded.UpdatedOn
            RETURNING Id;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        int id = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, ToClientParameters(client), cancellationToken: cancellationToken));

        client.Id = id;
        UpdateMemoryCache(client);
        return id;
    }

    public async Task<CohortContextEntity?> GetActiveCohortContextAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string sql = """
            SELECT CohortContextId, PhisCohortId, PhisClientListId, Prefix, Location, Type,
                   Jurisdiction, EncounterGroup, ClientListName, CohortDate, IsActive, CreatedOn
            FROM CohortContexts
            WHERE IsActive = 1
            ORDER BY CreatedOn DESC, CohortContextId DESC
            LIMIT 1;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        return await connection.QuerySingleOrDefaultAsync<CohortContextEntity>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task<CohortContextEntity?> GetCohortContextByListNameAsync(
        string clientListName,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        string normalizedListName = (clientListName ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedListName))
        {
            return null;
        }

        const string sql = """
            SELECT CohortContextId, PhisCohortId, PhisClientListId, Prefix, Location, Type,
                   Jurisdiction, EncounterGroup, ClientListName, CohortDate, IsActive, CreatedOn
            FROM CohortContexts
            WHERE ClientListName = @ClientListName
            LIMIT 1;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        return await connection.QuerySingleOrDefaultAsync<CohortContextEntity>(
            new CommandDefinition(sql, new { ClientListName = normalizedListName }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<string>> GetRecentClientListNamesAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        int limit = Math.Clamp(take, 1, 100);
        const string sql = """
            SELECT ClientListName
            FROM CohortContexts
            WHERE ClientListName IS NOT NULL AND TRIM(ClientListName) <> ''
            GROUP BY ClientListName
            ORDER BY MAX(CreatedOn) DESC, MAX(CohortContextId) DESC
            LIMIT @Limit;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        IEnumerable<string> clientListNames = await connection.QueryAsync<string>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken));
        return clientListNames.ToList();
    }

    public async Task<IReadOnlyList<CohortContextEntity>> GetRecentSavedListsAsync(
        int daysBack = 30,
        int maxLimit = 20,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        int days = Math.Clamp(daysBack, 0, 3650);
        int limit = Math.Clamp(maxLimit, 1, 100);
        const string sql = """
            SELECT CohortContextId, PhisCohortId, PhisClientListId, Prefix, Location, Type,
                   Jurisdiction, EncounterGroup, ClientListName, CohortDate, IsActive, CreatedOn
            FROM CohortContexts
            WHERE IsActive = 1
               OR date(CohortDate) >= date('now', '-' || @DaysBack || ' days')
            ORDER BY CohortContextId DESC
            LIMIT @Limit;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        IEnumerable<CohortContextEntity> contexts = await connection.QueryAsync<CohortContextEntity>(
            new CommandDefinition(sql, new { DaysBack = days, Limit = limit }, cancellationToken: cancellationToken));
        return contexts.ToList();
    }

    public async Task<bool> SetActiveCohortContextAsync(
        int cohortContextId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (cohortContextId <= 0)
        {
            return false;
        }

        await using SqliteConnection connection = OpenSqliteConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE CohortContexts SET IsActive = 0 WHERE IsActive = 1;",
                transaction: transaction,
                cancellationToken: cancellationToken));

        int updated = await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE CohortContexts SET IsActive = 1 WHERE CohortContextId = @CohortContextId;",
                new { CohortContextId = cohortContextId },
                transaction,
                cancellationToken: cancellationToken));

        if (updated == 0)
        {
            TryRollback(transaction);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> SaveCohortContextAsync(
        CohortContextEntity context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await EnsureInitializedAsync(cancellationToken);

        context.Prefix = NormalizeRequiredText(context.Prefix, "Prefix");
        context.Location = NormalizeRequiredText(context.Location, "Location");
        context.Type = NormalizeRequiredText(context.Type, "Type");
        context.Jurisdiction = NormalizeRequiredText(context.Jurisdiction, "Jurisdiction");
        context.EncounterGroup = NormalizeRequiredText(context.EncounterGroup, "EncounterGroup");
        context.CohortDate = context.CohortDate == default ? DateTime.Today : context.CohortDate.Date;
        context.ClientListName = string.IsNullOrWhiteSpace(context.ClientListName)
            ? DeriveClientListName(context)
            : context.ClientListName.Trim().ToUpperInvariant();
        context.IsActive = true;
        context.CreatedOn = context.CreatedOn == default ? DateTime.UtcNow : context.CreatedOn;

        await using SqliteConnection connection = OpenSqliteConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE CohortContexts SET IsActive = 0 WHERE IsActive = 1;",
                    transaction: transaction,
                    cancellationToken: cancellationToken));

            int id;
            if (context.CohortContextId > 0)
            {
                const string updateSql = """
                    UPDATE CohortContexts
                    SET PhisCohortId = @PhisCohortId,
                        PhisClientListId = @PhisClientListId,
                        Prefix = @Prefix,
                        Location = @Location,
                        Type = @Type,
                        Jurisdiction = @Jurisdiction,
                        EncounterGroup = @EncounterGroup,
                        ClientListName = @ClientListName,
                        CohortDate = @CohortDate,
                        IsActive = 1,
                        CreatedOn = @CreatedOn
                    WHERE CohortContextId = @CohortContextId;
                    """;

                int updated = await connection.ExecuteAsync(
                    new CommandDefinition(updateSql, context, transaction, cancellationToken: cancellationToken));

                id = updated > 0
                    ? context.CohortContextId
                    : await InsertCohortContextAsync(connection, transaction, context, cancellationToken);
            }
            else
            {
                id = await InsertCohortContextAsync(connection, transaction, context, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            context.CohortContextId = id;
            return id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            TryRollback(transaction);
            throw new InvalidOperationException(
                $"A cohort context for '{context.ClientListName}' already exists.",
                ex);
        }
    }

    public async Task<IReadOnlyList<CohortContextEntity>> GetRecentCohortContextsAsync(
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        int limit = Math.Clamp(take, 1, 100);
        const string sql = """
            SELECT CohortContextId, PhisCohortId, PhisClientListId, Prefix, Location, Type,
                   Jurisdiction, EncounterGroup, ClientListName, CohortDate, IsActive, CreatedOn
            FROM CohortContexts
            ORDER BY CreatedOn DESC, CohortContextId DESC
            LIMIT @Limit;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        IEnumerable<CohortContextEntity> contexts = await connection.QueryAsync<CohortContextEntity>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken));
        return contexts.ToList();
    }

    public async Task<IReadOnlyList<string>> GetLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string sql = """
            SELECT Name
            FROM LocationLookups
            WHERE IsActive = 1
            ORDER BY Name COLLATE NOCASE ASC;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        IEnumerable<string> locations = await connection.QueryAsync<string>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return locations.ToList();
    }

    public async Task<IReadOnlyList<string>> GetPrefixesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string sql = """
            SELECT Name
            FROM PrefixLookups
            WHERE IsActive = 1
            ORDER BY Name COLLATE NOCASE ASC;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        IEnumerable<string> prefixes = await connection.QueryAsync<string>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return prefixes.ToList();
    }

    public async Task<int> LogClientListHistoryAsync(
        int cohortContextId,
        string resolvedListName,
        int clientCount,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (cohortContextId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cohortContextId), "CohortContextId must be greater than zero.");
        }

        if (clientCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientCount), "Client count cannot be negative.");
        }

        string actor = string.IsNullOrWhiteSpace(createdBy) ? Environment.UserName : createdBy.Trim();
        const string sql = """
            INSERT INTO ClientListHistory
                (CohortContextId, ResolvedListName, ClientCount, CreatedBy, CreatedOn)
            VALUES
                (@CohortContextId, @ResolvedListName, @ClientCount, @CreatedBy, @CreatedOn)
            RETURNING Id;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                sql,
                new
                {
                    CohortContextId = cohortContextId,
                    ResolvedListName = NormalizeRequiredText(resolvedListName, nameof(resolvedListName)),
                    ClientCount = clientCount,
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow
                },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ClientListHistoryEntity>> GetClientListHistoryAsync(
        int cohortContextId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string sql = """
            SELECT Id, CohortContextId, ResolvedListName, ClientCount, CreatedBy, CreatedOn
            FROM ClientListHistory
            WHERE CohortContextId = @CohortContextId
            ORDER BY CreatedOn DESC, Id DESC;
            """;

        await using SqliteConnection connection = OpenSqliteConnection();
        IEnumerable<ClientListHistoryEntity> history = await connection.QueryAsync<ClientListHistoryEntity>(
            new CommandDefinition(sql, new { CohortContextId = cohortContextId }, cancellationToken: cancellationToken));
        return history.ToList();
    }

    private static string BuildConnectionString(IConfiguration configuration)
    {
        string baseDirectory = configuration["BaseDirectory"] ?? "C:\\PHIS";
        string fileName = configuration["Database:FileName"] ?? "phis_clients.db";
        string connectionStringTemplate = configuration["Database:ConnectionString"] ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(connectionStringTemplate))
        {
            string expandedConnectionString = connectionStringTemplate
                .Replace("{BaseDirectory}", baseDirectory)
                .Replace("{FileName}", fileName);

            var builder = new SqliteConnectionStringBuilder(expandedConnectionString);
            if (!string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return builder.ToString();
            }
        }

        string dbPath = Path.IsPathRooted(fileName) ? fileName : Path.Combine(baseDirectory, "Database", fileName);
        return new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    private static object ToClientParameters(PhisClientCacheEntity client) => new
    {
        client.CacheKey,
        client.ClientId,
        client.FullName,
        client.DateOfBirth,
        client.Email,
        Source = (int)client.Source,
        client.UpdatedOn
    };

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private void EnsureSupportedProvider()
    {
        if (!string.Equals(_provider, "SQLite", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Database provider '{_provider}' is not supported by this DbManager implementation.");
        }
    }

    private void EnsureDatabaseDirectoryExists()
    {
        string? dataSource = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        string? directory = string.IsNullOrWhiteSpace(dataSource) ? null : Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private SqliteConnection OpenSqliteConnection()
    {
        EnsureSupportedProvider();

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static async Task CreateSchemaAsync(IDbConnection connection)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS PhisClientCache (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CacheKey TEXT NOT NULL UNIQUE,
                ClientId TEXT NOT NULL,
                FullName TEXT NOT NULL,
                DateOfBirth TEXT NOT NULL,
                Email TEXT NULL,
                Source INTEGER NOT NULL,
                UpdatedOn TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_PhisClientCache_Email
                ON PhisClientCache (Email);

            CREATE TABLE IF NOT EXISTS CohortContexts (
                CohortContextId INTEGER PRIMARY KEY AUTOINCREMENT,
                PhisCohortId INTEGER NULL,
                PhisClientListId INTEGER NULL,
                Prefix TEXT NOT NULL DEFAULT 'CIP',
                Location TEXT NOT NULL,
                Type TEXT NOT NULL,
                Jurisdiction TEXT NOT NULL,
                EncounterGroup TEXT NOT NULL,
                ClientListName TEXT NOT NULL,
                CohortDate DATETIME NOT NULL DEFAULT CURRENT_DATE,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedOn DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_CohortContexts_IsActive
                ON CohortContexts (IsActive);

            CREATE INDEX IF NOT EXISTS IX_CohortContexts_CohortDate
                ON CohortContexts (CohortDate);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_CohortContexts_BusinessKey
                ON CohortContexts (Prefix, Location, CohortDate, Type);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_CohortContexts_ClientListName
                ON CohortContexts (ClientListName);

            CREATE TABLE IF NOT EXISTS LocationLookups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            INSERT OR IGNORE INTO LocationLookups (Name)
            VALUES ('MONCTON'), ('SHEDIAC'), ('RICHIBUCTO');

            CREATE TABLE IF NOT EXISTS PrefixLookups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            INSERT OR IGNORE INTO PrefixLookups (Name)
            VALUES ('CIP'), ('ETS');

            CREATE TABLE IF NOT EXISTS ClientListHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CohortContextId INTEGER NOT NULL,
                ResolvedListName TEXT NOT NULL,
                ClientCount INTEGER NOT NULL,
                CreatedBy TEXT NOT NULL,
                CreatedOn TEXT NOT NULL,
                CONSTRAINT FK_ClientListHistory_CohortContexts
                    FOREIGN KEY (CohortContextId)
                    REFERENCES CohortContexts (CohortContextId)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ClientListHistory_CohortContextId
                ON ClientListHistory (CohortContextId);
            """;

        await connection.ExecuteAsync(sql);
    }

    private async Task LoadMemoryCachesAsync(IDbConnection connection)
    {
        const string sql = """
            SELECT Id, CacheKey, ClientId, FullName, DateOfBirth, Email, Source, UpdatedOn
            FROM PhisClientCache
            ORDER BY UpdatedOn ASC, Id ASC;
            """;

        IEnumerable<PhisClientCacheEntity> clients = await connection.QueryAsync<PhisClientCacheEntity>(sql);

        _primaryMemoryCache.Clear();
        _emailMemoryCache.Clear();

        foreach (PhisClientCacheEntity client in clients)
        {
            UpdateMemoryCache(client);
        }
    }

    private static async Task<int> InsertCohortContextAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CohortContextEntity context,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT INTO CohortContexts
                (PhisCohortId, PhisClientListId, Prefix, Location, Type, Jurisdiction,
                 EncounterGroup, ClientListName, CohortDate, IsActive, CreatedOn)
            VALUES
                (@PhisCohortId, @PhisClientListId, @Prefix, @Location, @Type, @Jurisdiction,
                 @EncounterGroup, @ClientListName, @CohortDate, 1, @CreatedOn)
            RETURNING CohortContextId;
            """;

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(insertSql, context, transaction, cancellationToken: cancellationToken));
    }

    private void UpdateMemoryCache(PhisClientCacheEntity client)
    {
        string normalizedKey = NormalizeCacheKey(client.CacheKey);
        if (!string.IsNullOrWhiteSpace(normalizedKey))
        {
            client.CacheKey = normalizedKey;
            _primaryMemoryCache[normalizedKey] = client;
        }

        string normalizedEmail = NormalizeEmail(client.Email);
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            _emailMemoryCache[normalizedEmail] = client;
        }
    }

    private static string NormalizeCacheKey(string? cacheKey) =>
        (cacheKey ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToUpperInvariant();

    private static void TryRollback(IDbTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string DeriveClientListName(CohortContextEntity context) =>
        $"{context.Prefix}{context.Location}{context.Type}{context.CohortDate:yyyyMMdd}".ToUpperInvariant();

    private static string NormalizeRequiredText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Trim();
    }
}
