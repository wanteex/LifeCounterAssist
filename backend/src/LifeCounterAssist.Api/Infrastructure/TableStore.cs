using Azure;
using Azure.Data.Tables;
using Azure.Identity;

namespace LifeCounterAssist.Api.Infrastructure;

/// <summary>Storage configuration bound from the <c>Storage</c> section.</summary>
public sealed class StorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string Section = "Storage";

    /// <summary>Gets or sets a connection string (local Azurite or account key).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets the table endpoint used with managed identity.</summary>
    public string? TableEndpoint { get; set; }

    /// <summary>Gets or sets the client identifier of a user-assigned managed identity.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Gets or sets a prefix applied to table names.</summary>
    public string TablePrefix { get; set; } = "lca";
}

/// <summary>Logical table names.</summary>
public static class Tables
{
    /// <summary>Profiles and friend codes.</summary>
    public const string Users = "users";

    /// <summary>Friendship edges.</summary>
    public const string Friends = "friends";

    /// <summary>Decks.</summary>
    public const string Decks = "decks";

    /// <summary>Game states and join codes.</summary>
    public const string Games = "games";

    /// <summary>Per-user game history.</summary>
    public const string PlayerGames = "playergames";

    /// <summary>Pending invitations.</summary>
    public const string Invitations = "invitations";

    /// <summary>All tables.</summary>
    public static readonly string[] All = [Users, Friends, Decks, Games, PlayerGames, Invitations];
}

/// <summary>Thin wrapper over <see cref="TableServiceClient"/> with helpers used by repositories.</summary>
public sealed class TableStore
{
    private readonly TableServiceClient _service;
    private readonly string _prefix;

    /// <summary>Initializes a new instance of the <see cref="TableStore"/> class.</summary>
    /// <param name="options">Storage options.</param>
    /// <exception cref="InvalidOperationException">Thrown when no storage is configured.</exception>
    public TableStore(StorageOptions options)
    {
        _prefix = options.TablePrefix;
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            _service = new TableServiceClient(options.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(options.TableEndpoint))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = options.ManagedIdentityClientId,
            });
            _service = new TableServiceClient(new Uri(options.TableEndpoint), credential);
        }
        else
        {
            throw new InvalidOperationException("Configure Storage:ConnectionString or Storage:TableEndpoint.");
        }
    }

    /// <summary>Creates every table if missing.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task EnsureTablesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var name in Tables.All)
            await _service.CreateTableIfNotExistsAsync(_prefix + name, cancellationToken);
    }

    /// <summary>Gets a table client.</summary>
    /// <param name="name">Logical table name from <see cref="Tables"/>.</param>
    /// <returns>The client.</returns>
    public TableClient Table(string name) => _service.GetTableClient(_prefix + name);

    /// <summary>Reads an entity or returns <see langword="null"/> when missing.</summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="table">Logical table name.</param>
    /// <param name="partitionKey">Partition key.</param>
    /// <param name="rowKey">Row key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entity or <see langword="null"/>.</returns>
    public async Task<T?> GetAsync<T>(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
        where T : class, ITableEntity
    {
        var response = await Table(table).GetEntityIfExistsAsync<T>(partitionKey, rowKey, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    /// <summary>Reads every entity of a partition.</summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="table">Logical table name.</param>
    /// <param name="partitionKey">Partition key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entities.</returns>
    public async Task<List<T>> QueryPartitionAsync<T>(string table, string partitionKey, CancellationToken cancellationToken = default)
        where T : class, ITableEntity
    {
        List<T> result = [];
        await foreach (var entity in Table(table).QueryAsync<T>(e => e.PartitionKey == partitionKey, cancellationToken: cancellationToken))
            result.Add(entity);
        return result;
    }

    /// <summary>Inserts or replaces an entity.</summary>
    /// <param name="table">Logical table name.</param>
    /// <param name="entity">Entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task UpsertAsync(string table, ITableEntity entity, CancellationToken cancellationToken = default) =>
        Table(table).UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

    /// <summary>Inserts an entity, returning <see langword="false"/> when it already exists.</summary>
    /// <param name="table">Logical table name.</param>
    /// <param name="entity">Entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when inserted.</returns>
    public async Task<bool> TryAddAsync(string table, ITableEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            await Table(table).AddEntityAsync(entity, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }

    /// <summary>Deletes an entity, ignoring missing ones.</summary>
    /// <param name="table">Logical table name.</param>
    /// <param name="partitionKey">Partition key.</param>
    /// <param name="rowKey">Row key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task DeleteAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        Table(table).DeleteEntityAsync(partitionKey, rowKey, ETag.All, cancellationToken);
}
