using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FishingPlanner.Models;

/// <summary>
/// A bejelentkezett horgász munkamenetét és tokenjeit tároló modell.
/// </summary>
public sealed class UserSession
{
    [JsonPropertyName("user_id")]
    public Guid UserId { get; set; }

    [JsonPropertyName("felhasznalonev")]
    public string Felhasznalonev { get; set; } = string.Empty;

    [JsonPropertyName("megjelenitett_nev")]
    public string? MegjelenitettNev { get; set; }

    [JsonPropertyName("profilkep_url")]
    public string? ProfilkepUrl { get; set; }

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at_utc")]
    public DateTimeOffset ExpiresAtUtc { get; set; }

    [JsonPropertyName("is_admin")]
    public bool IsAdmin { get; set; }

    [JsonIgnore]
    public string MegjelenitesiNev =>
        !string.IsNullOrWhiteSpace(MegjelenitettNev) ? MegjelenitettNev.Trim() : Felhasznalonev;

    [JsonIgnore]
    public bool IsExpiredSoon => DateTimeOffset.UtcNow.AddMinutes(1) >= ExpiresAtUtc;
}

public sealed class LoginModel
{
    [Required(ErrorMessage = "A felhasználónév megadása kötelező.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "A felhasználónév 3 és 30 karakter között legyen.")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Csak angol betűk, számok, pont, aláhúzás vagy kötőjel használható.")]
    public string Felhasznalonev { get; set; } = string.Empty;

    [Required(ErrorMessage = "A jelszó megadása kötelező.")]
    [MinLength(6, ErrorMessage = "A jelszónak legalább 6 karakter hosszúnak kell lennie.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterModel
{
    [Required(ErrorMessage = "A felhasználónév megadása kötelező.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "A felhasználónév 3 és 30 karakter között legyen.")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Csak angol betűk, számok, pont, aláhúzás vagy kötőjel használható.")]
    public string Felhasznalonev { get; set; } = string.Empty;

    [Required(ErrorMessage = "A jelszó megadása kötelező.")]
    [MinLength(6, ErrorMessage = "A jelszó legalább 6 karakter hosszú legyen.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "A jelszó megerősítése kötelező.")]
    [Compare(nameof(Password), ErrorMessage = "A két jelszó nem egyezik meg.")]
    public string PasswordConfirm { get; set; } = string.Empty;
}

public sealed record AuthResult(
    bool Success,
    bool RequiresEmailConfirmation = false,
    string? ErrorMessage = null,
    UserSession? Session = null);
