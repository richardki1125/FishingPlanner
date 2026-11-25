using System.Text.Json.Serialization;

namespace FishingPlanner.Models;

/// <summary>
/// A Supabase projekt publikus csatlakozási adatait tároló konfigurációs modell.
/// Soha ne égesd be a forráskódba; eszközszintű tárhelyről vagy külső konfigurációs fájlból töltődik.
/// </summary>
public sealed class SupabaseConfig
{
    [JsonPropertyName("SupabaseUrl")]
    public string SupabaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("AnonKey")]
    public string AnonKey { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SupabaseUrl) &&
        !string.IsNullOrWhiteSpace(AnonKey) &&
        !SupabaseUrl.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) &&
        !AnonKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) &&
        Uri.TryCreate(SupabaseUrl.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    public string NormalizedUrl => SupabaseUrl.Trim().TrimEnd('/');
}
