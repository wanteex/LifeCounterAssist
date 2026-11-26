using LifeCounterAssist.Api.Auth;
using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Infrastructure;

namespace LifeCounterAssist.Api.Services;

/// <summary>Deck returned by the API.</summary>
/// <param name="Id">Deck identifier.</param>
/// <param name="Name">Deck name.</param>
/// <param name="Url">External link.</param>
/// <param name="Source">Source site.</param>
/// <param name="CommanderName">Commander name(s).</param>
/// <param name="CommanderImageUrl">Commander art URL.</param>
/// <param name="ColorIdentity">Color identity letters.</param>
public sealed record DeckDto(
    string Id, string Name, string? Url, string Source, string? CommanderName, string? CommanderImageUrl, string? ColorIdentity);

/// <summary>Deck creation payload. Provide a URL, a commander name, or both.</summary>
/// <param name="Url">Archidekt/Moxfield/other deck link.</param>
/// <param name="Name">Deck name override.</param>
/// <param name="CommanderName">Commander name override.</param>
public sealed record AddDeckRequest(string? Url, string? Name, string? CommanderName);

/// <summary>Manages a user's decks.</summary>
public sealed class DeckService(TableStore store, DeckImporter importer, TimeProvider clock)
{
    private const int MaxDecksPerUser = 100;
    private const int MaxNameLength = 80;

    /// <summary>Lists the caller's decks.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Decks sorted by name.</returns>
    public async Task<List<DeckDto>> ListAsync(CurrentUser user, CancellationToken cancellationToken = default) =>
        (await store.QueryPartitionAsync<DeckEntity>(Tables.Decks, user.Id, cancellationToken))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();

    /// <summary>Reads one of the caller's decks.</summary>
    /// <param name="userId">Owner.</param>
    /// <param name="deckId">Deck identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deck or <see langword="null"/>.</returns>
    public Task<DeckEntity?> GetAsync(string userId, string deckId, CancellationToken cancellationToken = default) =>
        store.GetAsync<DeckEntity>(Tables.Decks, userId, deckId, cancellationToken);

    /// <summary>Adds a deck, importing metadata from the link and Scryfall when possible.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="request">Deck data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created deck.</returns>
    /// <exception cref="DomainException">Thrown when input is insufficient or the limit is reached.</exception>
    public async Task<DeckDto> AddAsync(CurrentUser user, AddDeckRequest request, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        if (url is not null && !Uri.TryCreate(url, UriKind.Absolute, out _)) throw new DomainException("invalid_deck_url");
        if (url is null && string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.CommanderName))
            throw new DomainException("deck_requires_url_or_name");

        var existing = await store.QueryPartitionAsync<DeckEntity>(Tables.Decks, user.Id, cancellationToken);
        if (existing.Count >= MaxDecksPerUser) throw new DomainException("too_many_decks");

        var parsed = DeckImporter.ParseUrl(url);
        var imported = await importer.ImportAsync(parsed, cancellationToken);
        var commanderName = Clean(request.CommanderName) ?? imported?.CommanderName;

        CardInfo? card = null;
        if (commanderName is not null)
        {
            // Partner pairs are "A / B"; art and identity come from the first commander.
            card = await importer.FindCardAsync(commanderName.Split(" / ")[0], cancellationToken);
            if (card is not null && !commanderName.Contains(" / ")) commanderName = card.Name;
        }

        var entity = new DeckEntity
        {
            PartitionKey = user.Id,
            RowKey = Guid.NewGuid().ToString("N"),
            Name = Truncate(Clean(request.Name) ?? Clean(imported?.Name) ?? commanderName ?? "Deck"),
            Url = url,
            Source = parsed.Site == DeckSite.Unknown ? (url is null ? "manual" : "link") : parsed.Site.ToString().ToLowerInvariant(),
            CommanderName = commanderName,
            CommanderImageUrl = card?.ArtUrl,
            ColorIdentity = card?.ColorIdentity,
            CreatedAt = clock.GetUtcNow(),
        };
        await store.UpsertAsync(Tables.Decks, entity, cancellationToken);
        return ToDto(entity);
    }

    /// <summary>Deletes one of the caller's decks.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="deckId">Deck identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task DeleteAsync(CurrentUser user, string deckId, CancellationToken cancellationToken = default) =>
        store.DeleteAsync(Tables.Decks, user.Id, deckId, cancellationToken);

    /// <summary>Maps an entity to its DTO.</summary>
    /// <param name="d">Entity.</param>
    /// <returns>The DTO.</returns>
    public static DeckDto ToDto(DeckEntity d) =>
        new(d.RowKey, d.Name, d.Url, d.Source, d.CommanderName, d.CommanderImageUrl, d.ColorIdentity);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value) => value.Length > MaxNameLength ? value[..MaxNameLength] : value;
}
