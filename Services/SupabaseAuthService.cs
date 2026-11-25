using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FishingPlanner.Models;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// A Supabase Auth (GoTrue REST API) alapú felhasználónév + jelszó hitelesítés,
/// automatikus JWT frissítéssel és tartós munkamenet-tárolással (e-mail megadása nélkül).
/// </summary>
public sealed class SupabaseAuthService : IAuthService
{
    private const string SessionStorageKey = "supabase_user_session";
    private const string InternalEmailDomain = "fishingplanner.app";
    private const string DefaultAdminUsername = "Richard";

    private readonly HttpClient _httpClient;
    private readonly ISupabaseConfigService _configService;
    private readonly ILogger<SupabaseAuthService> _logger;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    public UserSession? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null && !string.IsNullOrWhiteSpace(CurrentUser.AccessToken);
    public bool IsAdmin => CurrentUser?.IsAdmin ?? false;
    public bool IsInitialized { get; private set; }

    public event Action<UserSession?>? AuthStateChanged;

    public SupabaseAuthService(
        HttpClient httpClient,
        ISupabaseConfigService configService,
        ILogger<SupabaseAuthService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (IsInitialized)
        {
            return;
        }

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (IsInitialized)
            {
                return;
            }

            var json = await ReadSessionJsonAsync();
            if (!string.IsNullOrWhiteSpace(json))
            {
                var saved = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.UserSession);
                if (saved is not null && !string.IsNullOrWhiteSpace(saved.RefreshToken))
                {
                    CurrentUser = saved;
                    if (saved.IsExpiredSoon)
                    {
                        await RefreshSessionInternalAsync(ct);
                    }

                    if (CurrentUser is not null)
                    {
                        await EnsureProfileAndRoleAsync(CurrentUser, ct);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "A mentett munkamenet visszaállítása sikertelen, újbóli belépés szükséges.");
            await ClearSessionInternalAsync();
        }
        finally
        {
            IsInitialized = true;
            _tokenGate.Release();
            AuthStateChanged?.Invoke(CurrentUser);
        }
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        if (CurrentUser is null)
        {
            return null;
        }

        if (!CurrentUser.IsExpiredSoon)
        {
            return CurrentUser.AccessToken;
        }

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (CurrentUser is null)
            {
                return null;
            }

            if (!CurrentUser.IsExpiredSoon)
            {
                return CurrentUser.AccessToken;
            }

            var refreshed = await RefreshSessionInternalAsync(ct);
            return refreshed ? CurrentUser?.AccessToken : null;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public async Task<AuthResult> SignInAsync(string felhasznalonev, string password, CancellationToken ct = default)
    {
        var config = await _configService.GetConfigAsync(ct);
        if (!config.IsConfigured)
        {
            return new AuthResult(false, ErrorMessage: "Nincs megadva a Supabase Project URL és az Anon kulcs.");
        }

        var trimmedUser = felhasznalonev.Trim();

        try
        {
            var payload = new SignInRequest
            {
                Email = ToInternalEmail(trimmedUser),
                Password = password
            };

            using var request = CreateAuthRequest(
                HttpMethod.Post,
                $"{config.NormalizedUrl}/auth/v1/token?grant_type=password",
                config.AnonKey,
                JsonSerializer.Serialize(payload, SupabaseJsonContext.Default.SignInRequest));

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ExtractHungarianAuthError(body, "Sikertelen bejelentkezés.");
                return new AuthResult(false, ErrorMessage: errorMessage);
            }

            var authData = JsonSerializer.Deserialize(body, SupabaseJsonContext.Default.AuthTokenResponse);
            var session = MapToSession(authData, trimmedUser);
            if (session is null)
            {
                return new AuthResult(false, ErrorMessage: "A szerver válasza nem tartalmazott érvényes munkamenetet.");
            }

            await SetSessionAsync(session);
            await EnsureProfileAndRoleAsync(session, ct);
            return new AuthResult(true, Session: CurrentUser ?? session);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt bejelentkezés közben.");
            return new AuthResult(false, ErrorMessage: "Hálózati hiba történt a bejelentkezés során. Ellenőrizd az internetkapcsolatot.");
        }
    }

