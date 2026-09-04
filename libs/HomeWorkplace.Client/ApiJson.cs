using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeWorkplace.Client;

/// <summary>One serializer configuration for everything the services speak: camelCase, enums as numbers.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
