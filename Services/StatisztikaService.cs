using FishingPlanner.Models;

namespace FishingPlanner.Services;

/// <summary>
/// A horgásztúrák és az év végi összesítés fogási statisztikáit és LINQ ranglistáit kiszámító szolgáltatás.
/// </summary>
public interface IStatisztikaService
{
    TuraStatisztikaOsszesito SzamolStatisztika(
        Tura tura,
        IReadOnlyCollection<Resztvevo> resztvevok,
        IReadOnlyCollection<Fogas> fogasok,
        IReadOnlyCollection<HalKategoriaCsoport>? halKategoriak = null,
        IReadOnlyCollection<KoltsegTetel>? koltsegek = null,
        IReadOnlyCollection<RegisztraltFelhasznalo>? profilok = null);

    EvVegiOsszesito SzamolEvVegiStatisztika(
        int ev,
        IReadOnlyCollection<Tura> evTurai,
        IReadOnlyCollection<Resztvevo> osszesResztvevo,
        IReadOnlyCollection<Fogas> osszesFogas,
        IReadOnlyCollection<KoltsegTetel>? osszesKoltseg = null,
        IReadOnlyCollection<HalKategoriaCsoport>? halKategoriak = null,
        IReadOnlyCollection<RegisztraltFelhasznalo>? profilok = null);
}

