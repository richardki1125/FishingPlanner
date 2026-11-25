using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FishingPlanner.Models;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// A Supabase PostgreSQL (PostgREST /rest/v1) adatbázissal kommunikáló ITuraService implementáció.
/// </summary>
public sealed class SupabaseTuraService : ITuraService
{
    private readonly HttpClient _httpClient;
    private readonly ISupabaseConfigService _configService;
    private readonly IAuthService _authService;
    private readonly IMediaService _mediaService;
    private readonly ILogger<SupabaseTuraService> _logger;

    public SupabaseTuraService(
        HttpClient httpClient,
        ISupabaseConfigService configService,
        IAuthService authService,
        IMediaService mediaService,
        ILogger<SupabaseTuraService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // --- Túrák ---

    public async Task<IReadOnlyList<Tura>> GetTurakAsync(CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                "turak?select=*&order=kezdo_datum.desc",
                SupabaseJsonContext.Default.ListTuraDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a túrák lekérdezésekor a Supabase-ből.");
            throw;
        }
    }

    public async Task<Tura?> GetTuraByIdAsync(Guid turaId, CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                $"turak?id=eq.{turaId}&select=*&limit=1",
                SupabaseJsonContext.Default.ListTuraDto,
                ct);

            var dto = dtos?.FirstOrDefault();
            return dto is null ? null : MapToDomain(dto);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra lekérdezésekor.", turaId);
            throw;
        }
    }

    public async Task<Tura> CreateTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tura);

        try
        {
            var dto = MapToDto(tura);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.TuraDto);

            var created = await SendAndReadAsync(
                HttpMethod.Post,
                "turak?select=*",
                SupabaseJsonContext.Default.ListTuraDto,
                ct,
                json,
                preferRepresentation: true);

            return created is { Count: > 0 }
                ? MapToDomain(created[0])
                : throw new InvalidOperationException("A létrehozott túra nem olvasható vissza.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt új túra mentésekor: {Nev}", tura.Nev);
            throw;
        }
    }

    public async Task<Tura> UpdateTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tura);

        try
        {
            var dto = MapToDto(tura);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.TuraDto);

            var updated = await SendAndReadAsync(
                HttpMethod.Patch,
                $"turak?id=eq.{tura.Id}&select=*",
                SupabaseJsonContext.Default.ListTuraDto,
                ct,
                json,
                preferRepresentation: true);

            return updated is { Count: > 0 }
                ? MapToDomain(updated[0])
                : throw new KeyNotFoundException($"A frissíteni kívánt túra ({tura.Id}) nem található.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra frissítésekor.", tura.Id);
            throw;
        }
    }

    public async Task DeleteTuraAsync(Guid turaId, CancellationToken ct = default)
    {
        try
        {
            // Túra törlése előtt a hozzá tartozó fogásfotók takarítása a Storage-ból
            try
            {
                var fogasok = await GetFogasokAsync(turaId, ct);
                foreach (var fogas in fogasok.Where(f => !string.IsNullOrWhiteSpace(f.KepUrl)))
                {
                    await _mediaService.DeletePhotoAsync(fogas.KepUrl, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "A túrához tartozó fotók takarítása részben sikertelen: {TuraId}", turaId);
            }

            await SendNoContentAsync(HttpMethod.Delete, $"turak?id=eq.{turaId}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra törlésekor.", turaId);
            throw;
        }
    }

    // --- Idempotens Upsert metódusok (offline szinkronizáláshoz és biztonsági mentés visszaállításához) ---

    public async Task<Tura> UpsertTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tura);
        var dto = MapToDto(tura);
        var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.TuraDto);

        var result = await SendAndReadAsync(
            HttpMethod.Post,
            "turak?on_conflict=id&select=*",
            SupabaseJsonContext.Default.ListTuraDto,
            ct,
            json,
            preferHeader: "resolution=merge-duplicates,return=representation");

        return result is { Count: > 0 } ? MapToDomain(result[0]) : tura;
    }

    public async Task<Resztvevo> UpsertResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resztvevo);
        var dto = MapToDto(resztvevo);
        var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.ResztvevoDto);

        var result = await SendAndReadAsync(
            HttpMethod.Post,
            "resztvevok?on_conflict=id&select=*",
            SupabaseJsonContext.Default.ListResztvevoDto,
            ct,
            json,
            preferHeader: "resolution=merge-duplicates,return=representation");

        return result is { Count: > 0 } ? MapToDomain(result[0]) : resztvevo;
    }

    public async Task<FelszerelesTetel> UpsertFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);
        var dto = MapToDto(tetel);
        var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.FelszerelesTetelDto);

        var result = await SendAndReadAsync(
            HttpMethod.Post,
            "felszereles_tetelek?on_conflict=id&select=*",
            SupabaseJsonContext.Default.ListFelszerelesTetelDto,
            ct,
            json,
            preferHeader: "resolution=merge-duplicates,return=representation");

        return result is { Count: > 0 } ? MapToDomain(result[0]) : tetel;
    }

    public async Task<Fogas> UpsertFogasAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);
        var dto = MapToDto(fogas);
        var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.FogasDto);

        var result = await SendAndReadAsync(
            HttpMethod.Post,
            "fogasok?on_conflict=id&select=*",
            SupabaseJsonContext.Default.ListFogasDto,
            ct,
            json,
            preferHeader: "resolution=merge-duplicates,return=representation");

        return result is { Count: > 0 } ? MapToDomain(result[0]) : fogas;
    }

    public async Task<KoltsegTetel> UpsertKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);
        var dto = MapToDto(tetel);
        var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.KoltsegTetelDto);

        try
        {
            var result = await SendAndReadAsync(
                HttpMethod.Post,
                "koltseg_tetelek?on_conflict=id&select=*",
                SupabaseJsonContext.Default.ListKoltsegTetelDto,
                ct,
                json,
                preferHeader: "resolution=merge-duplicates,return=representation");

            return result is { Count: > 0 } ? MapToDomain(result[0]) : tetel;
        }
        catch (InvalidOperationException ex) when (IsMissingKoltsegTableException(ex))
        {
            throw CreateMissingKoltsegTableException(ex);
        }
    }

    public async Task<CsomaglistaSablon> UpsertCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sablon);
        var dto = MapToDto(sablon);
        var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.CsomaglistaSablonDto);

        try
        {
            var result = await SendAndReadAsync(
                HttpMethod.Post,
                "csomaglista_sablonok?on_conflict=id&select=*",
                SupabaseJsonContext.Default.ListCsomaglistaSablonDto,
                ct,
                json,
                preferHeader: "resolution=merge-duplicates,return=representation");

            return result is { Count: > 0 } ? MapToDomain(result[0]) : sablon;
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
    }

    public async Task<CsomaglistaSablonTetel> UpsertCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);
        var dto = MapToDto(tetel);
        var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.CsomaglistaSablonTetelDto);

        try
        {
            var result = await SendAndReadAsync(
                HttpMethod.Post,
                "csomaglista_sablon_tetelek?on_conflict=id&select=*",
                SupabaseJsonContext.Default.ListCsomaglistaSablonTetelDto,
                ct,
                json,
                preferHeader: "resolution=merge-duplicates,return=representation");

            return result is { Count: > 0 } ? MapToDomain(result[0]) : tetel;
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
    }

    // --- Regisztrált felhasználók ---

    public async Task<IReadOnlyList<RegisztraltFelhasznalo>> GetRegisztraltFelhasznalokAsync(CancellationToken ct = default)
    {
        try
        {
            try
            {
                var dtos = await SendAndReadAsync(
                    HttpMethod.Get,
                    "profiles?select=id,felhasznalonev,megjelenitett_nev,profilkep_url&order=felhasznalonev.asc",
                    SupabaseJsonContext.Default.ListProfileListItemDto,
                    ct);

                return dtos?
                    .Select(p => new RegisztraltFelhasznalo(p.Id, p.Felhasznalonev, p.MegjelenitettNev, p.ProfilkepUrl))
                    .ToList() ?? [];
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("column", StringComparison.OrdinalIgnoreCase))
            {
                // Tartalék, ha a megjelenitett_nev / profilkep_url oszlopok még nincsenek lefuttatva
                var fallbackDtos = await SendAndReadAsync(
                    HttpMethod.Get,
                    "profiles?select=id,felhasznalonev&order=felhasznalonev.asc",
                    SupabaseJsonContext.Default.ListProfileDto,
                    ct);

                return fallbackDtos?
                    .Select(p => new RegisztraltFelhasznalo(p.Id, p.Felhasznalonev))
                    .ToList() ?? [];
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a regisztrált felhasználók lekérdezésekor.");
            throw;
        }
    }

    // --- Hal főkategóriák és alkategóriák ---

    public async Task<IReadOnlyList<HalKategoriaCsoport>> GetHalKategoriakAsync(CancellationToken ct = default)
    {
        try
        {
            var katTask = SendAndReadAsync(
                HttpMethod.Get,
                "hal_kategoriak?select=id,nev,sorrend&order=sorrend.asc,nev.asc",
                SupabaseJsonContext.Default.ListHalKategoriaDto,
                ct);

            var alTask = SendAndReadAsync(
                HttpMethod.Get,
                "hal_alkategoriak?select=id,kategoria_id,nev,sorrend&order=sorrend.asc,nev.asc",
                SupabaseJsonContext.Default.ListHalAlkategoriaDto,
                ct);

            await Task.WhenAll(katTask, alTask);

            var katDtos = await katTask ?? [];
            var alDtos = await alTask ?? [];

            var kategoriak = katDtos
                .Select(k => new HalKategoria { Id = k.Id, Nev = k.Nev, Sorrend = k.Sorrend })
                .ToList();

            var alkategoriak = alDtos
                .Select(a => new HalAlkategoria { Id = a.Id, KategoriaId = a.KategoriaId, Nev = a.Nev, Sorrend = a.Sorrend })
                .ToList();

            return HalKatalogusDefaults.BuildCsoportok(kategoriak, alkategoriak);
        }
        catch (InvalidOperationException ex)
        {
            // Ha a hal_kategoriak tábla még nincs lefuttatva a Supabase-ben, biztonságos alapértelmezett listát adunk
            _logger.LogWarning(ex, "A hal_kategoriak / hal_alkategoriak tábla még nem érhető el a Supabase-ben, alapértelmezett katalógus használata.");
            return HalKatalogusDefaults.GetDefaultCsoportok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a hal kategóriák lekérdezésekor.");
            throw;
        }
    }

    // --- Résztvevők ---

    public async Task<IReadOnlyList<Resztvevo>> GetResztvevokAsync(Guid turaId, CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                $"resztvevok?tura_id=eq.{turaId}&select=*&order=nev.asc",
                SupabaseJsonContext.Default.ListResztvevoDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra résztvevőinek lekérdezésekor.", turaId);
            throw;
        }
    }

    public async Task<Resztvevo> AddResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resztvevo);

        try
        {
            // Ha ez a felhasználó korábban már résztvevő volt ebben a túrában (aktiv_e = false),
            // újraaktiváljuk a meglévő rekordját, így az összes korábbi fogása és felszerelése visszakapcsolódik!
            if (resztvevo.UserId.HasValue && resztvevo.UserId.Value != Guid.Empty)
            {
                var existing = await SendAndReadAsync(
                    HttpMethod.Get,
                    $"resztvevok?tura_id=eq.{resztvevo.TuraId}&user_id=eq.{resztvevo.UserId.Value}&select=*&limit=1",
                    SupabaseJsonContext.Default.ListResztvevoDto,
                    ct);

                var korabbi = existing?.FirstOrDefault();
                if (korabbi is not null)
                {
                    var patch = new ResztvevoReaktivPatchDto
                    {
                        AktivE = true,
                        Nev = resztvevo.Nev.Trim(),
                        Szerepkor = resztvevo.Szerepkor.Trim()
                    };
                    var patchJson = JsonSerializer.Serialize(patch, SupabaseJsonContext.Default.ResztvevoReaktivPatchDto);
                    var reactivated = await SendAndReadAsync(
                        HttpMethod.Patch,
                        $"resztvevok?id=eq.{korabbi.Id}&select=*",
                        SupabaseJsonContext.Default.ListResztvevoDto,
                        ct,
                        patchJson,
                        preferRepresentation: true);

                    return reactivated is { Count: > 0 }
                        ? MapToDomain(reactivated[0])
                        : MapToDomain(korabbi);
                }
            }

            resztvevo.AktivE = true;
            var dto = MapToDto(resztvevo);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.ResztvevoDto);

            var created = await SendAndReadAsync(
                HttpMethod.Post,
                "resztvevok?select=*",
                SupabaseJsonContext.Default.ListResztvevoDto,
                ct,
                json,
                preferRepresentation: true);

            return created is { Count: > 0 }
                ? MapToDomain(created[0])
                : throw new InvalidOperationException("A résztvevő mentése nem tért vissza adattal.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt résztvevő hozzáadásakor: {Nev}", resztvevo.Nev);
            throw;
        }
    }

    public async Task<Resztvevo> UpdateResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resztvevo);

        try
        {
            var dto = MapToDto(resztvevo);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.ResztvevoDto);

            var updated = await SendAndReadAsync(
                HttpMethod.Patch,
                $"resztvevok?id=eq.{resztvevo.Id}&select=*",
                SupabaseJsonContext.Default.ListResztvevoDto,
                ct,
                json,
                preferRepresentation: true);

            return updated is { Count: > 0 }
                ? MapToDomain(updated[0])
                : throw new KeyNotFoundException($"A résztvevő ({resztvevo.Id}) nem található.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {ResztvevoId} résztvevő frissítésekor.", resztvevo.Id);
            throw;
        }
    }

    public async Task DeleteResztvevoAsync(Guid resztvevoId, CancellationToken ct = default)
    {
        try
        {
            // Adatmegőrző eltávolítás (Soft-Delete): NEM töröljük a sort, csak inaktiváljuk,
            // így a résztvevő összes korábbi fogása és felszerelése sértetlenül megmarad!
            var patchJson = JsonSerializer.Serialize(
                new ResztvevoAktivPatchDto { AktivE = false },
                SupabaseJsonContext.Default.ResztvevoAktivPatchDto);

            await SendNoContentAsync(HttpMethod.Patch, $"resztvevok?id=eq.{resztvevoId}", ct, patchJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {ResztvevoId} résztvevő eltávolításakor.", resztvevoId);
            throw;
        }
    }

    // --- Felszerelések ---

    public async Task<IReadOnlyList<FelszerelesTetel>> GetFelszerelesekAsync(Guid turaId, CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                $"felszereles_tetelek?tura_id=eq.{turaId}&select=*&order=kozos_e.desc,kategoria.asc,nev.asc",
                SupabaseJsonContext.Default.ListFelszerelesTetelDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra felszereléseinek lekérdezésekor.", turaId);
            throw;
        }
    }

    public async Task<FelszerelesTetel> AddFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        try
        {
            var dto = MapToDto(tetel);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.FelszerelesTetelDto);

            var created = await SendAndReadAsync(
                HttpMethod.Post,
                "felszereles_tetelek?select=*",
                SupabaseJsonContext.Default.ListFelszerelesTetelDto,
                ct,
                json,
                preferRepresentation: true);

            return created is { Count: > 0 }
                ? MapToDomain(created[0])
                : throw new InvalidOperationException("A felszerelés mentése nem tért vissza adattal.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt felszerelés hozzáadásakor: {Nev}", tetel.Nev);
            throw;
        }
    }

    public async Task<FelszerelesTetel> UpdateFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        try
        {
            var dto = MapToDto(tetel);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.FelszerelesTetelDto);

            var updated = await SendAndReadAsync(
                HttpMethod.Patch,
                $"felszereles_tetelek?id=eq.{tetel.Id}&select=*",
                SupabaseJsonContext.Default.ListFelszerelesTetelDto,
                ct,
                json,
                preferRepresentation: true);

            return updated is { Count: > 0 }
                ? MapToDomain(updated[0])
                : throw new KeyNotFoundException($"A felszerelési tétel ({tetel.Id}) nem található.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} felszerelés frissítésekor.", tetel.Id);
            throw;
        }
    }

    public async Task ToggleBepakolvaAsync(Guid tetelId, bool bepakolva, CancellationToken ct = default)
    {
        try
        {
            var patch = new FelszerelesBepakolvaPatchDto { Bepakolva = bepakolva };
            var json = JsonSerializer.Serialize(patch, SupabaseJsonContext.Default.FelszerelesBepakolvaPatchDto);

            await SendNoContentAsync(
                HttpMethod.Patch,
                $"felszereles_tetelek?id=eq.{tetelId}",
                ct,
                json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} felszerelés állapotának frissítésekor.", tetelId);
            throw;
        }
    }

    public async Task DeleteFelszerelesAsync(Guid tetelId, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(HttpMethod.Delete, $"felszereles_tetelek?id=eq.{tetelId}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} felszerelés törlésekor.", tetelId);
            throw;
        }
    }

    // --- Fogások ---

    public async Task<IReadOnlyList<Fogas>> GetFogasokAsync(Guid turaId, CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                $"fogasok?tura_id=eq.{turaId}&select=*&order=idopont.desc",
                SupabaseJsonContext.Default.ListFogasDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra fogásainak lekérdezésekor.", turaId);
            throw;
        }
    }

    public async Task<Fogas> AddFogasAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);

        try
        {
            var dto = MapToDto(fogas);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.FogasDto);

            var created = await SendAndReadAsync(
                HttpMethod.Post,
                "fogasok?select=*",
                SupabaseJsonContext.Default.ListFogasDto,
                ct,
                json,
                preferRepresentation: true);

            return created is { Count: > 0 }
                ? MapToDomain(created[0])
                : throw new InvalidOperationException("A fogás mentése nem tért vissza adattal.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt új fogás rögzítésekor: {Halfaj}", fogas.Halfaj);
            throw;
        }
    }

    public async Task<Fogas> UpdateFogasAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);

        try
        {
            var dto = MapToDto(fogas);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.FogasDto);

            var updated = await SendAndReadAsync(
                HttpMethod.Patch,
                $"fogasok?id=eq.{fogas.Id}&select=*",
                SupabaseJsonContext.Default.ListFogasDto,
                ct,
                json,
                preferRepresentation: true);

            return updated is { Count: > 0 }
                ? MapToDomain(updated[0])
                : throw new KeyNotFoundException($"A fogás ({fogas.Id}) nem található.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {FogasId} fogás frissítésekor.", fogas.Id);
            throw;
        }
    }

    public async Task DeleteFogasAsync(Guid fogasId, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(HttpMethod.Delete, $"fogasok?id=eq.{fogasId}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {FogasId} fogás törlésekor.", fogasId);
            throw;
        }
    }

    // --- Költségek ---

    public async Task<IReadOnlyList<KoltsegTetel>> GetKoltsegekAsync(Guid turaId, CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                $"koltseg_tetelek?tura_id=eq.{turaId}&select=*&order=kozos_e.desc,created_at.asc",
                SupabaseJsonContext.Default.ListKoltsegTetelDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (InvalidOperationException ex) when (IsMissingKoltsegTableException(ex))
        {
            throw CreateMissingKoltsegTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra költségeinek lekérdezésekor.", turaId);
            throw;
        }
    }

    public async Task<KoltsegTetel> AddKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        try
        {
            var dto = MapToDto(tetel);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.KoltsegTetelDto);

            var created = await SendAndReadAsync(
                HttpMethod.Post,
                "koltseg_tetelek?select=*",
                SupabaseJsonContext.Default.ListKoltsegTetelDto,
                ct,
                json,
                preferRepresentation: true);

            return created is { Count: > 0 }
                ? MapToDomain(created[0])
                : throw new InvalidOperationException("A költségmező mentése nem tért vissza adattal.");
        }
        catch (InvalidOperationException ex) when (IsMissingKoltsegTableException(ex))
        {
            throw CreateMissingKoltsegTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt költségmező hozzáadásakor: {Nev}", tetel.Nev);
            throw;
        }
    }

    public async Task<KoltsegTetel> UpdateKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        try
        {
            var dto = MapToDto(tetel);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.KoltsegTetelDto);

            var updated = await SendAndReadAsync(
                HttpMethod.Patch,
                $"koltseg_tetelek?id=eq.{tetel.Id}&select=*",
                SupabaseJsonContext.Default.ListKoltsegTetelDto,
                ct,
                json,
                preferRepresentation: true);

            return updated is { Count: > 0 }
                ? MapToDomain(updated[0])
                : throw new KeyNotFoundException($"A költségmező ({tetel.Id}) nem található.");
        }
        catch (InvalidOperationException ex) when (IsMissingKoltsegTableException(ex))
        {
            throw CreateMissingKoltsegTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {KoltsegId} költségmező frissítésekor.", tetel.Id);
            throw;
        }
    }

    public async Task DeleteKoltsegAsync(Guid koltsegId, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(HttpMethod.Delete, $"koltseg_tetelek?id=eq.{koltsegId}", ct);
        }
        catch (InvalidOperationException ex) when (IsMissingKoltsegTableException(ex))
        {
            throw CreateMissingKoltsegTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {KoltsegId} költségmező törlésekor.", koltsegId);
            throw;
        }
    }

    private static bool IsMissingKoltsegTableException(InvalidOperationException ex) =>
        ex.Message.Contains("koltseg_tetelek", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Could not find the table", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException CreateMissingKoltsegTableException(InvalidOperationException inner) =>
        new("A költségek tábla még nincs létrehozva az adatbázisban. Futtasd le a supabase/add_koltsegek.sql fájlt a Supabase SQL Editorban!", inner);

    // --- Újrahasználható csomaglisták (sablonok) ---

    public async Task<IReadOnlyList<CsomaglistaSablon>> GetCsomaglistaSablonokAsync(CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                "csomaglista_sablonok?select=*&order=nev.asc",
                SupabaseJsonContext.Default.ListCsomaglistaSablonDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a csomaglista sablonok lekérdezésekor.");
            throw;
        }
    }

    public async Task<CsomaglistaSablon> CreateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sablon);

        try
        {
            var dto = MapToDto(sablon);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.CsomaglistaSablonDto);

            var created = await SendAndReadAsync(
                HttpMethod.Post,
                "csomaglista_sablonok?select=*",
                SupabaseJsonContext.Default.ListCsomaglistaSablonDto,
                ct,
                json,
                preferRepresentation: true);

            return created is { Count: > 0 }
                ? MapToDomain(created[0])
                : throw new InvalidOperationException("A csomaglista mentése nem tért vissza adattal.");
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt csomaglista sablon létrehozásakor: {Nev}", sablon.Nev);
            throw;
        }
    }

    public async Task<CsomaglistaSablon> UpdateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sablon);

        try
        {
            var dto = MapToDto(sablon);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.CsomaglistaSablonDto);

            var updated = await SendAndReadAsync(
                HttpMethod.Patch,
                $"csomaglista_sablonok?id=eq.{sablon.Id}&select=*",
                SupabaseJsonContext.Default.ListCsomaglistaSablonDto,
                ct,
                json,
                preferRepresentation: true);

            return updated is { Count: > 0 }
                ? MapToDomain(updated[0])
                : throw new KeyNotFoundException($"A csomaglista ({sablon.Id}) nem található.");
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {SablonId} csomaglista frissítésekor.", sablon.Id);
            throw;
        }
    }

    public async Task DeleteCsomaglistaSablonAsync(Guid sablonId, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(HttpMethod.Delete, $"csomaglista_sablonok?id=eq.{sablonId}", ct);
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {SablonId} csomaglista törlésekor.", sablonId);
            throw;
        }
    }

    public async Task<IReadOnlyList<CsomaglistaSablonTetel>> GetCsomaglistaSablonTetelekAsync(Guid sablonId, CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                $"csomaglista_sablon_tetelek?sablon_id=eq.{sablonId}&select=*&order=kozos_e.desc,kategoria.asc,nev.asc",
                SupabaseJsonContext.Default.ListCsomaglistaSablonTetelDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {SablonId} csomaglista tételeinek lekérdezésekor.", sablonId);
            throw;
        }
    }

    public async Task<IReadOnlyList<CsomaglistaSablonTetel>> GetAllCsomaglistaSablonTetelekAsync(CancellationToken ct = default)
    {
        try
        {
            var dtos = await SendAndReadAsync(
                HttpMethod.Get,
                "csomaglista_sablon_tetelek?select=*&order=kozos_e.desc,kategoria.asc,nev.asc",
                SupabaseJsonContext.Default.ListCsomaglistaSablonTetelDto,
                ct);

            return dtos?.Select(MapToDomain).ToList() ?? [];
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a csomaglista sablon tételek lekérdezésekor.");
            throw;
        }
    }

    public async Task<CsomaglistaSablonTetel> AddCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        try
        {
            var dto = MapToDto(tetel);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.CsomaglistaSablonTetelDto);

            var created = await SendAndReadAsync(
                HttpMethod.Post,
                "csomaglista_sablon_tetelek?select=*",
                SupabaseJsonContext.Default.ListCsomaglistaSablonTetelDto,
                ct,
                json,
                preferRepresentation: true);

            return created is { Count: > 0 }
                ? MapToDomain(created[0])
                : throw new InvalidOperationException("A csomaglista tétel mentése nem tért vissza adattal.");
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt csomaglista sablon tétel hozzáadásakor: {Nev}", tetel.Nev);
            throw;
        }
    }

    public async Task<CsomaglistaSablonTetel> UpdateCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        try
        {
            var dto = MapToDto(tetel);
            var json = JsonSerializer.Serialize(dto, SupabaseJsonContext.Default.CsomaglistaSablonTetelDto);

            var updated = await SendAndReadAsync(
                HttpMethod.Patch,
                $"csomaglista_sablon_tetelek?id=eq.{tetel.Id}&select=*",
                SupabaseJsonContext.Default.ListCsomaglistaSablonTetelDto,
                ct,
                json,
                preferRepresentation: true);

            return updated is { Count: > 0 }
                ? MapToDomain(updated[0])
                : throw new KeyNotFoundException($"A csomaglista tétel ({tetel.Id}) nem található.");
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} sablon tétel frissítésekor.", tetel.Id);
            throw;
        }
    }

    public async Task DeleteCsomaglistaSablonTetelAsync(Guid tetelId, CancellationToken ct = default)
    {
        try
        {
            await SendNoContentAsync(HttpMethod.Delete, $"csomaglista_sablon_tetelek?id=eq.{tetelId}", ct);
        }
        catch (InvalidOperationException ex) when (IsMissingSablonTableException(ex))
        {
            throw CreateMissingSablonTableException(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} sablon tétel törlésekor.", tetelId);
            throw;
        }
    }

    private static bool IsMissingSablonTableException(InvalidOperationException ex) =>
        ex.Message.Contains("csomaglista_sablon", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Could not find the table", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException CreateMissingSablonTableException(InvalidOperationException inner) =>
        new("A csomaglista sablon táblák még nincsenek létrehozva az adatbázisban. Futtasd le a supabase/add_csomaglista_sablonok.sql fájlt a Supabase SQL Editorban!", inner);

    // --- HTTP segédmetódusok ---

    private async Task<T?> SendAndReadAsync<T>(
        HttpMethod method,
        string relativePath,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct,
        string? jsonBody = null,
        bool preferRepresentation = false,
        string? preferHeader = null)
    {
        var effectivePrefer = preferHeader ?? (preferRepresentation ? "return=representation" : null);
        using var response = await SendCoreAsync(method, relativePath, ct, jsonBody, effectivePrefer);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize(body, typeInfo);
    }

    private async Task SendNoContentAsync(
        HttpMethod method,
        string relativePath,
        CancellationToken ct,
        string? jsonBody = null)
    {
        using var response = await SendCoreAsync(method, relativePath, ct, jsonBody, preferHeader: null);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string relativePath,
        CancellationToken ct,
        string? jsonBody,
        string? preferHeader)
    {
        var config = await _configService.GetConfigAsync(ct);
        if (!config.IsConfigured)
        {
            throw new InvalidOperationException("A Supabase kapcsolat nincs beállítva.");
        }

        var token = await _authService.GetValidAccessTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Lejárt a munkamenet. Jelentkezz be újra!");
        }

        using var request = new HttpRequestMessage(method, $"{config.NormalizedUrl}/rest/v1/{relativePath}");
        request.Headers.Add("apikey", config.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrWhiteSpace(preferHeader))
        {
            request.Headers.Add("Prefer", preferHeader);
        }

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();

            var message = ExtractPostgrestError(errorBody, response.StatusCode);
            throw new InvalidOperationException(message);
        }

        return response;
    }

    private static string ExtractPostgrestError(string body, System.Net.HttpStatusCode statusCode)
    {
        if (statusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return "A munkamenet érvénytelen vagy lejárt. Jelentkezz be újra!";
        }

        try
        {
            var err = JsonSerializer.Deserialize(body, SupabaseJsonContext.Default.PostgrestErrorResponse);
            if (!string.IsNullOrWhiteSpace(err?.Message))
            {
                return $"Adatbázis hiba: {err.Message}";
            }
        }
        catch
        {
            // Ignore malformed JSON error payload
        }

        return $"Supabase szerverhiba ({(int)statusCode}).";
    }

    // --- DTO <-> Domain leképezés ---

    private static Tura MapToDomain(TuraDto dto)
    {
        var kezdoDatum = DateTime.TryParseExact(
            dto.KezdoDatum,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : DateTime.Today;

        return new Tura
        {
            Id = dto.Id,
            Nev = dto.Nev,
            Helyszin = dto.Helyszin,
            KezdoDatum = kezdoDatum,
            VegDatum = kezdoDatum.AddDays(Math.Max(0, dto.NapokSzama - 1))
        };
    }

    private static TuraDto MapToDto(Tura t) => new()
    {
        Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id,
        Nev = t.Nev.Trim(),
        Helyszin = t.Helyszin.Trim(),
        KezdoDatum = t.KezdoDatum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        NapokSzama = t.NapokSzama
    };

    private static Resztvevo MapToDomain(ResztvevoDto dto) => new()
    {
        Id = dto.Id,
        TuraId = dto.TuraId,
        UserId = dto.UserId,
        Nev = dto.Nev,
        Szerepkor = dto.Szerepkor,
        AktivE = dto.AktivE
    };

    private static ResztvevoDto MapToDto(Resztvevo r) => new()
    {
        Id = r.Id == Guid.Empty ? Guid.NewGuid() : r.Id,
        TuraId = r.TuraId,
        UserId = r.UserId,
        Nev = r.Nev.Trim(),
        Szerepkor = r.Szerepkor.Trim(),
        AktivE = r.AktivE
    };

    private static FelszerelesTetel MapToDomain(FelszerelesTetelDto dto) => new()
    {
        Id = dto.Id,
        TuraId = dto.TuraId,
        ResztvevoId = dto.KozosE ? null : dto.ResztvevoId,
        Nev = dto.Nev,
        Kategoria = dto.Kategoria,
        Mennyiseg = dto.Mennyiseg,
        Bepakolva = dto.Bepakolva,
        KozosE = dto.KozosE
    };

    private static FelszerelesTetelDto MapToDto(FelszerelesTetel f) => new()
    {
        Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id,
        TuraId = f.TuraId,
        ResztvevoId = f.KozosE ? null : f.ResztvevoId,
        Nev = f.Nev.Trim(),
        Kategoria = f.Kategoria.Trim(),
        Mennyiseg = f.Mennyiseg,
        Bepakolva = f.Bepakolva,
        KozosE = f.KozosE
    };

    private static Fogas MapToDomain(FogasDto dto) => new()
    {
        Id = dto.Id,
        TuraId = dto.TuraId,
        ResztvevoId = dto.ResztvevoId,
        Halfaj = dto.Halfaj,
        DarabAlapuE = dto.DarabAlapuE,
        Mennyiseg = dto.DarabAlapuE ? Math.Max(1, dto.Mennyiseg) : 1,
        VisszaengedettE = dto.VisszaengedettE,
        SulyKg = dto.DarabAlapuE ? null : dto.SulyKg,
        HosszCm = dto.DarabAlapuE ? null : dto.HosszCm,
        Idopont = dto.Idopont.ToLocalTime().DateTime,
        Csali = dto.Csali,
        Megjegyzes = dto.Megjegyzes,
        KepUrl = dto.KepUrl
    };

    private static FogasDto MapToDto(Fogas f)
    {
        var localTime = f.Idopont.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(f.Idopont, DateTimeKind.Local)
            : f.Idopont;

        return new FogasDto
        {
            Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id,
            TuraId = f.TuraId,
            ResztvevoId = f.ResztvevoId,
            Halfaj = f.Halfaj.Trim(),
            DarabAlapuE = f.DarabAlapuE,
            Mennyiseg = f.DarabAlapuE ? Math.Max(1, f.Mennyiseg) : 1,
            VisszaengedettE = f.VisszaengedettE,
            SulyKg = f.DarabAlapuE ? null : f.SulyKg,
            HosszCm = f.DarabAlapuE ? null : f.HosszCm,
            Idopont = new DateTimeOffset(localTime).ToUniversalTime(),
            Csali = f.Csali.Trim(),
            Megjegyzes = string.IsNullOrWhiteSpace(f.Megjegyzes) ? null : f.Megjegyzes.Trim(),
            KepUrl = string.IsNullOrWhiteSpace(f.KepUrl) ? null : f.KepUrl.Trim()
        };
    }

    private static KoltsegTetel MapToDomain(KoltsegTetelDto dto) => new()
    {
        Id = dto.Id,
        TuraId = dto.TuraId,
        Nev = dto.Nev,
        Osszeg = dto.Osszeg,
        KozosE = dto.KozosE,
        ResztvevoId = dto.KozosE ? null : dto.ResztvevoId,
        FizetoResztvevoId = dto.FizetoResztvevoId,
        Megjegyzes = dto.Megjegyzes
    };

    private static KoltsegTetelDto MapToDto(KoltsegTetel k) => new()
    {
        Id = k.Id == Guid.Empty ? Guid.NewGuid() : k.Id,
        TuraId = k.TuraId,
        Nev = k.Nev.Trim(),
        Osszeg = Math.Max(0m, k.Osszeg),
        KozosE = k.KozosE,
        ResztvevoId = k.KozosE ? null : k.ResztvevoId,
        FizetoResztvevoId = k.FizetoResztvevoId == Guid.Empty ? null : k.FizetoResztvevoId,
        Megjegyzes = string.IsNullOrWhiteSpace(k.Megjegyzes) ? null : k.Megjegyzes.Trim()
    };

    private static CsomaglistaSablon MapToDomain(CsomaglistaSablonDto dto) => new()
    {
        Id = dto.Id,
        Nev = dto.Nev,
        Leiras = dto.Leiras,
        CreatedAt = dto.CreatedAt ?? DateTimeOffset.UtcNow
    };

    private static CsomaglistaSablonDto MapToDto(CsomaglistaSablon s) => new()
    {
        Id = s.Id == Guid.Empty ? Guid.NewGuid() : s.Id,
        Nev = s.Nev.Trim(),
        Leiras = string.IsNullOrWhiteSpace(s.Leiras) ? null : s.Leiras.Trim()
    };

    private static CsomaglistaSablonTetel MapToDomain(CsomaglistaSablonTetelDto dto) => new()
    {
        Id = dto.Id,
        SablonId = dto.SablonId,
        Nev = dto.Nev,
        Kategoria = dto.Kategoria,
        Mennyiseg = Math.Max(1, dto.Mennyiseg),
        KozosE = dto.KozosE
    };

    private static CsomaglistaSablonTetelDto MapToDto(CsomaglistaSablonTetel t) => new()
    {
        Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id,
        SablonId = t.SablonId,
        Nev = t.Nev.Trim(),
        Kategoria = t.Kategoria.Trim(),
        Mennyiseg = Math.Max(1, t.Mennyiseg),
        KozosE = t.KozosE
    };
}
