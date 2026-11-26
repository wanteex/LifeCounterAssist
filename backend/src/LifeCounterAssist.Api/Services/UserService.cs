using LifeCounterAssist.Api.Auth;
using LifeCounterAssist.Api.Domain;
using LifeCounterAssist.Api.Infrastructure;

namespace LifeCounterAssist.Api.Services;

/// <summary>Public profile returned by the API.</summary>
/// <param name="Id">User identifier.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="FriendCode">Code others use to add this user.</param>
/// <param name="Language">Preferred language.</param>
public sealed record ProfileDto(string Id, string DisplayName, string FriendCode, string? Language);

/// <summary>Profile update payload.</summary>
/// <param name="DisplayName">New display name.</param>
/// <param name="Language">Preferred language (<c>fr</c> or <c>en</c>).</param>
public sealed record UpdateProfileRequest(string? DisplayName, string? Language);

/// <summary>Manages user profiles and friend codes.</summary>
public sealed class UserService(TableStore store, TimeProvider clock)
{
    private const int FriendCodeLength = 6;
    private const int MaxDisplayNameLength = 40;

    /// <summary>Returns the caller profile, creating it on first sign-in.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile entity.</returns>
    public async Task<UserEntity> EnsureProfileAsync(CurrentUser user, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(user.Id, cancellationToken);
        if (existing is not null) return existing;

        var code = await ReserveFriendCodeAsync(user.Id, cancellationToken);
        var entity = new UserEntity
        {
            PartitionKey = UserEntity.Partition,
            RowKey = user.Id,
            DisplayName = Truncate(user.DisplayName),
            Email = user.Email,
            FriendCode = code,
            CreatedAt = clock.GetUtcNow(),
        };
        if (await store.TryAddAsync(Tables.Users, entity, cancellationToken)) return entity;

        // Lost a race with a concurrent first request; release our code and use the stored profile.
        await store.DeleteAsync(Tables.Users, FriendCodeEntity.Partition, code, cancellationToken);
        return await GetAsync(user.Id, cancellationToken) ?? throw new InvalidOperationException("Profile creation failed.");
    }

    /// <summary>Reads a profile.</summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile or <see langword="null"/>.</returns>
    public Task<UserEntity?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        store.GetAsync<UserEntity>(Tables.Users, UserEntity.Partition, userId, cancellationToken);

    /// <summary>Updates the caller profile.</summary>
    /// <param name="user">Current user.</param>
    /// <param name="request">Changes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated profile.</returns>
    /// <exception cref="DomainException">Thrown when values are invalid.</exception>
    public async Task<UserEntity> UpdateAsync(CurrentUser user, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await EnsureProfileAsync(user, cancellationToken);
        if (request.DisplayName is not null)
        {
            var name = request.DisplayName.Trim();
            if (name.Length == 0) throw new DomainException("invalid_display_name");
            entity.DisplayName = Truncate(name);
        }

        if (request.Language is not null)
        {
            if (request.Language is not ("fr" or "en")) throw new DomainException("invalid_language");
            entity.Language = request.Language;
        }

        await store.UpsertAsync(Tables.Users, entity, cancellationToken);
        return entity;
    }

    /// <summary>Finds a user by friend code.</summary>
    /// <param name="friendCode">Friend code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile or <see langword="null"/>.</returns>
    public async Task<UserEntity?> FindByFriendCodeAsync(string friendCode, CancellationToken cancellationToken = default)
    {
        var code = Codes.Normalize(friendCode);
        if (code.Length != FriendCodeLength) return null;
        var index = await store.GetAsync<FriendCodeEntity>(Tables.Users, FriendCodeEntity.Partition, code, cancellationToken);
        return index is null ? null : await GetAsync(index.UserId, cancellationToken);
    }

    /// <summary>Maps an entity to its DTO.</summary>
    /// <param name="entity">Entity.</param>
    /// <returns>The DTO.</returns>
    public static ProfileDto ToDto(UserEntity entity) =>
        new(entity.RowKey, entity.DisplayName, entity.FriendCode, entity.Language);

    private async Task<string> ReserveFriendCodeAsync(string userId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = Codes.New(FriendCodeLength);
            var index = new FriendCodeEntity { PartitionKey = FriendCodeEntity.Partition, RowKey = code, UserId = userId };
            if (await store.TryAddAsync(Tables.Users, index, cancellationToken)) return code;
        }

        throw new InvalidOperationException("Could not allocate a friend code.");
    }

    private static string Truncate(string value) =>
        value.Length > MaxDisplayNameLength ? value[..MaxDisplayNameLength] : value;
}