public sealed class StatisztikaService : IStatisztikaService
{
    public TuraStatisztikaOsszesito SzamolStatisztika(
        Tura tura,
        IReadOnlyCollection<Resztvevo> resztvevok,
        IReadOnlyCollection<Fogas> fogasok,
        IReadOnlyCollection<HalKategoriaCsoport>? halKategoriak = null,
        IReadOnlyCollection<KoltsegTetel>? koltsegek = null,
        IReadOnlyCollection<RegisztraltFelhasznalo>? profilok = null)
    {
        ArgumentNullException.ThrowIfNull(tura);
        ArgumentNullException.ThrowIfNull(resztvevok);
        ArgumentNullException.ThrowIfNull(fogasok);

        var kategoriaLookup = BuildKategoriaLookup(halKategoriak);
        var profilMap = (profilok ?? []).ToDictionary(p => p.Id, p => p);

        string ResolveDisplayNev(Resztvevo r)
        {
            if (r.UserId.HasValue && profilMap.TryGetValue(r.UserId.Value, out var p))
            {
                return p.MegjelenitesiNev;
            }

            return r.Nev.Trim();
        }

        var resztvevoMap = resztvevok.ToDictionary(r => r.Id, ResolveDisplayNev);

        // 1. Horgász ranglista egy túrán belül (megtartottakkal együtt)
        var ranglistaNyers = resztvevok
            .GroupJoin(
                fogasok,
                r => r.Id,
                f => f.ResztvevoId,
                (r, resztvevoFogasai) =>
                {
                    var lista = resztvevoFogasai.ToList();
                    var db = lista.Sum(x => x.EffektiveDb);
                    var sulyos = lista.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).ToList();
                    var osszSuly = Math.Round(sulyos.Sum(x => x.SulyKg!.Value), 2);
                    var maxSuly = sulyos.Count > 0 ? Math.Round(sulyos.Max(x => x.SulyKg!.Value), 2) : 0.0;
                    var atlagSuly = sulyos.Count > 0 ? Math.Round(sulyos.Average(x => x.SulyKg!.Value), 2) : 0.0;

                    var megtartottLista = lista.Where(x => !x.VisszaengedettE).ToList();
                    var megtartottDb = megtartottLista.Sum(x => x.EffektiveDb);
                    var megtartottSuly = Math.Round(
                        megtartottLista.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).Sum(x => x.SulyKg!.Value), 2);

                    return new
                    {
                        r.Id,
                        Nev = ResolveDisplayNev(r),
                        r.Szerepkor,
                        r.AktivE,
                        FogasokSzama = db,
                        OsszSulyKg = osszSuly,
                        LegnagyobbHalKg = maxSuly,
                        AtlagSulyKg = atlagSuly,
                        MegtartottDb = megtartottDb,
                        MegtartottSulyKg = megtartottSuly
                    };
                })
            .Where(x => x.AktivE || x.FogasokSzama > 0)
            .OrderByDescending(x => x.OsszSulyKg)
            .ThenByDescending(x => x.FogasokSzama)
            .ThenByDescending(x => x.LegnagyobbHalKg)
            .ThenBy(x => x.Nev, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var horgaszRanglista = ranglistaNyers
            .Select((elem, idx) => new HorgaszRanglistaElem(
                idx + 1,
                elem.Id,
                elem.Nev,
                elem.Szerepkor,
                elem.FogasokSzama,
                elem.OsszSulyKg,
                elem.LegnagyobbHalKg,
                elem.AtlagSulyKg,
                elem.MegtartottDb,
                elem.MegtartottSulyKg))
            .ToList();

        var osszKoltseg = (koltsegek ?? []).Sum(k => k.Osszeg);
        var aktivLetszam = resztvevok.Count(r => r.AktivE);
        var koltsegPerFo = aktivLetszam > 0 ? osszKoltseg / aktivLetszam : 0m;

        return BuildCoreStatisztika(
            fogasok,
            horgaszRanglista,
            resztvevoMap,
            turaNevMap: null,
            kategoriaLookup,
            osszKoltseg,
            koltsegPerFo);
    }

    public EvVegiOsszesito SzamolEvVegiStatisztika(
        int ev,
        IReadOnlyCollection<Tura> evTurai,
        IReadOnlyCollection<Resztvevo> osszesResztvevo,
        IReadOnlyCollection<Fogas> osszesFogas,
        IReadOnlyCollection<KoltsegTetel>? osszesKoltseg = null,
        IReadOnlyCollection<HalKategoriaCsoport>? halKategoriak = null,
        IReadOnlyCollection<RegisztraltFelhasznalo>? profilok = null)
    {
        ArgumentNullException.ThrowIfNull(evTurai);
        ArgumentNullException.ThrowIfNull(osszesResztvevo);
        ArgumentNullException.ThrowIfNull(osszesFogas);

        var koltsegek = osszesKoltseg ?? [];
        var kategoriaLookup = BuildKategoriaLookup(halKategoriak);
        var profilMap = (profilok ?? []).ToDictionary(p => p.Id, p => p);
        var turaNevMap = evTurai.ToDictionary(t => t.Id, t => t.Nev);

        string ResolveDisplayNev(Resztvevo r)
        {
            if (r.UserId.HasValue && profilMap.TryGetValue(r.UserId.Value, out var p))
            {
                return p.MegjelenitesiNev;
            }

            return r.Nev.Trim();
        }

        string GetAnglerKey(Resztvevo r) =>
            r.UserId.HasValue && r.UserId.Value != Guid.Empty
                ? $"u:{r.UserId.Value:N}"
                : $"n:{r.Nev.Trim().ToLowerInvariant()}";

        var resztvevoById = osszesResztvevo.ToDictionary(r => r.Id, r => r);
        var resztvevoDisplayMap = osszesResztvevo.ToDictionary(r => r.Id, ResolveDisplayNev);

        var fogasokByAngler = new Dictionary<string, List<Fogas>>(StringComparer.OrdinalIgnoreCase);
        foreach (var fogas in osszesFogas)
        {
            if (!resztvevoById.TryGetValue(fogas.ResztvevoId, out var r))
            {
                continue;
            }

            var key = GetAnglerKey(r);
            if (!fogasokByAngler.TryGetValue(key, out var list))
            {
                list = [];
                fogasokByAngler[key] = list;
            }

            list.Add(fogas);
        }

        var evesRanglistaNyers = osszesResztvevo
            .GroupBy(GetAnglerKey, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var elso = g.First();
                var displayNev = ResolveDisplayNev(elso);
                var aktivTurak = g.Where(x => x.AktivE).Select(x => x.TuraId).Distinct().Count();
                var resztvevoFogasai = fogasokByAngler.TryGetValue(g.Key, out var fl) ? fl : [];

                var db = resztvevoFogasai.Sum(x => x.EffektiveDb);
                var sulyos = resztvevoFogasai.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).ToList();
                var osszSuly = Math.Round(sulyos.Sum(x => x.SulyKg!.Value), 2);
                var maxSuly = sulyos.Count > 0 ? Math.Round(sulyos.Max(x => x.SulyKg!.Value), 2) : 0.0;
                var atlagSuly = sulyos.Count > 0 ? Math.Round(sulyos.Average(x => x.SulyKg!.Value), 2) : 0.0;

                var megtartottLista = resztvevoFogasai.Where(x => !x.VisszaengedettE).ToList();
                var megtartottDb = megtartottLista.Sum(x => x.EffektiveDb);
                var megtartottSuly = Math.Round(
                    megtartottLista.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).Sum(x => x.SulyKg!.Value), 2);

                var osszesTuraReszvetel = Math.Max(
                    aktivTurak,
                    resztvevoFogasai.Select(f => f.TuraId).Distinct().Count());

                return new
                {
                    elso.Id,
                    Nev = displayNev,
                    TuraSzoveg = $"{osszesTuraReszvetel} túra",
                    AktivE = aktivTurak > 0,
                    FogasokSzama = db,
                    OsszSulyKg = osszSuly,
                    LegnagyobbHalKg = maxSuly,
                    AtlagSulyKg = atlagSuly,
                    MegtartottDb = megtartottDb,
                    MegtartottSulyKg = megtartottSuly
                };
            })
            .Where(x => x.AktivE || x.FogasokSzama > 0)
            .OrderByDescending(x => x.OsszSulyKg)
            .ThenByDescending(x => x.FogasokSzama)
            .ThenByDescending(x => x.LegnagyobbHalKg)
            .ThenBy(x => x.Nev, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var evesRanglista = evesRanglistaNyers
            .Select((elem, idx) => new HorgaszRanglistaElem(
                idx + 1,
                elem.Id,
                elem.Nev,
                elem.TuraSzoveg,
                elem.FogasokSzama,
                elem.OsszSulyKg,
                elem.LegnagyobbHalKg,
                elem.AtlagSulyKg,
                elem.MegtartottDb,
                elem.MegtartottSulyKg))
            .ToList();

        var evesOsszKoltseg = koltsegek.Sum(k => k.Osszeg);

        var statisztika = BuildCoreStatisztika(
            osszesFogas,
            evesRanglista,
            resztvevoDisplayMap,
            turaNevMap,
            kategoriaLookup,
            evesOsszKoltseg,
            koltsegPerFo: 0m);

        var fogasokByTura = osszesFogas
            .GroupBy(f => f.TuraId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var koltsegekByTura = koltsegek
            .GroupBy(k => k.TuraId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Osszeg));

        var turaBontas = evTurai
            .OrderByDescending(t => t.KezdoDatum)
            .Select(t =>
            {
                var tFogasok = fogasokByTura.TryGetValue(t.Id, out var fl) ? fl : [];
                var sulyos = tFogasok.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).ToList();
                var tKoltseg = koltsegekByTura.TryGetValue(t.Id, out var kSum) ? kSum : 0m;

                return new EvVegiTuraSor(
                    t.Id,
                    t.Nev,
                    t.Helyszin,
                    t.KezdoDatum,
                    t.VegDatum,
                    t.NapokSzama,
                    tFogasok.Sum(x => x.EffektiveDb),
                    Math.Round(sulyos.Sum(x => x.SulyKg!.Value), 2),
                    sulyos.Count > 0 ? Math.Round(sulyos.Max(x => x.SulyKg!.Value), 2) : 0.0,
                    tKoltseg);
            })
            .ToList();

        var helyszinekSzama = evTurai
            .Select(t => t.Helyszin.Trim())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Count();

        return new EvVegiOsszesito
        {
            Ev = ev,
            TurakSzama = evTurai.Count,
            OsszesTuraNap = evTurai.Sum(t => t.NapokSzama),
            HelyszinekSzama = helyszinekSzama,
            OsszesKoltseg = evesOsszKoltseg,
            Statisztika = statisztika,
            TuraBontas = turaBontas
        };
    }

    private static TuraStatisztikaOsszesito BuildCoreStatisztika(
        IReadOnlyCollection<Fogas> fogasok,
        IReadOnlyList<HorgaszRanglistaElem> horgaszRanglista,
        Dictionary<Guid, string> resztvevoMap,
        Dictionary<Guid, string>? turaNevMap,
        Dictionary<string, string> kategoriaLookup,
        decimal turaOsszKoltseg,
        decimal koltsegPerFo)
    {
        string ResolveKategoriaNev(string halfaj)
        {
            var trimmed = halfaj?.Trim() ?? string.Empty;
            if (trimmed.Length > 0 && kategoriaLookup.TryGetValue(trimmed, out var kat))
            {
                return kat;
            }

            return "Egyéb halfajok";
        }

        // Legnagyobb súlyra mért hal
        var legnagyobbFogas = fogasok
            .Where(f => !f.DarabAlapuE && f.SulyKg.HasValue)
            .OrderByDescending(f => f.SulyKg!.Value)
            .ThenByDescending(f => f.HosszCm ?? 0)
            .Select(f => new LegnagyobbFogasStat(
                f.Halfaj,
                ResolveKategoriaNev(f.Halfaj),
                Math.Round(f.SulyKg!.Value, 2),
                Math.Round(f.HosszCm ?? 0, 1),
                resztvevoMap.TryGetValue(f.ResztvevoId, out var nev) ? nev : "Ismeretlen horgász",
                f.Csali,
                f.Idopont,
                turaNevMap is not null && turaNevMap.TryGetValue(f.TuraId, out var tNev) ? tNev : null))
            .FirstOrDefault();

        // Csalik eredményessége (külön kg és db rangsor)
        var csaliStatisztikak = fogasok
            .GroupBy(f => f.Csali.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sulyos = g.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).ToList();
                return new CsaliStat(
                    g.First().Csali.Trim(),
                    g.Sum(x => x.EffektiveDb),
                    Math.Round(sulyos.Sum(x => x.SulyKg!.Value), 2),
                    sulyos.Count > 0 ? Math.Round(sulyos.Max(x => x.SulyKg!.Value), 2) : 0.0);
            })
            .OrderByDescending(c => c.OsszSulyKg)
            .ThenByDescending(c => c.FogasokSzama)
            .ToList();

        var legeredmenyesebbCsaliKg = csaliStatisztikak
            .Where(c => c.OsszSulyKg > 0)
            .OrderByDescending(c => c.OsszSulyKg)
            .ThenByDescending(c => c.LegnagyobbSulyKg)
            .ThenByDescending(c => c.FogasokSzama)
            .FirstOrDefault();

        var legeredmenyesebbCsaliDb = csaliStatisztikak
            .OrderByDescending(c => c.FogasokSzama)
            .ThenByDescending(c => c.OsszSulyKg)
            .FirstOrDefault();

        // Napszakok aktivitása (külön kg és db rangsor + top főkategóriák)
        var napszakStatisztikak = fogasok
            .GroupBy(f => GetNapszakNev(f.Idopont))
            .Select(g =>
            {
                var topKatok = g
                    .GroupBy(x => ResolveKategoriaNev(x.Halfaj), StringComparer.OrdinalIgnoreCase)
                    .Select(kg => new { Kat = kg.Key, Db = kg.Sum(z => z.EffektiveDb) })
                    .OrderByDescending(x => x.Db)
                    .Take(3)
                    .Select(x => $"{x.Kat} ({x.Db} db)");

                return new NapszakStat(
                    g.Key,
                    g.Sum(x => x.EffektiveDb),
                    Math.Round(g.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).Sum(x => x.SulyKg!.Value), 2),
                    string.Join(", ", topKatok));
            })
            .OrderByDescending(n => n.FogasokSzama)
            .ThenByDescending(n => n.OsszSulyKg)
            .ToList();

        var legaktivabbNapszakKg = napszakStatisztikak
            .Where(n => n.OsszSulyKg > 0)
            .OrderByDescending(n => n.OsszSulyKg)
            .ThenByDescending(n => n.FogasokSzama)
            .FirstOrDefault();

        var legaktivabbNapszakDb = napszakStatisztikak
            .OrderByDescending(n => n.FogasokSzama)
            .ThenByDescending(n => n.OsszSulyKg)
            .FirstOrDefault();

        // Halfajok és főkategóriák szerinti 2 szintű összesítés
        var halfajStatisztikak = fogasok
            .GroupBy(f => f.Halfaj.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var nev = g.First().Halfaj.Trim();
                var sulyos = g.Where(x => !x.DarabAlapuE && x.SulyKg.HasValue).ToList();
                return new HalfajStat(
                    nev,
                    ResolveKategoriaNev(nev),
                    g.Sum(x => x.EffektiveDb),
                    Math.Round(sulyos.Sum(x => x.SulyKg!.Value), 2),
                    sulyos.Count > 0 ? Math.Round(sulyos.Max(x => x.SulyKg!.Value), 2) : 0.0);
            })
            .OrderByDescending(h => h.FogasokSzama)
            .ThenByDescending(h => h.OsszSulyKg)
            .ToList();

        var halKategoriaStatisztikak = halfajStatisztikak
            .GroupBy(h => h.KategoriaNev, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var alLista = g
                    .OrderByDescending(a => a.FogasokSzama)
                    .ThenByDescending(a => a.OsszSulyKg)
                    .ToList();

                return new HalKategoriaStat(
                    g.Key,
                    alLista.Sum(a => a.FogasokSzama),
                    Math.Round(alLista.Sum(a => a.OsszSulyKg), 2),
                    alLista.Count > 0 ? Math.Round(alLista.Max(a => a.LegnagyobbSulyKg), 2) : 0.0,
                    alLista);
            })
            .OrderByDescending(k => k.FogasokSzama)
            .ThenByDescending(k => k.OsszSulyKg)
            .ToList();

        var osszesFogasDb = fogasok.Sum(f => f.EffektiveDb);
        var visszaengedettDb = fogasok.Where(f => f.VisszaengedettE).Sum(f => f.EffektiveDb);
        var megtartottFogasok = fogasok.Where(f => !f.VisszaengedettE).ToList();
        var megtartottDb = megtartottFogasok.Sum(f => f.EffektiveDb);
        var megtartottSuly = Math.Round(
            megtartottFogasok.Where(f => !f.DarabAlapuE && f.SulyKg.HasValue).Sum(f => f.SulyKg!.Value), 2);

        // Megtartott halak személyenkénti bontása
        var megtartottBontas = horgaszRanglista
            .Where(h => h.MegtartottDb > 0)
            .OrderByDescending(h => h.MegtartottDb)
            .ThenByDescending(h => h.MegtartottSulyKg)
            .ThenBy(h => h.Nev, StringComparer.CurrentCultureIgnoreCase)
            .Select(h => new MegtartottHorgaszStat(h.Nev, h.MegtartottDb, h.MegtartottSulyKg))
            .ToList();

        return new TuraStatisztikaOsszesito
        {
            OsszesFogasDb = osszesFogasDb,
            OsszesFogottSulyKg = Math.Round(
                fogasok.Where(f => !f.DarabAlapuE && f.SulyKg.HasValue).Sum(f => f.SulyKg!.Value), 2),
            VisszaengedettFogasDb = visszaengedettDb,
            MegtartottFogasDb = megtartottDb,
            MegtartottSulyKg = megtartottSuly,
            TuraOsszKoltseg = turaOsszKoltseg,
            KoltsegPerFo = koltsegPerFo,
            LegnagyobbHal = legnagyobbFogas,
            LegeredmenyesebbCsaliKg = legeredmenyesebbCsaliKg,
            LegeredmenyesebbCsaliDb = legeredmenyesebbCsaliDb,
            LegaktivabbNapszakKg = legaktivabbNapszakKg,
            LegaktivabbNapszakDb = legaktivabbNapszakDb,
            MegtartottBontas = megtartottBontas,
            HorgaszRanglista = horgaszRanglista,
            CsaliStatisztikak = csaliStatisztikak,
            NapszakStatisztikak = napszakStatisztikak,
            HalKategoriaStatisztikak = halKategoriaStatisztikak,
            HalfajStatisztikak = halfajStatisztikak
        };
    }

    private static Dictionary<string, string> BuildKategoriaLookup(IReadOnlyCollection<HalKategoriaCsoport>? halKategoriak)
    {
        var katalogus = halKategoriak is { Count: > 0 }
            ? halKategoriak
            : HalKatalogusDefaults.GetDefaultCsoportok();

        var kategoriaLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csoport in katalogus)
        {
            kategoriaLookup[csoport.Kategoria.Nev.Trim()] = csoport.Kategoria.Nev;
            foreach (var alkat in csoport.Alkategoriak)
            {
                kategoriaLookup[alkat.Nev.Trim()] = csoport.Kategoria.Nev;
            }
        }

        return kategoriaLookup;
    }

    private static string GetNapszakNev(DateTime idopont) => idopont.Hour switch
    {
        >= 4 and < 8 => "Hajnal (04:00–07:59)",
        >= 8 and < 12 => "Reggel / Délelőtt (08:00–11:59)",
        >= 12 and < 18 => "Délután (12:00–17:59)",
        >= 18 and < 22 => "Este (18:00–21:59)",
        _ => "Éjszaka (22:00–03:59)"
    };
}
