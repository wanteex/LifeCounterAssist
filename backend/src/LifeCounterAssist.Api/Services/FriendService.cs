using LifeCounterAssist.Api.Auth;
using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Hubs;
using LifeCounterAssist.Api.Infrastructure;

namespace LifeCounterAssist.Api.Services;

/// <summary>Friend returned by the API.</summary>
/// <param name="UserId">Friend identifier.</param>
/// <param name="DisplayName">Friend display name.</param>
/// <param name="Status"><c>Pending</c> or <c>Accepted</c>.</param>
/// <param name="Direction"><c>Outgoing</c> or <c>Incoming</c>.</param>
public sealed record FriendDto(string UserId, string DisplayName, string Status, string Direction);

/// <summary>Friend request payload.</summary>
/// <param name="FriendCode">Code of the user to add.</param>
public sealed record AddFriendRequest(string FriendCode);

/// <summary>Manages friendships. Each friendship is stored on both users' partitions.</summary>
public sealed class FriendService(TableStore store, UserService users, Notifier notifier, TimeProvider clock)
{
    /// <summary>Lists the caller's friends and pending requests.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Friends sorted by name.</returns>
    public async Task<List<FriendDto>> ListAsync(CurrentUser user, CancellationToken cancellationToken = default)
    {
        var edges = await store.QueryPartitionAsync<FriendEntity>(Tables.Friends, user.Id, cancellationToken);
        var profiles = await Task.WhenAll(edges.Select(e => users.GetAsync(e.RowKey, cancellationToken)));
        return edges
            .Zip(profiles)
            .Where(x => x.Second is not null)
            .Select(x => new FriendDto(x.First.RowKey, x.Second!.DisplayName, x.First.Status, x.First.Direction))
            .OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Sends a friend request, or accepts it when the other user already asked.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="request">Request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resulting friend entry.</returns>
    /// <exception cref="NotFoundException">Thrown when the code is unknown.</exception>
    /// <exception cref="DomainException">Thrown when adding yourself.</exception>
    public async Task<FriendDto> AddAsync(CurrentUser user, AddFriendRequest request, CancellationToken cancellationToken = default)
    {
        var target = await users.FindByFriendCodeAsync(request.FriendCode, cancellationToken)
            ?? throw new NotFoundException("friend_code_not_found");
        if (target.RowKey == user.Id) throw new DomainException("cannot_add_self");

        var existing = await GetEdgeAsync(user.Id, target.RowKey, cancellationToken);
        if (existing is { Status: FriendStatus.Pending, Direction: FriendDirection.Incoming })
            return await AcceptAsync(user, target.RowKey, cancellationToken);
        if (existing is not null)
            return new FriendDto(target.RowKey, target.DisplayName, existing.Status, existing.Direction);

        var now = clock.GetUtcNow();
        await store.UpsertAsync(Tables.Friends, Edge(user.Id, target.RowKey, FriendStatus.Pending, FriendDirection.Outgoing, now), cancellationToken);
        await store.UpsertAsync(Tables.Friends, Edge(target.RowKey, user.Id, FriendStatus.Pending, FriendDirection.Incoming, now), cancellationToken);
        await notifier.FriendsChangedAsync(target.RowKey, cancellationToken);
        return new FriendDto(target.RowKey, target.DisplayName, FriendStatus.Pending, FriendDirection.Outgoing);
    }

    /// <summary>Accepts an incoming friend request.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="friendUserId">User who sent the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accepted friend.</returns>
    /// <exception cref="NotFoundException">Thrown when no incoming request exists.</exception>
    public async Task<FriendDto> AcceptAsync(CurrentUser user, string friendUserId, CancellationToken cancellationToken = default)
    {
        var edge = await GetEdgeAsync(user.Id, friendUserId, cancellationToken);
        if (edge is not { Direction: FriendDirection.Incoming }) throw new NotFoundException("friend_request_not_found");

        var now = clock.GetUtcNow();
        await store.UpsertAsync(Tables.Friends, Edge(user.Id, friendUserId, FriendStatus.Accepted, FriendDirection.Incoming, edge.CreatedAt), cancellationToken);
        await store.UpsertAsync(Tables.Friends, Edge(friendUserId, user.Id, FriendStatus.Accepted, FriendDirection.Outgoing, now), cancellationToken);
        await notifier.FriendsChangedAsync(friendUserId, cancellationToken);

        var friend = await users.GetAsync(friendUserId, cancellationToken);
        return new FriendDto(friendUserId, friend?.DisplayName ?? "?", FriendStatus.Accepted, FriendDirection.Incoming);
    }

    /// <summary>Removes a friend or declines/cancels a request.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="friendUserId">Other user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RemoveAsync(CurrentUser user, string friendUserId, CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(Tables.Friends, user.Id, friendUserId, cancellationToken);
        await store.DeleteAsync(Tables.Friends, friendUserId, user.Id, cancellationToken);
        await notifier.FriendsChangedAsync(friendUserId, cancellationToken);
    }

    /// <summary>Checks whether two users are accepted friends.</summary>
    /// <param name="userId">First user.</param>
    /// <param name="otherUserId">Second user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when friends.</returns>
    public async Task<bool> AreFriendsAsync(string userId, string otherUserId, CancellationToken cancellationToken = default) =>
        (await GetEdgeAsync(userId, otherUserId, cancellationToken))?.Status == FriendStatus.Accepted;

    private Task<FriendEntity?> GetEdgeAsync(string userId, string friendUserId, CancellationToken cancellationToken) =>
        store.GetAsync<FriendEntity>(Tables.Friends, userId, friendUserId, cancellationToken);

    private static FriendEntity Edge(string owner, string friend, string status, string direction, DateTimeOffset createdAt) =>
        new() { PartitionKey = owner, RowKey = friend, Status = status, Direction = direction, CreatedAt = createdAt };
}