    public async Task<AuthResult> SignUpAsync(string felhasznalonev, string password, CancellationToken ct = default)
    {
        var config = await _configService.GetConfigAsync(ct);
        if (!config.IsConfigured)
        {
            return new AuthResult(false, ErrorMessage: "Nincs megadva a Supabase Project URL és az Anon kulcs.");
        }

        var trimmedUser = felhasznalonev.Trim();

        try
        {
            var payload = new SignUpRequest
            {
                Email = ToInternalEmail(trimmedUser),
                Password = password,
                Data = new SignUpUserData { Felhasznalonev = trimmedUser }
            };

            using var request = CreateAuthRequest(
                HttpMethod.Post,
                $"{config.NormalizedUrl}/auth/v1/signup",
                config.AnonKey,
                JsonSerializer.Serialize(payload, SupabaseJsonContext.Default.SignUpRequest));

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ExtractHungarianAuthError(body, "Sikertelen regisztráció.");
                return new AuthResult(false, ErrorMessage: errorMessage);
            }

            var authData = JsonSerializer.Deserialize(body, SupabaseJsonContext.Default.AuthTokenResponse);
            var session = MapToSession(authData, trimmedUser);

            // Ha a Supabase projektben még be van kapcsolva a "Confirm email", nem ad azonnali tokent
            if (session is null)
            {
                return new AuthResult(
                    Success: false,
                    RequiresEmailConfirmation: true,
                    ErrorMessage: "A Supabase projektben még be van kapcsolva a 'Confirm email' opció. Kapcsold ki a Supabase Dashboard → Authentication → Providers → Email menüben, majd próbáld újra!");
            }

            await SetSessionAsync(session);
            await EnsureProfileAndRoleAsync(session, ct);
            return new AuthResult(true, Session: CurrentUser ?? session);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt regisztráció közben.");
            return new AuthResult(false, ErrorMessage: "Hálózati hiba történt a regisztráció során. Ellenőrizd az internetkapcsolatot.");
        }
    }

    public async Task<AuthResult> UpdateProfileAsync(string? megjelenitettNev, string? profilkepUrl, CancellationToken ct = default)
    {
        var session = CurrentUser;
        if (session is null || session.UserId == Guid.Empty)
        {
            return new AuthResult(false, ErrorMessage: "Nincs bejelentkezett felhasználó.");
        }

        var trimmedName = string.IsNullOrWhiteSpace(megjelenitettNev) ? null : megjelenitettNev.Trim();
        var trimmedPhoto = string.IsNullOrWhiteSpace(profilkepUrl) ? null : profilkepUrl.Trim();

        if (trimmedName is not null && (trimmedName.Length < 2 || trimmedName.Length > 40))
        {
            return new AuthResult(false, ErrorMessage: "A megjelenített név 2 és 40 karakter között legyen.");
        }

        var config = await _configService.GetConfigAsync(ct);
        var token = await GetValidAccessTokenAsync(ct);
        if (!config.IsConfigured || string.IsNullOrWhiteSpace(token))
        {
            return new AuthResult(false, ErrorMessage: "A munkamenet lejárt vagy nincs internetkapcsolat.");
        }

        try
        {
            var payload = new ProfileUpdateDto
            {
                MegjelenitettNev = trimmedName,
                ProfilkepUrl = trimmedPhoto
            };

            var json = JsonSerializer.Serialize(payload, SupabaseJsonContext.Default.ProfileUpdateDto);
            using var req = new HttpRequestMessage(HttpMethod.Patch, $"{config.NormalizedUrl}/rest/v1/profiles?id=eq.{session.UserId}");
            req.Headers.Add("apikey", config.AnonKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Prefer", "return=minimal");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var errBody = await res.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Profil mentési hiba ({StatusCode}): {Body}", (int)res.StatusCode, errBody);
                return new AuthResult(false, ErrorMessage: "Nem sikerült menteni a profilt. Futtasd le a Supabase SQL Editorban az add_user_profiles.sql fájlt!");
            }

            // A meglévő túrák résztvevői között is frissítjük a megjelenített nevet ennél a felhasználónál
            var effectiveName = trimmedName ?? session.Felhasznalonev;
            try
            {
                var nevPatch = JsonSerializer.Serialize(
                    new ResztvevoNevPatchDto { Nev = effectiveName },
                    SupabaseJsonContext.Default.ResztvevoNevPatchDto);

                using var participantReq = new HttpRequestMessage(
                    HttpMethod.Patch,
                    $"{config.NormalizedUrl}/rest/v1/resztvevok?user_id=eq.{session.UserId}");
                participantReq.Headers.Add("apikey", config.AnonKey);
                participantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                participantReq.Headers.Add("Prefer", "return=minimal");
                participantReq.Content = new StringContent(nevPatch, Encoding.UTF8, "application/json");
                using var _ = await _httpClient.SendAsync(participantReq, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "A résztvevői név utólagos frissítése kihagyva.");
            }

            await UpdateSessionProfileAsync(session, session.IsAdmin, trimmedName, trimmedPhoto);
            return new AuthResult(true, Session: CurrentUser);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a profil frissítése közben.");
            return new AuthResult(false, ErrorMessage: "Hálózati hiba történt a profil mentése során.");
        }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await _configService.GetConfigAsync(ct);
            var token = CurrentUser?.AccessToken;

            if (config.IsConfigured && !string.IsNullOrWhiteSpace(token))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.NormalizedUrl}/auth/v1/logout");
                request.Headers.Add("apikey", config.AnonKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                await _httpClient.SendAsync(request, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "A szerveroldali kijelentkezés nem sikerült, helyi munkamenet törlése folyamatban.");
        }
        finally
        {
            await _tokenGate.WaitAsync(CancellationToken.None);
            try
            {
                await ClearSessionInternalAsync();
            }
            finally
            {
                _tokenGate.Release();
            }

            AuthStateChanged?.Invoke(null);
        }
    }

    private async Task<bool> RefreshSessionInternalAsync(CancellationToken ct)
    {
        if (CurrentUser is null || string.IsNullOrWhiteSpace(CurrentUser.RefreshToken))
        {
            await ClearSessionInternalAsync();
            return false;
        }

        var config = await _configService.GetConfigAsync(ct);
        if (!config.IsConfigured)
        {
            return false;
        }

        try
        {
            var payload = new RefreshTokenRequest { RefreshToken = CurrentUser.RefreshToken };
            using var request = CreateAuthRequest(
                HttpMethod.Post,
                $"{config.NormalizedUrl}/auth/v1/token?grant_type=refresh_token",
                config.AnonKey,
                JsonSerializer.Serialize(payload, SupabaseJsonContext.Default.RefreshTokenRequest));

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                await ClearSessionInternalAsync();
                AuthStateChanged?.Invoke(null);
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var authData = JsonSerializer.Deserialize(body, SupabaseJsonContext.Default.AuthTokenResponse);
            var refreshed = MapToSession(authData, CurrentUser.Felhasznalonev);

            if (refreshed is null)
            {
                await ClearSessionInternalAsync();
                AuthStateChanged?.Invoke(null);
                return false;
            }

            var previousIsAdmin = CurrentUser.IsAdmin;
            var previousMegjelenitettNev = CurrentUser.MegjelenitettNev;
            var previousProfilkepUrl = CurrentUser.ProfilkepUrl;
            CurrentUser = refreshed;
            CurrentUser.IsAdmin = previousIsAdmin;
            CurrentUser.MegjelenitettNev = previousMegjelenitettNev;
            CurrentUser.ProfilkepUrl = previousProfilkepUrl;
            await WriteSessionJsonAsync(JsonSerializer.Serialize(CurrentUser, SupabaseJsonContext.Default.UserSession));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "A munkamenet megújítása sikertelen volt.");
            return false;
        }
    }

    private async Task SetSessionAsync(UserSession session)
    {
        await _tokenGate.WaitAsync();
        try
        {
            CurrentUser = session;
            var json = JsonSerializer.Serialize(session, SupabaseJsonContext.Default.UserSession);
            await WriteSessionJsonAsync(json);
        }
        finally
        {
            _tokenGate.Release();
        }

        AuthStateChanged?.Invoke(session);
    }

    /// <summary>
    /// Biztosítja, hogy a bejelentkezett felhasználó szerepeljen a public.profiles táblában,
    /// valamint betölti a Supabase-ből az is_admin jogosultságát.
    /// </summary>
    private async Task EnsureProfileAndRoleAsync(UserSession session, CancellationToken ct)
    {
        if (session.UserId == Guid.Empty || string.IsNullOrWhiteSpace(session.AccessToken) || string.IsNullOrWhiteSpace(session.Felhasznalonev))
        {
            return;
        }

        var fallbackAdmin = IsDefaultAdminName(session.Felhasznalonev);

        try
        {
            var config = await _configService.GetConfigAsync(ct);
            if (!config.IsConfigured)
            {
                await UpdateSessionProfileAsync(session, fallbackAdmin, session.MegjelenitettNev, session.ProfilkepUrl);
                return;
            }

            // 1. Profil sor biztosítása (csak id + felhasznalonev, így az is_admin értéket nem írja felül)
            var dto = new ProfileDto
            {
                Id = session.UserId,
                Felhasznalonev = session.Felhasznalonev.Trim()
            };

            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.ProfileDto);
            using (var upsertReq = new HttpRequestMessage(HttpMethod.Post, $"{config.NormalizedUrl}/rest/v1/profiles?on_conflict=id"))
            {
                upsertReq.Headers.Add("apikey", config.AnonKey);
                upsertReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                upsertReq.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
                upsertReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var upsertRes = await _httpClient.SendAsync(upsertReq, ct);
                if (!upsertRes.IsSuccessStatusCode)
                {
                    _logger.LogDebug("A profil szinkronizálása nem sikerült ({StatusCode}).", (int)upsertRes.StatusCode);
                }
            }

            // 2. Profil adatok és is_admin jogosultság kiolvasása a Supabase public.profiles táblából
            using var roleReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"{config.NormalizedUrl}/rest/v1/profiles?id=eq.{session.UserId}&select=id,felhasznalonev,is_admin,megjelenitett_nev,profilkep_url&limit=1");
            roleReq.Headers.Add("apikey", config.AnonKey);
            roleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var roleRes = await _httpClient.SendAsync(roleReq, ct);
            if (roleRes.IsSuccessStatusCode)
            {
                var roleBody = await roleRes.Content.ReadAsStringAsync(ct);
                var rows = JsonSerializer.Deserialize(roleBody, SupabaseJsonContext.Default.ListProfileWithRoleDto);
                var profile = rows?.FirstOrDefault();
                if (profile is not null)
                {
                    await UpdateSessionProfileAsync(session, profile.IsAdmin, profile.MegjelenitettNev, profile.ProfilkepUrl);
                    return;
                }
            }

            // Tartalék lekérdezés, ha a megjelenitett_nev / profilkep_url oszlop még nincs lefuttatva
            using var fallbackReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"{config.NormalizedUrl}/rest/v1/profiles?id=eq.{session.UserId}&select=id,felhasznalonev,is_admin&limit=1");
            fallbackReq.Headers.Add("apikey", config.AnonKey);
            fallbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var fallbackRes = await _httpClient.SendAsync(fallbackReq, ct);
            if (fallbackRes.IsSuccessStatusCode)
            {
                var fbBody = await fallbackRes.Content.ReadAsStringAsync(ct);
                var fbRows = JsonSerializer.Deserialize(fbBody, SupabaseJsonContext.Default.ListProfileWithRoleDto);
                var fbProfile = fbRows?.FirstOrDefault();
                if (fbProfile is not null)
                {
                    await UpdateSessionProfileAsync(session, fbProfile.IsAdmin, session.MegjelenitettNev, session.ProfilkepUrl);
                    return;
                }
            }

            await UpdateSessionProfileAsync(session, fallbackAdmin, session.MegjelenitettNev, session.ProfilkepUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "A profil / jogosultság ellenőrzése offline állapot miatt alapértelmezettre állt.");
            await UpdateSessionProfileAsync(session, session.IsAdmin || fallbackAdmin, session.MegjelenitettNev, session.ProfilkepUrl);
        }
    }

    private async Task UpdateSessionProfileAsync(
        UserSession session,
        bool isAdmin,
        string? megjelenitettNev,
        string? profilkepUrl)
    {
        var unchanged =
            session.IsAdmin == isAdmin &&
            string.Equals(session.MegjelenitettNev, megjelenitettNev, StringComparison.Ordinal) &&
            string.Equals(session.ProfilkepUrl, profilkepUrl, StringComparison.Ordinal) &&
            CurrentUser?.IsAdmin == isAdmin &&
            string.Equals(CurrentUser?.MegjelenitettNev, megjelenitettNev, StringComparison.Ordinal) &&
            string.Equals(CurrentUser?.ProfilkepUrl, profilkepUrl, StringComparison.Ordinal);

        if (unchanged)
        {
            return;
        }

        session.IsAdmin = isAdmin;
        session.MegjelenitettNev = megjelenitettNev;
        session.ProfilkepUrl = profilkepUrl;

        if (CurrentUser is not null && CurrentUser.UserId == session.UserId)
        {
            CurrentUser.IsAdmin = isAdmin;
            CurrentUser.MegjelenitettNev = megjelenitettNev;
            CurrentUser.ProfilkepUrl = profilkepUrl;
            var json = JsonSerializer.Serialize(CurrentUser, SupabaseJsonContext.Default.UserSession);
            await WriteSessionJsonAsync(json);
            AuthStateChanged?.Invoke(CurrentUser);
        }
    }

    private static bool IsDefaultAdminName(string? felhasznalonev) =>
        string.Equals(felhasznalonev?.Trim(), DefaultAdminUsername, StringComparison.OrdinalIgnoreCase);

    private Task ClearSessionInternalAsync()
    {
        CurrentUser = null;
        try
        {
            SecureStorage.Default.Remove(SessionStorageKey);
        }
        catch
        {
            // Ignored on unpackaged Windows
        }

        Preferences.Default.Remove(SessionStorageKey);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A Supabase Auth technikai azonosítóját képezi a felhasználónévből; a felhasználó sosem lát e-mail címet.
    /// </summary>
    private static string ToInternalEmail(string felhasznalonev) =>
        $"{felhasznalonev.Trim().ToLowerInvariant()}@{InternalEmailDomain}";

    private static HttpRequestMessage CreateAuthRequest(HttpMethod method, string url, string anonKey, string jsonBody)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("apikey", anonKey);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return request;
    }

    private static UserSession? MapToSession(AuthTokenResponse? response, string fallbackUsername)
    {
        if (response is null ||
            string.IsNullOrWhiteSpace(response.AccessToken) ||
            string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            return null;
        }

        var username = response.User?.UserMetadata?.Felhasznalonev;
        if (string.IsNullOrWhiteSpace(username))
        {
            var rawEmail = response.User?.Email ?? response.Email;
            username = !string.IsNullOrWhiteSpace(fallbackUsername)
                ? fallbackUsername.Trim()
                : (rawEmail?.Split('@')[0] ?? "Horgász");
        }

        var expiresIn = response.ExpiresIn > 0 ? response.ExpiresIn : 3600;

        return new UserSession
        {
            UserId = response.User?.Id ?? response.Id ?? Guid.Empty,
            Felhasznalonev = username,
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            IsAdmin = IsDefaultAdminName(username)
        };
    }

    private static string ExtractHungarianAuthError(string responseBody, string fallback)
    {
        try
        {
            var err = JsonSerializer.Deserialize(responseBody, SupabaseJsonContext.Default.AuthErrorResponse);
            var raw = err?.ErrorDescription ?? err?.Msg ?? err?.Message ?? err?.Error ?? string.Empty;

            if (raw.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
            {
                return "Hibás felhasználónév vagy jelszó.";
            }

            if (raw.Contains("Email not confirmed", StringComparison.OrdinalIgnoreCase))
            {
                return "A Supabase projektben kapcsold ki a 'Confirm email' beállítást (Authentication → Providers → Email)!";
            }

            if (raw.Contains("User already registered", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("already been registered", StringComparison.OrdinalIgnoreCase))
            {
                return "Ez a felhasználónév már foglalt. Válassz másikat!";
            }

            if (raw.Contains("Password should be at least", StringComparison.OrdinalIgnoreCase))
            {
                return "A jelszónak legalább 6 karakter hosszúnak kell lennie.";
            }

            if (raw.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                return "Túl sok próbálkozás rövid időn belül. Próbáld meg kicsit később.";
            }

            return string.IsNullOrWhiteSpace(raw) ? fallback : $"{fallback} ({raw})";
        }
        catch
        {
            return fallback;
        }
    }

    private async Task<string?> ReadSessionJsonAsync()
    {
        try
        {
            var val = await SecureStorage.Default.GetAsync(SessionStorageKey);
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or PlatformNotSupportedException)
        {
            _logger.LogDebug(ex, "SecureStorage olvasás nem elérhető, Preferences tartalék használata.");
        }

        return Preferences.Default.Get(SessionStorageKey, string.Empty);
    }

    private async Task WriteSessionJsonAsync(string json)
    {
        try
        {
            await SecureStorage.Default.SetAsync(SessionStorageKey, json);
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or PlatformNotSupportedException)
        {
            _logger.LogDebug(ex, "SecureStorage írás nem elérhető, Preferences tartalék használata.");
        }

        Preferences.Default.Set(SessionStorageKey, json);
    }
}
