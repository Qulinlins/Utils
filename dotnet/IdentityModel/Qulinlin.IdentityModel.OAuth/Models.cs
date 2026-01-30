using System.Text.Json.Serialization;

namespace Qulinlin.IdentityModel.OAuth;

public record BaseResult
{
    [JsonPropertyName("token_type")] public string? TokenType;
    [JsonPropertyName("expires_in")] public ulong ExpiresIn;
}

public record ErrorResult
{
    [JsonPropertyName("error")] public string? Error;
    [JsonPropertyName("error_description")] public string? ErrorDescription;
    
}

public sealed record DeviceFlowData:ErrorResult
{
    [JsonPropertyName("user_code")] public string? UserCode;
    [JsonPropertyName("device_code")] public string? DeviceCode;
    [JsonPropertyName("verification_uri")] public string? VerificationUri;
    [JsonPropertyName("verification_uri_complete")] public string? VerificationUriComplete;
    [JsonPropertyName("interval")] public int Interval = 5;
}

public record AuthorizeResult:ErrorResult
{
    [JsonPropertyName("access_token")] public string? AccessToken;
    [JsonPropertyName("refresh_token")] public string? RefreshToken;
    [JsonPropertyName("id_token")] public string? IdToken;
}


