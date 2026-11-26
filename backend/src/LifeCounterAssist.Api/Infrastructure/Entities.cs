using Azure;
using Azure.Data.Tables;

namespace LifeCounterAssist.Api.Infrastructure;

/// <summary>Base class for table entities with the required system properties.</summary>
public abstract class EntityBase : ITableEntity
{
    /// <inheritdoc/>
    public string PartitionKey { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string RowKey { get; set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset? Timestamp { get; set; }

    /// <inheritdoc/>
    public ETag ETag { get; set; }
}

/// <summary>User profile. PartitionKey = "user", RowKey = user identifier.</summary>
public sealed class UserEntity : EntityBase
{
    /// <summary>Partition holding every profile.</summary>
    public const string Partition = "user";

    /// <summary>Gets or sets the display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the email.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the friend code used to add this user.</summary>
    public string FriendCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the preferred UI language.</summary>
    public string? Language { get; set; }

    /// <summary>Gets or sets the creation date.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Friend code index. PartitionKey = "code", RowKey = friend code.</summary>
public sealed class FriendCodeEntity : EntityBase
{
    /// <summary>Partition holding every friend code.</summary>
    public const string Partition = "code";

    /// <summary>Gets or sets the owner of the code.</summary>
    public string UserId { get; set; } = string.Empty;
}

/// <summary>Friendship edge. PartitionKey = user, RowKey = friend.</summary>
public sealed class FriendEntity : EntityBase
{
    /// <summary>Gets or sets <c>Pending</c> or <c>Accepted</c>.</summary>
    public string Status { get; set; } = FriendStatus.Pending;

    /// <summary>Gets or sets <c>Outgoing</c> or <c>Incoming</c>.</summary>
    public string Direction { get; set; } = FriendDirection.Outgoing;

    /// <summary>Gets or sets the creation date.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Friendship status values.</summary>
public static class FriendStatus
{
    /// <summary>Request not accepted yet.</summary>
    public const string Pending = "Pending";

    /// <summary>Both users are friends.</summary>
    public const string Accepted = "Accepted";
}

/// <summary>Friendship request direction values.</summary>
public static class FriendDirection
{
    /// <summary>The partition owner sent the request.</summary>
    public const string Outgoing = "Outgoing";

    /// <summary>The partition owner received the request.</summary>
    public const string Incoming = "Incoming";
}

/// <summary>Deck. PartitionKey = owner, RowKey = deck identifier.</summary>
public sealed class DeckEntity : EntityBase
{
    /// <summary>Gets or sets the deck name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the external deck URL.</summary>
    public string? Url { get; set; }

    /// <summary>Gets or sets the source (archidekt, moxfield, manual, link).</summary>
    public string Source { get; set; } = "manual";

    /// <summary>Gets or sets the commander name.</summary>
    public string? CommanderName { get; set; }

    /// <summary>Gets or sets the commander art URL.</summary>
    public string? CommanderImageUrl { get; set; }

    /// <summary>Gets or sets the color identity, e.g. <c>WUB</c>.</summary>
    public string? ColorIdentity { get; set; }

    /// <summary>Gets or sets the creation date.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Serialized game state. PartitionKey = game identifier, RowKey = "state".</summary>
public sealed class GameEntity : EntityBase
{
    /// <summary>Row key of the state row.</summary>
    public const string StateRow = "state";

    /// <summary>Gets or sets the JSON-serialized state.</summary>
    public string StateJson { get; set; } = string.Empty;

    /// <summary>Gets or sets the status, duplicated for queries.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>Join code index. PartitionKey = "joincode", RowKey = code.</summary>
public sealed class JoinCodeEntity : EntityBase
{
    /// <summary>Partition holding join codes.</summary>
    public const string Partition = "joincode";

    /// <summary>Gets or sets the game identifier.</summary>
    public string GameId { get; set; } = string.Empty;
}

/// <summary>Per-user game summary for history. PartitionKey = user, RowKey = game identifier.</summary>
public sealed class PlayerGameEntity : EntityBase
{
    /// <summary>Gets or sets the game status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the creation date.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the finish date.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Gets or sets a value indicating whether the user won.</summary>
    public bool Won { get; set; }

    /// <summary>Gets or sets the winner name.</summary>
    public string? WinnerName { get; set; }

    /// <summary>Gets or sets the number of players.</summary>
    public int PlayerCount { get; set; }

    /// <summary>Gets or sets the comma-separated player names.</summary>
    public string PlayerNames { get; set; } = string.Empty;

    /// <summary>Gets or sets the deck the user played.</summary>
    public string? DeckName { get; set; }

    /// <summary>Gets or sets the commander the user played.</summary>
    public string? CommanderName { get; set; }

    /// <summary>Gets or sets the user's final life total.</summary>
    public int? FinalLife { get; set; }
}

/// <summary>Pending invitation. PartitionKey = invited user, RowKey = game identifier.</summary>
public sealed class InvitationEntity : EntityBase
{
    /// <summary>Gets or sets the host identifier.</summary>
    public string HostUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the host display name.</summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>Gets or sets the creation date.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
