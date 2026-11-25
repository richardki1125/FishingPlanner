using System.Text.Json.Serialization;
using FishingPlanner.Models;

namespace FishingPlanner.Services;

// --- Supabase Auth (GoTrue) DTO-k ---

public sealed class SignInRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class SignUpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public SignUpUserData? Data { get; set; }
}

public sealed class SignUpUserData
{
    public string Felhasznalonev { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class AuthTokenResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public AuthUserDto? User { get; set; }
    public Guid? Id { get; set; }
    public string? Email { get; set; }
}

public sealed class AuthUserDto
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public AuthUserMetadataDto? UserMetadata { get; set; }
}

public sealed class AuthUserMetadataDto
{
    public string? Felhasznalonev { get; set; }
}

public sealed class AuthErrorResponse
{
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    public string? Msg { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
}

// --- Supabase PostgREST DTO-k ---

public sealed class ProfileDto
{
    public Guid Id { get; set; }
    public string Felhasznalonev { get; set; } = string.Empty;
}

public sealed class ProfileListItemDto
{
    public Guid Id { get; set; }
    public string Felhasznalonev { get; set; } = string.Empty;
    public string? MegjelenitettNev { get; set; }
    public string? ProfilkepUrl { get; set; }
}

public sealed class ProfileWithRoleDto
{
    public Guid Id { get; set; }
    public string Felhasznalonev { get; set; } = string.Empty;
    public string? MegjelenitettNev { get; set; }
    public string? ProfilkepUrl { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class ProfileUpdateDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? MegjelenitettNev { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ProfilkepUrl { get; set; }
}

public sealed class ResztvevoNevPatchDto
{
    public string Nev { get; set; } = string.Empty;
}

public sealed class HalKategoriaDto
{
    public Guid Id { get; set; }
    public string Nev { get; set; } = string.Empty;
    public int Sorrend { get; set; } = 100;
}

public sealed class HalAlkategoriaDto
{
    public Guid Id { get; set; }
    public Guid KategoriaId { get; set; }
    public string Nev { get; set; } = string.Empty;
    public int Sorrend { get; set; } = 100;
}

public sealed class TuraDto
{
    public Guid Id { get; set; }
    public string Nev { get; set; } = string.Empty;
    public string Helyszin { get; set; } = string.Empty;
    public string KezdoDatum { get; set; } = string.Empty;
    public int NapokSzama { get; set; }
}

public sealed class ResztvevoDto
{
    public Guid Id { get; set; }
    public Guid TuraId { get; set; }
    public Guid? UserId { get; set; }
    public string Nev { get; set; } = string.Empty;
    public string Szerepkor { get; set; } = string.Empty;
    public bool AktivE { get; set; } = true;
}

public sealed class ResztvevoAktivPatchDto
{
    public bool AktivE { get; set; }
}

public sealed class ResztvevoReaktivPatchDto
{
    public bool AktivE { get; set; } = true;
    public string Nev { get; set; } = string.Empty;
    public string Szerepkor { get; set; } = string.Empty;
}

public sealed class FelszerelesTetelDto
{
    public Guid Id { get; set; }
    public Guid TuraId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? ResztvevoId { get; set; }

    public string Nev { get; set; } = string.Empty;
    public string Kategoria { get; set; } = string.Empty;
    public int Mennyiseg { get; set; }
    public bool Bepakolva { get; set; }
    public bool KozosE { get; set; }
}

public sealed class FelszerelesBepakolvaPatchDto
{
    public bool Bepakolva { get; set; }
}

public sealed class FogasDto
{
    public Guid Id { get; set; }
    public Guid TuraId { get; set; }
    public Guid ResztvevoId { get; set; }
    public string Halfaj { get; set; } = string.Empty;
    public bool DarabAlapuE { get; set; }
    public int Mennyiseg { get; set; } = 1;
    public bool VisszaengedettE { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? SulyKg { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? HosszCm { get; set; }

    public DateTimeOffset Idopont { get; set; }
    public string Csali { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Megjegyzes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? KepUrl { get; set; }
}

public sealed class KoltsegTetelDto
{
    public Guid Id { get; set; }
    public Guid TuraId { get; set; }
    public string Nev { get; set; } = string.Empty;
    public decimal Osszeg { get; set; }
    public bool KozosE { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? ResztvevoId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? FizetoResztvevoId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Megjegyzes { get; set; }
}

public sealed class CsomaglistaSablonDto
{
    public Guid Id { get; set; }
    public string Nev { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Leiras { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class CsomaglistaSablonTetelDto
{
    public Guid Id { get; set; }
    public Guid SablonId { get; set; }
    public string Nev { get; set; } = string.Empty;
    public string Kategoria { get; set; } = "Egyéb";
    public int Mennyiseg { get; set; } = 1;
    public bool KozosE { get; set; } = true;
}

public sealed class PostgrestErrorResponse
{
    public string? Message { get; set; }
    public string? Details { get; set; }
    public string? Hint { get; set; }
    public string? Code { get; set; }
}

/// <summary>
/// Source-generated System.Text.Json kontextus az Android Release (AOT/trimming) kompatibilitáshoz.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SupabaseConfig))]
[JsonSerializable(typeof(UserSession))]
[JsonSerializable(typeof(SignInRequest))]
[JsonSerializable(typeof(SignUpRequest))]
[JsonSerializable(typeof(SignUpUserData))]
[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(AuthTokenResponse))]
[JsonSerializable(typeof(AuthUserDto))]
[JsonSerializable(typeof(AuthUserMetadataDto))]
[JsonSerializable(typeof(AuthErrorResponse))]
[JsonSerializable(typeof(ProfileDto))]
[JsonSerializable(typeof(List<ProfileDto>))]
[JsonSerializable(typeof(ProfileListItemDto))]
[JsonSerializable(typeof(List<ProfileListItemDto>))]
[JsonSerializable(typeof(ProfileWithRoleDto))]
[JsonSerializable(typeof(List<ProfileWithRoleDto>))]
[JsonSerializable(typeof(ProfileUpdateDto))]
[JsonSerializable(typeof(ResztvevoNevPatchDto))]
[JsonSerializable(typeof(HalKategoriaDto))]
[JsonSerializable(typeof(List<HalKategoriaDto>))]
[JsonSerializable(typeof(HalAlkategoriaDto))]
[JsonSerializable(typeof(List<HalAlkategoriaDto>))]
[JsonSerializable(typeof(TuraDto))]
[JsonSerializable(typeof(List<TuraDto>))]
[JsonSerializable(typeof(ResztvevoDto))]
[JsonSerializable(typeof(List<ResztvevoDto>))]
[JsonSerializable(typeof(ResztvevoAktivPatchDto))]
[JsonSerializable(typeof(ResztvevoReaktivPatchDto))]
[JsonSerializable(typeof(FelszerelesTetelDto))]
[JsonSerializable(typeof(List<FelszerelesTetelDto>))]
[JsonSerializable(typeof(FelszerelesBepakolvaPatchDto))]
[JsonSerializable(typeof(FogasDto))]
[JsonSerializable(typeof(List<FogasDto>))]
[JsonSerializable(typeof(KoltsegTetelDto))]
[JsonSerializable(typeof(List<KoltsegTetelDto>))]
[JsonSerializable(typeof(CsomaglistaSablonDto))]
[JsonSerializable(typeof(List<CsomaglistaSablonDto>))]
[JsonSerializable(typeof(CsomaglistaSablonTetelDto))]
[JsonSerializable(typeof(List<CsomaglistaSablonTetelDto>))]
[JsonSerializable(typeof(PostgrestErrorResponse))]
[JsonSerializable(typeof(OfflineCacheStore))]
[JsonSerializable(typeof(PendingSyncOperation))]
[JsonSerializable(typeof(List<PendingSyncOperation>))]
[JsonSerializable(typeof(FishingPlannerBackup))]
[JsonSerializable(typeof(Tura))]
[JsonSerializable(typeof(List<Tura>))]
[JsonSerializable(typeof(Resztvevo))]
[JsonSerializable(typeof(List<Resztvevo>))]
[JsonSerializable(typeof(FelszerelesTetel))]
[JsonSerializable(typeof(List<FelszerelesTetel>))]
[JsonSerializable(typeof(Fogas))]
[JsonSerializable(typeof(List<Fogas>))]
[JsonSerializable(typeof(KoltsegTetel))]
[JsonSerializable(typeof(List<KoltsegTetel>))]
[JsonSerializable(typeof(CsomaglistaSablon))]
[JsonSerializable(typeof(List<CsomaglistaSablon>))]
[JsonSerializable(typeof(CsomaglistaSablonTetel))]
[JsonSerializable(typeof(List<CsomaglistaSablonTetel>))]
[JsonSerializable(typeof(RegisztraltFelhasznalo))]
[JsonSerializable(typeof(List<RegisztraltFelhasznalo>))]
[JsonSerializable(typeof(HalKategoria))]
[JsonSerializable(typeof(List<HalKategoria>))]
[JsonSerializable(typeof(HalAlkategoria))]
[JsonSerializable(typeof(List<HalAlkategoria>))]
public partial class SupabaseJsonContext : JsonSerializerContext
{
}
