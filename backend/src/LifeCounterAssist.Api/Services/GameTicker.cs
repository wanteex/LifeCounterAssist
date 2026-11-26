namespace LifeCounterAssist.Api.Services;

/// <summary>Calls <see cref="GameService.TickAsync"/> every second; failures are logged and never stop the loop.</summary>
public sealed class GameTicker(GameService games, ILogger<GameTicker> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await games.TickAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Game tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down.
        }
    }
}
