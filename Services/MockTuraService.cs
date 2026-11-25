using FishingPlanner.Models;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// Memóriabeli, szálbiztos mock implementáció az ITuraService interfészhez,
/// előre feltöltött minta horgásztúrával, résztvevőkkel, felszerelésekkel és fogásokkal.
/// </summary>
public sealed class MockTuraService : ITuraService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<MockTuraService> _logger;

    private readonly List<Tura> _turak = [];
    private readonly List<Resztvevo> _resztvevok = [];
    private readonly List<FelszerelesTetel> _felszerelesek = [];
    private readonly List<Fogas> _fogasok = [];
    private readonly List<KoltsegTetel> _koltsegek = [];
    private readonly List<CsomaglistaSablon> _sablonok = [];
    private readonly List<CsomaglistaSablonTetel> _sablonTetelek = [];

    public MockTuraService(ILogger<MockTuraService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SeedSampleData();
    }

    private void SeedSampleData()
    {
        var turaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var benceId = Guid.Parse("22222222-2222-2222-2222-222222222201");
        var danielId = Guid.Parse("22222222-2222-2222-2222-222222222202");
        var martonId = Guid.Parse("22222222-2222-2222-2222-222222222203");

        var kezdoDatum = DateTime.Today.AddDays(-2);

        _turak.Add(new Tura
        {
            Id = turaId,
            Nev = "Tisza-tavi őszi bojlis túra",
            Helyszin = "Tisza-tó – Abádszalóki-öböl",
            KezdoDatum = kezdoDatum,
            VegDatum = kezdoDatum.AddDays(6)
        });

        _resztvevok.AddRange(
        [
            new Resztvevo
            {
                Id = benceId,
                TuraId = turaId,
                UserId = benceId,
                Nev = "bence",
                Szerepkor = "Szervező / Bojlis"
            },
            new Resztvevo
            {
                Id = danielId,
                TuraId = turaId,
                UserId = danielId,
                Nev = "dani",
                Szerepkor = "Szakács / Feederes"
            },
            new Resztvevo
            {
                Id = martonId,
                TuraId = turaId,
                UserId = martonId,
                Nev = "marton",
                Szerepkor = "Fotós / Pergető"
            }
        ]);

        _felszerelesek.AddRange(
        [
            // Közös felszerelések
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333301"),
                TuraId = turaId,
                ResztvevoId = null,
                Nev = "3 személyes horgászsátor (Bivvy)",
                Kategoria = "Táborozás",
                Mennyiseg = 1,
                Bepakolva = true,
                KozosE = true
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333302"),
                TuraId = turaId,
                ResztvevoId = null,
                Nev = "Gumicsónak elektromos motorral",
                Kategoria = "Csónakázás",
                Mennyiseg = 1,
                Bepakolva = true,
                KozosE = true
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333303"),
                TuraId = turaId,
                ResztvevoId = null,
                Nev = "Gázfőző + palack",
                Kategoria = "Konyha",
                Mennyiseg = 1,
                Bepakolva = false,
                KozosE = true
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333304"),
                TuraId = turaId,
                ResztvevoId = null,
                Nev = "Bojli és pellet csomag (fűszeres-halas)",
                Kategoria = "Etetés és csali",
                Mennyiseg = 4,
                Bepakolva = true,
                KozosE = true
            },

            // Egyéni felszerelések
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333305"),
                TuraId = turaId,
                ResztvevoId = benceId,
                Nev = "3 db bojlis bot + nyeles merítő",
                Kategoria = "Botok és orsók",
                Mennyiseg = 1,
                Bepakolva = true,
                KozosE = false
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333306"),
                TuraId = turaId,
                ResztvevoId = benceId,
                Nev = "Horgászágy + hálózsák",
                Kategoria = "Táborozás",
                Mennyiseg = 1,
                Bepakolva = false,
                KozosE = false
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333307"),
                TuraId = turaId,
                ResztvevoId = danielId,
                Nev = "Feeder botkészlet + etetőkosarak",
                Kategoria = "Botok és orsók",
                Mennyiseg = 1,
                Bepakolva = true,
                KozosE = false
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333308"),
                TuraId = turaId,
                ResztvevoId = danielId,
                Nev = "Hűtőtáska élelmiszerrel",
                Kategoria = "Konyha",
                Mennyiseg = 1,
                Bepakolva = true,
                KozosE = false
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333309"),
                TuraId = turaId,
                ResztvevoId = martonId,
                Nev = "Pergető felszerelés + műcsali doboz",
                Kategoria = "Botok és orsók",
                Mennyiseg = 1,
                Bepakolva = true,
                KozosE = false
            },
            new FelszerelesTetel
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333310"),
                TuraId = turaId,
                ResztvevoId = martonId,
                Nev = "Vízálló ruházat és fejlámpa",
                Kategoria = "Ruházat",
                Mennyiseg = 1,
                Bepakolva = false,
                KozosE = false
            }
        ]);

        _fogasok.AddRange(
        [
            new Fogas
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444401"),
                TuraId = turaId,
                ResztvevoId = benceId,
                Halfaj = "Tükörponty",
                SulyKg = 11.4,
                HosszCm = 78.0,
                Idopont = kezdoDatum.AddHours(5).AddMinutes(40),
                Csali = "20mm Fűszeres halas bojli",
                Megjegyzes = "Erős hajnali kapás a nádfal elől, visszaengedve (C&R).",
                KepUrl = null
            },
            new Fogas
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444402"),
                TuraId = turaId,
                ResztvevoId = danielId,
                Halfaj = "Amur",
                SulyKg = 7.8,
                HosszCm = 84.0,
                Idopont = kezdoDatum.AddDays(1).AddHours(15).AddMinutes(20),
                Csali = "Mézes kukorica pop-up",
                Megjegyzes = "Intenzív fárasztás a stég mellett.",
                KepUrl = null
            },
            new Fogas
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444403"),
                TuraId = turaId,
                ResztvevoId = martonId,
                Halfaj = "Csuka",
                SulyKg = 4.6,
                HosszCm = 74.0,
                Idopont = kezdoDatum.AddDays(1).AddHours(8).AddMinutes(15),
                Csali = "12cm Gumihal (firetiger)",
                Megjegyzes = "Akadós részről, csónakból pergetve.",
                KepUrl = null
            }
        ]);

        _koltsegek.AddRange(
        [
            new KoltsegTetel
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555501"),
                TuraId = turaId,
                Nev = "Ételek",
                Osszeg = 24000m,
                KozosE = true,
                FizetoResztvevoId = danielId,
                Megjegyzes = "Hétvégi közös bevásárlás (hús, pékáru, kávé)"
            },
            new KoltsegTetel
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555502"),
                TuraId = turaId,
                Nev = "Etetőanyag és csali",
                Osszeg = 18000m,
                KozosE = true,
                FizetoResztvevoId = benceId,
                Megjegyzes = "Bojli, pellet, kukorica"
            },
            new KoltsegTetel
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555503"),
                TuraId = turaId,
                Nev = "Horgászjegy / Napijegy",
                Osszeg = 9000m,
                KozosE = false,
                ResztvevoId = martonId,
                FizetoResztvevoId = martonId,
                Megjegyzes = "Csónakos kiegészítő jegy"
            }
        ]);

        var alapSablonId = Guid.Parse("66666666-6666-6666-6666-666666666601");
        var konyhaSablonId = Guid.Parse("66666666-6666-6666-6666-666666666602");

        _sablonok.AddRange(
        [
            new CsomaglistaSablon
            {
                Id = alapSablonId,
                Nev = "Bojlis alapszett",
                Leiras = "Többnapos pontyozó túrák alapfelszerelése"
            },
            new CsomaglistaSablon
            {
                Id = konyhaSablonId,
                Nev = "Konyha és tábor",
                Leiras = "Főzéshez és táborozáshoz szükséges közös felszerelés"
            }
        ]);

        _sablonTetelek.AddRange(
        [
            new CsomaglistaSablonTetel
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777701"),
                SablonId = alapSablonId,
                Nev = "3 db bojlis bot + orsók",
                Kategoria = "Botok és orsók",
                Mennyiseg = 1,
                KozosE = false
            },
            new CsomaglistaSablonTetel
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777702"),
                SablonId = alapSablonId,
                Nev = "Pontymatrac és merítőháló",
                Kategoria = "Botok és orsók",
                Mennyiseg = 1,
                KozosE = true
            },
            new CsomaglistaSablonTetel
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777703"),
                SablonId = konyhaSablonId,
                Nev = "Gázfőző + palack",
                Kategoria = "Konyha",
                Mennyiseg = 1,
                KozosE = true
            },
            new CsomaglistaSablonTetel
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777704"),
                SablonId = konyhaSablonId,
                Nev = "Hűtőtáska + jégakku",
                Kategoria = "Konyha",
                Mennyiseg = 1,
                KozosE = true
            }
        ]);
    }

    // --- Túrák ---

    public async Task<IReadOnlyList<Tura>> GetTurakAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _turak
                .OrderByDescending(t => t.KezdoDatum)
                .Select(Clone)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a túrák lekérdezése közben.");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Tura?> GetTuraByIdAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var tura = _turak.FirstOrDefault(t => t.Id == turaId);
            return tura is null ? null : Clone(tura);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} azonosítójú túra lekérdezése közben.", turaId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Tura> CreateTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tura);

        await _gate.WaitAsync(ct);
        try
        {
            var ujTura = Clone(tura);
            if (ujTura.Id == Guid.Empty)
            {
                ujTura.Id = Guid.NewGuid();
            }

            _turak.Add(ujTura);
            return Clone(ujTura);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt új túra létrehozása közben: {TuraNev}", tura.Nev);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Tura> UpdateTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tura);

        await _gate.WaitAsync(ct);
        try
        {
            var index = _turak.FindIndex(t => t.Id == tura.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"A frissíteni kívánt túra ({tura.Id}) nem található.");
            }

            var frissitett = Clone(tura);
            _turak[index] = frissitett;
            return Clone(frissitett);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra frissítése közben.", tura.Id);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteTuraAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var removed = _turak.RemoveAll(t => t.Id == turaId);
            if (removed == 0)
            {
                throw new KeyNotFoundException($"A törölni kívánt túra ({turaId}) nem található.");
            }

            // Kapcsolódó entitások kaszkádolt törlése
            _resztvevok.RemoveAll(r => r.TuraId == turaId);
            _felszerelesek.RemoveAll(f => f.TuraId == turaId);
            _fogasok.RemoveAll(f => f.TuraId == turaId);
            _koltsegek.RemoveAll(k => k.TuraId == turaId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra törlése közben.", turaId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Regisztrált felhasználók ---

    public Task<IReadOnlyList<RegisztraltFelhasznalo>> GetRegisztraltFelhasznalokAsync(CancellationToken ct = default)
    {
        IReadOnlyList<RegisztraltFelhasznalo> lista =
        [
            new(Guid.Parse("22222222-2222-2222-2222-222222222201"), "bence"),
            new(Guid.Parse("22222222-2222-2222-2222-222222222202"), "dani"),
            new(Guid.Parse("22222222-2222-2222-2222-222222222203"), "marton"),
            new(Guid.Parse("22222222-2222-2222-2222-222222222204"), "gabor")
        ];
        return Task.FromResult(lista);
    }

    // --- Résztvevők ---

    public async Task<IReadOnlyList<Resztvevo>> GetResztvevokAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _resztvevok
                .Where(r => r.TuraId == turaId)
                .OrderBy(r => r.Nev)
                .Select(Clone)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra résztvevőinek lekérdezése közben.", turaId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Resztvevo> AddResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resztvevo);

        await _gate.WaitAsync(ct);
        try
        {
            if (_turak.All(t => t.Id != resztvevo.TuraId))
            {
                throw new KeyNotFoundException($"A megadott túra ({resztvevo.TuraId}) nem található.");
            }

            var ujResztvevo = Clone(resztvevo);
            if (ujResztvevo.Id == Guid.Empty)
            {
                ujResztvevo.Id = Guid.NewGuid();
            }

            _resztvevok.Add(ujResztvevo);
            return Clone(ujResztvevo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt résztvevő hozzáadása közben: {Nev}", resztvevo.Nev);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Resztvevo> UpdateResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resztvevo);

        await _gate.WaitAsync(ct);
        try
        {
            var index = _resztvevok.FindIndex(r => r.Id == resztvevo.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"A frissíteni kívánt résztvevő ({resztvevo.Id}) nem található.");
            }

            var frissitett = Clone(resztvevo);
            _resztvevok[index] = frissitett;
            return Clone(frissitett);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {ResztvevoId} résztvevő frissítése közben.", resztvevo.Id);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteResztvevoAsync(Guid resztvevoId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var removed = _resztvevok.RemoveAll(r => r.Id == resztvevoId);
            if (removed == 0)
            {
                throw new KeyNotFoundException($"A törölni kívánt résztvevő ({resztvevoId}) nem található.");
            }

            _felszerelesek.RemoveAll(f => f.ResztvevoId == resztvevoId);
            _fogasok.RemoveAll(f => f.ResztvevoId == resztvevoId);
            _koltsegek.RemoveAll(k => k.ResztvevoId == resztvevoId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {ResztvevoId} résztvevő törlése közben.", resztvevoId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Felszerelések ---

    public async Task<IReadOnlyList<FelszerelesTetel>> GetFelszerelesekAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _felszerelesek
                .Where(f => f.TuraId == turaId)
                .OrderByDescending(f => f.KozosE)
                .ThenBy(f => f.Kategoria)
                .ThenBy(f => f.Nev)
                .Select(Clone)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra felszereléseinek lekérdezése közben.", turaId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FelszerelesTetel> AddFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            if (_turak.All(t => t.Id != tetel.TuraId))
            {
                throw new KeyNotFoundException($"A megadott túra ({tetel.TuraId}) nem található.");
            }

            var ujTetel = Clone(tetel);
            if (ujTetel.Id == Guid.Empty)
            {
                ujTetel.Id = Guid.NewGuid();
            }

            if (ujTetel.KozosE)
            {
                ujTetel.ResztvevoId = null;
            }

            _felszerelesek.Add(ujTetel);
            return Clone(ujTetel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt felszerelés hozzáadása közben: {TetelNev}", tetel.Nev);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FelszerelesTetel> UpdateFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var index = _felszerelesek.FindIndex(f => f.Id == tetel.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"A frissíteni kívánt felszerelési tétel ({tetel.Id}) nem található.");
            }

            var frissitett = Clone(tetel);
            if (frissitett.KozosE)
            {
                frissitett.ResztvevoId = null;
            }

            _felszerelesek[index] = frissitett;
            return Clone(frissitett);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} felszerelés frissítése közben.", tetel.Id);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ToggleBepakolvaAsync(Guid tetelId, bool bepakolva, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var tetel = _felszerelesek.FirstOrDefault(f => f.Id == tetelId);
            if (tetel is null)
            {
                throw new KeyNotFoundException($"A felszerelési tétel ({tetelId}) nem található.");
            }

            tetel.Bepakolva = bepakolva;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} felszerelés állapotának váltása közben.", tetelId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteFelszerelesAsync(Guid tetelId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var removed = _felszerelesek.RemoveAll(f => f.Id == tetelId);
            if (removed == 0)
            {
                throw new KeyNotFoundException($"A törölni kívánt felszerelési tétel ({tetelId}) nem található.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TetelId} felszerelés törlése közben.", tetelId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Fogások ---

    public async Task<IReadOnlyList<Fogas>> GetFogasokAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _fogasok
                .Where(f => f.TuraId == turaId)
                .OrderByDescending(f => f.Idopont)
                .Select(Clone)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {TuraId} túra fogásainak lekérdezése közben.", turaId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Fogas> AddFogasAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);

        await _gate.WaitAsync(ct);
        try
        {
            if (_turak.All(t => t.Id != fogas.TuraId))
            {
                throw new KeyNotFoundException($"A megadott túra ({fogas.TuraId}) nem található.");
            }

            var ujFogas = Clone(fogas);
            if (ujFogas.Id == Guid.Empty)
            {
                ujFogas.Id = Guid.NewGuid();
            }

            _fogasok.Add(ujFogas);
            return Clone(ujFogas);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt új fogás rögzítése közben: {Halfaj}", fogas.Halfaj);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Fogas> UpdateFogasAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);

        await _gate.WaitAsync(ct);
        try
        {
            var index = _fogasok.FindIndex(f => f.Id == fogas.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"A frissíteni kívánt fogás ({fogas.Id}) nem található.");
            }

            var frissitett = Clone(fogas);
            _fogasok[index] = frissitett;
            return Clone(frissitett);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {FogasId} fogás frissítése közben.", fogas.Id);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteFogasAsync(Guid fogasId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var removed = _fogasok.RemoveAll(f => f.Id == fogasId);
            if (removed == 0)
            {
                throw new KeyNotFoundException($"A törölni kívánt fogás ({fogasId}) nem található.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a(z) {FogasId} fogás törlése közben.", fogasId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Költségek ---

    public async Task<IReadOnlyList<KoltsegTetel>> GetKoltsegekAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _koltsegek
                .Where(k => k.TuraId == turaId)
                .OrderByDescending(k => k.KozosE)
                .ThenBy(k => k.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KoltsegTetel> AddKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var uj = Clone(tetel);
            if (uj.Id == Guid.Empty)
            {
                uj.Id = Guid.NewGuid();
            }

            if (uj.KozosE)
            {
                uj.ResztvevoId = null;
            }

            _koltsegek.Add(uj);
            return Clone(uj);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KoltsegTetel> UpdateKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var index = _koltsegek.FindIndex(k => k.Id == tetel.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"A frissíteni kívánt költségmező ({tetel.Id}) nem található.");
            }

            var frissitett = Clone(tetel);
            if (frissitett.KozosE)
            {
                frissitett.ResztvevoId = null;
            }

            _koltsegek[index] = frissitett;
            return Clone(frissitett);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteKoltsegAsync(Guid koltsegId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var removed = _koltsegek.RemoveAll(k => k.Id == koltsegId);
            if (removed == 0)
            {
                throw new KeyNotFoundException($"A törölni kívánt költségmező ({koltsegId}) nem található.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Újrahasználható csomaglisták (sablonok) ---

    public async Task<IReadOnlyList<CsomaglistaSablon>> GetCsomaglistaSablonokAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _sablonok
                .OrderBy(s => s.Nev, StringComparer.CurrentCultureIgnoreCase)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablon> CreateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sablon);
        await _gate.WaitAsync(ct);
        try
        {
            var uj = Clone(sablon);
            if (uj.Id == Guid.Empty) uj.Id = Guid.NewGuid();
            _sablonok.Add(uj);
            return Clone(uj);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablon> UpdateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sablon);
        await _gate.WaitAsync(ct);
        try
        {
            var idx = _sablonok.FindIndex(s => s.Id == sablon.Id);
            if (idx < 0) throw new KeyNotFoundException($"A csomaglista ({sablon.Id}) nem található.");
            var friss = Clone(sablon);
            _sablonok[idx] = friss;
            return Clone(friss);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteCsomaglistaSablonAsync(Guid sablonId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _sablonok.RemoveAll(s => s.Id == sablonId);
            _sablonTetelek.RemoveAll(t => t.SablonId == sablonId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CsomaglistaSablonTetel>> GetCsomaglistaSablonTetelekAsync(Guid sablonId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _sablonTetelek
                .Where(t => t.SablonId == sablonId)
                .OrderByDescending(t => t.KozosE)
                .ThenBy(t => t.Kategoria)
                .ThenBy(t => t.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CsomaglistaSablonTetel>> GetAllCsomaglistaSablonTetelekAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _sablonTetelek
                .OrderByDescending(t => t.KozosE)
                .ThenBy(t => t.Kategoria)
                .ThenBy(t => t.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablonTetel> AddCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);
        await _gate.WaitAsync(ct);
        try
        {
            var uj = Clone(tetel);
            if (uj.Id == Guid.Empty) uj.Id = Guid.NewGuid();
            _sablonTetelek.Add(uj);
            return Clone(uj);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablonTetel> UpdateCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);
        await _gate.WaitAsync(ct);
        try
        {
            var idx = _sablonTetelek.FindIndex(t => t.Id == tetel.Id);
            if (idx < 0) throw new KeyNotFoundException($"A sablon tétel ({tetel.Id}) nem található.");
            var friss = Clone(tetel);
            _sablonTetelek[idx] = friss;
            return Clone(friss);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteCsomaglistaSablonTetelAsync(Guid tetelId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _sablonTetelek.RemoveAll(t => t.Id == tetelId);
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Defenzív klónozó segédmetódusok ---

    public Task<IReadOnlyList<HalKategoriaCsoport>> GetHalKategoriakAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HalKategoriaCsoport>>(HalKatalogusDefaults.GetDefaultCsoportok());

    private static Tura Clone(Tura t) => new()
    {
        Id = t.Id,
        Nev = t.Nev,
        Helyszin = t.Helyszin,
        KezdoDatum = t.KezdoDatum,
        VegDatum = t.VegDatum
    };

    private static Resztvevo Clone(Resztvevo r) => new()
    {
        Id = r.Id,
        TuraId = r.TuraId,
        UserId = r.UserId,
        Nev = r.Nev,
        Szerepkor = r.Szerepkor,
        AktivE = r.AktivE
    };

    private static FelszerelesTetel Clone(FelszerelesTetel f) => new()
    {
        Id = f.Id,
        TuraId = f.TuraId,
        ResztvevoId = f.ResztvevoId,
        Nev = f.Nev,
        Kategoria = f.Kategoria,
        Mennyiseg = f.Mennyiseg,
        Bepakolva = f.Bepakolva,
        KozosE = f.KozosE
    };

    private static Fogas Clone(Fogas f) => new()
    {
        Id = f.Id,
        TuraId = f.TuraId,
        ResztvevoId = f.ResztvevoId,
        Halfaj = f.Halfaj,
        DarabAlapuE = f.DarabAlapuE,
        Mennyiseg = f.DarabAlapuE ? Math.Max(1, f.Mennyiseg) : 1,
        VisszaengedettE = f.VisszaengedettE,
        SulyKg = f.DarabAlapuE ? null : f.SulyKg,
        HosszCm = f.DarabAlapuE ? null : f.HosszCm,
        Idopont = f.Idopont,
        Csali = f.Csali,
        Megjegyzes = f.Megjegyzes,
        KepUrl = f.KepUrl
    };

    private static KoltsegTetel Clone(KoltsegTetel k) => new()
    {
        Id = k.Id,
        TuraId = k.TuraId,
        Nev = k.Nev,
        Osszeg = k.Osszeg,
        KozosE = k.KozosE,
        ResztvevoId = k.KozosE ? null : k.ResztvevoId,
        FizetoResztvevoId = k.FizetoResztvevoId,
        Megjegyzes = k.Megjegyzes
    };

    private static CsomaglistaSablon Clone(CsomaglistaSablon s) => new()
    {
        Id = s.Id,
        Nev = s.Nev,
        Leiras = s.Leiras,
        CreatedAt = s.CreatedAt
    };

    private static CsomaglistaSablonTetel Clone(CsomaglistaSablonTetel t) => new()
    {
        Id = t.Id,
        SablonId = t.SablonId,
        Nev = t.Nev,
        Kategoria = t.Kategoria,
        Mennyiseg = Math.Max(1, t.Mennyiseg),
        KozosE = t.KozosE
    };
}
