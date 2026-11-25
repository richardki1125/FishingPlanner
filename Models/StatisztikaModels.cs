namespace FishingPlanner.Models;

/// <summary>
/// Egy horgásztúra (vagy teljes év) fogási statisztikáit és horgász ranglistáját tároló eredményobjektum.
/// </summary>
public sealed class TuraStatisztikaOsszesito
{
    public int OsszesFogasDb { get; init; }
    public double OsszesFogottSulyKg { get; init; }
    public int VisszaengedettFogasDb { get; init; }
    public int MegtartottFogasDb { get; init; }
    public double MegtartottSulyKg { get; init; }
    public decimal TuraOsszKoltseg { get; init; }
    public decimal KoltsegPerFo { get; init; }

    public LegnagyobbFogasStat? LegnagyobbHal { get; init; }

    // Legeredményesebb csali külön kg-ra és db-ra bontva
    public CsaliStat? LegeredmenyesebbCsaliKg { get; init; }
    public CsaliStat? LegeredmenyesebbCsaliDb { get; init; }

    // Legaktívabb napszak külön kg-ra és db-ra bontva
    public NapszakStat? LegaktivabbNapszakKg { get; init; }
    public NapszakStat? LegaktivabbNapszakDb { get; init; }

    public IReadOnlyList<MegtartottHorgaszStat> MegtartottBontas { get; init; } = [];
    public IReadOnlyList<HorgaszRanglistaElem> HorgaszRanglista { get; init; } = [];
    public IReadOnlyList<CsaliStat> CsaliStatisztikak { get; init; } = [];
    public IReadOnlyList<NapszakStat> NapszakStatisztikak { get; init; } = [];
    public IReadOnlyList<HalKategoriaStat> HalKategoriaStatisztikak { get; init; } = [];
    public IReadOnlyList<HalfajStat> HalfajStatisztikak { get; init; } = [];
}

/// <summary>
/// Egy kiválasztott naptári év összes túrájának összesített év végi statisztikája.
/// </summary>
public sealed class EvVegiOsszesito
{
    public int Ev { get; init; }
    public int TurakSzama { get; init; }
    public int OsszesTuraNap { get; init; }
    public int HelyszinekSzama { get; init; }
    public decimal OsszesKoltseg { get; init; }
    public TuraStatisztikaOsszesito Statisztika { get; init; } = new();
    public IReadOnlyList<EvVegiTuraSor> TuraBontas { get; init; } = [];
}

public sealed record EvVegiTuraSor(
    Guid TuraId,
    string Nev,
    string Helyszin,
    DateTime KezdoDatum,
    DateTime VegDatum,
    int NapokSzama,
    int FogasokSzama,
    double OsszSulyKg,
    double LegnagyobbHalKg,
    decimal OsszKoltseg);

public sealed record HorgaszRanglistaElem(
    int Helyezes,
    Guid ResztvevoId,
    string Nev,
    string Szerepkor,
    int FogasokSzama,
    double OsszSulyKg,
    double LegnagyobbHalKg,
    double AtlagSulyKg,
    int MegtartottDb = 0,
    double MegtartottSulyKg = 0.0);

public sealed record MegtartottHorgaszStat(
    string Nev,
    int MegtartottDb,
    double MegtartottSulyKg);

public sealed record LegnagyobbFogasStat(
    string Halfaj,
    string? KategoriaNev,
    double SulyKg,
    double HosszCm,
    string HorgaszNev,
    string Csali,
    DateTime Idopont,
    string? TuraNev = null);

public sealed record CsaliStat(
    string Csali,
    int FogasokSzama,
    double OsszSulyKg,
    double LegnagyobbSulyKg);

public sealed record NapszakStat(
    string Napszak,
    int FogasokSzama,
    double OsszSulyKg,
    string? TopFajokSzoveg = null);

public sealed record HalKategoriaStat(
    string KategoriaNev,
    int FogasokSzama,
    double OsszSulyKg,
    double LegnagyobbSulyKg,
    IReadOnlyList<HalfajStat> Alkategoriak);

public sealed record HalfajStat(
    string Halfaj,
    string KategoriaNev,
    int FogasokSzama,
    double OsszSulyKg,
    double LegnagyobbSulyKg);
