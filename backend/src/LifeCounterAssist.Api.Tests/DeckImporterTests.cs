using LifeCounterAssist.Api.Services;

namespace LifeCounterAssist.Api.Tests;

public class DeckImporterTests
{
    [Theory]
    [InlineData("https://archidekt.com/decks/123456/my-deck", DeckSite.Archidekt, "123456")]
    [InlineData("https://www.archidekt.com/decks/42", DeckSite.Archidekt, "42")]
    [InlineData("https://moxfield.com/decks/AbC-123_xyz", DeckSite.Moxfield, "AbC-123_xyz")]
    [InlineData("https://www.moxfield.com/decks/AbC-123_xyz/primer", DeckSite.Moxfield, "AbC-123_xyz")]
    public void ParseUrl_RecognizesSupportedSites(string url, DeckSite site, string id)
    {
        Assert.Equal(new ParsedDeckUrl(site, id), DeckImporter.ParseUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("ftp://archidekt.com/decks/1")]
    [InlineData("https://archidekt.com.evil.example/decks/1")]
    [InlineData("https://archidekt.com/decks/abc")]
    [InlineData("https://moxfield.com/decks/../../admin")]
    [InlineData("https://tappedout.net/mtg-decks/my-deck/")]
    public void ParseUrl_ReturnsUnknownForUnsupportedOrUnsafeInput(string? url)
    {
        Assert.Equal(new ParsedDeckUrl(DeckSite.Unknown, null), DeckImporter.ParseUrl(url));
    }
}
