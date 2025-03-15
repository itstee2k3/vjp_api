namespace vjp_api.Models;
using System.Text.Json.Serialization;

public class TokenModel
{
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; }
}