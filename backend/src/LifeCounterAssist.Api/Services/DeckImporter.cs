using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LifeCounterAssist.Api.Services;

/// <summary>Supported deck-building sites.</summary>
public enum DeckSite
{
    /// <summary>Not recognized; the link is stored as-is.</summary>
    Unknown,

    /// <summary>archidekt.com.</summary>
    Archidekt,

    /// <summary>moxfield.com.</summary>
    Moxfield,
}

/// <summary>Result of parsing a deck URL.</summary>
/// <param name="Site">Detected site.</param>
/// <param name="ExternalId">Deck identifier on the site.</param>
public sealed record ParsedDeckUrl(DeckSite Site, string? ExternalId);

/// <summary>Data imported from an external deck.</summary>
/// <param name="Name">Deck name.</param>
/// <param name="CommanderName">Commander name(s).</param>
public sealed record ImportedDeck(string? Name, string? CommanderName);

/// <summary>Card details from Scryfall.</summary>
/// <param name="Name">Exact card name.</param>
/// <param name="ArtUrl">Art crop URL.</param>
/// <param name="ColorIdentity">Color identity letters, e.g. <c>WUB</c>.</param>
public sealed record CardInfo(string Name, string? ArtUrl, string ColorIdentity);

/// <summary>Imports deck metadata from Archidekt, Moxfield and Scryfall.</summary>
/// <remarks>
/// Only fixed API hosts are called, with identifiers extracted by <see cref="ParseUrl"/>,
/// so user-supplied URLs are never fetched directly.
/// </remarks>
public sealed partial class DeckImporter(HttpClient http, ILogger<DeckImporter> logger)
{
    /// <summary>Detects the site and deck identifier of a URL.</summary>
    /// <param name="url">User-provided URL.</param>
    /// <returns>The parsed URL.</returns>
    public static ParsedDeckUrl ParseUrl(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new ParsedDeckUrl(DeckSite.Unknown, null);

        var host = uri.Host.ToLowerInvariant();
        var archidekt = ArchidektPath().Match(uri.AbsolutePath);
        if (host is "archidekt.com" or "www.archidekt.com" && archidekt.Success)
            return new ParsedDeckUrl(DeckSite.Archidekt, archidekt.Groups[1].Value);

        var moxfield = MoxfieldPath().Match(uri.AbsolutePath);
        if (host is "moxfield.com" or "www.moxfield.com" && moxfield.Success)
            return new ParsedDeckUrl(DeckSite.Moxfield, moxfield.Groups[1].Value);

        return new ParsedDeckUrl(DeckSite.Unknown, null);
    }

    /// <summary>Fetches the deck name and commander from the site. Never throws.</summary>
    /// <param name="parsed">Parsed URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The imported data, or <see langword="null"/> on failure.</returns>
    public async Task<ImportedDeck?> ImportAsync(ParsedDeckUrl parsed, CancellationToken cancellationToken = default)
    {
        try
        {
            return parsed.Site switch
            {
                DeckSite.Archidekt => await ImportArchidektAsync(parsed.ExternalId!, cancellationToken),
                DeckSite.Moxfield => await ImportMoxfieldAsync(parsed.ExternalId!, cancellationToken),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Deck import failed for {Site} {DeckId}", parsed.Site, parsed.ExternalId);
            return null;
        }
    }

    /// <summary>Looks up a card on Scryfall with fuzzy matching. Never throws.</summary>
    /// <param name="name">Approximate card name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Card details, or <see langword="null"/> when not found.</returns>
    public async Task<CardInfo?> FindCardAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = "https://api.scryfall.com/cards/named?fuzzy=" + Uri.EscapeDataString(name);
            using var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var art = root.TryGetProperty("image_uris", out var images)
                ? images.GetProperty("art_crop").GetString()
                : root.TryGetProperty("card_faces", out var faces) && faces[0].TryGetProperty("image_uris", out var faceImages)
                    ? faceImages.GetProperty("art_crop").GetString()
                    : null;
            var colors = root.TryGetProperty("color_identity", out var ci)
                ? string.Concat(ci.EnumerateArray().Select(c => c.GetString()))
                : string.Empty;
            return new CardInfo(root.GetProperty("name").GetString() ?? name, art, colors);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Scryfall lookup failed for {Card}", name);
            return null;
        }
    }

    private async Task<ImportedDeck> ImportArchidektAsync(string id, CancellationToken cancellationToken)
    {
        var root = await http.GetFromJsonAsync<JsonElement>($"https://archidekt.com/api/decks/{id}/", cancellationToken);
        var commanders = root.GetProperty("cards").EnumerateArray()
            .Where(c => c.TryGetProperty("categories", out var cats)
                && cats.ValueKind == JsonValueKind.Array
                && cats.EnumerateArray().Any(x => x.GetString() == "Commander"))
            .Select(c => c.GetProperty("card").GetProperty("oracleCard").GetProperty("name").GetString())
            .OfType<string>()
            .ToList();
        return new ImportedDeck(root.GetProperty("name").GetString(), JoinCommanders(commanders));
    }

    private async Task<ImportedDeck> ImportMoxfieldAsync(string id, CancellationToken cancellationToken)
    {
        var root = await http.GetFromJsonAsync<JsonElement>($"https://api2.moxfield.com/v3/decks/all/{id}", cancellationToken);
        List<string> commanders = [];
        if (root.TryGetProperty("boards", out var boards)
            && boards.TryGetProperty("commanders", out var board)
            && board.TryGetProperty("cards", out var cards))
        {
            commanders.AddRange(cards.EnumerateObject()
                .Select(c => c.Value.GetProperty("card").GetProperty("name").GetString())
                .OfType<string>());
        }

        return new ImportedDeck(root.GetProperty("name").GetString(), JoinCommanders(commanders));
    }

    private static string? JoinCommanders(List<string> names) => names.Count == 0 ? null : string.Join(" / ", names);

    [GeneratedRegex(@"^/decks/(\d{1,12})(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ArchidektPath();

    [GeneratedRegex(@"^/decks/([A-Za-z0-9_-]{4,64})(?:/|$)")]
    private static partial Regex MoxfieldPath();
}
