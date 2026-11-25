using FishingPlanner.Models;

namespace FishingPlanner.Services;

/// <summary>
/// A felhasználónév + jelszó alapú hitelesítés (regisztráció, bejelentkezés, token-frissítés, kijelentkezés) szerződése.
/// </summary>
public interface IAuthService
{
    UserSession? CurrentUser { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    bool IsInitialized { get; }

    event Action<UserSession?>? AuthStateChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default);
    Task<AuthResult> SignInAsync(string felhasznalonev, string password, CancellationToken ct = default);
    Task<AuthResult> SignUpAsync(string felhasznalonev, string password, CancellationToken ct = default);
    Task<AuthResult> UpdateProfileAsync(string? megjelenitettNev, string? profilkepUrl, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
}
