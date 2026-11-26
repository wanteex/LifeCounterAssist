using LifeCounterAssist.Api.Domain;

namespace LifeCounterAssist.Api.Services;

/// <summary>Pushes game changes to connected clients.</summary>
public interface IGameNotifier
{
    /// <summary>Broadcasts a new game state to the game's watchers.</summary>
    /// <param name="state">Game state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task GameUpdatedAsync(GameState state, CancellationToken ct);

    /// <summary>Broadcasts the list of joinable games.</summary>
    /// <param name="games">Game summaries.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task ListChangedAsync(IReadOnlyList<GameSummaryDto> games, CancellationToken ct);

    /// <summary>Broadcasts the game currently featured on the room screen.</summary>
    /// <param name="featured">Featured game, or <see langword="null"/> when none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task ScreenChangedAsync(GameState? featured, CancellationToken ct);
}
