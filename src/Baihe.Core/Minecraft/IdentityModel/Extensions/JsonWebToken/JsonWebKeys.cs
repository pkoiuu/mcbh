using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;

namespace Baihe.Core.Minecraft.IdentityModel.Extensions.JsonWebToken;

public record JsonWebKeys
{
    [JsonPropertyName("keys")] public required JsonWebKey[] Keys { get; init; }
}