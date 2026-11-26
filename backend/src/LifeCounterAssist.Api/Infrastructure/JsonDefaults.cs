using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeCounterAssist.Api.Infrastructure;

/// <summary>Shared JSON settings for HTTP, SignalR and persistence.</summary>
public static class JsonDefaults
{
    /// <summary>Gets web defaults with string enums.</summary>
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Applies the shared settings to existing options.</summary>
    /// <param name="options">Options to configure.</param>
    /// <returns>The same options.</returns>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}
