using System.Globalization;
using FishingPlanner.Models;
using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

public enum KoltsegSzuroMod
{
    Mind,
    CsakKozos,
    AdottSzemely
}

/// <summary>
/// A túrák anyagi tervezéséért, költségmezők kezeléséért és személyenkénti leosztásáért felelős ViewModel.
/// </summary>
public sealed class KoltsegekViewModel : ViewModelBase
{
    private static readonly CultureInfo HuCulture = CultureInfo.GetCultureInfo("hu-HU");

    private readonly ITuraService _turaService;
    private readonly ISettingsService _settingsService;
    private readonly IActiveTuraState _activeTuraState;

    public static readonly IReadOnlyList<string> Penznemek = ["Ft", "EUR", "RON"];

    public static readonly IReadOnlyList<string> KategoriaJavaslatok =
    [
        "Ételek",
        "Italok",
        "Etetőanyag és csali",
        "Üzemanyag / Utazás",
        "Horgászjegy / Napijegy",
        "Szállás / Helyfoglalás",
        "Felszerelés",
        "Egyéb"
    ];

    public static readonly IReadOnlyList<string> GyorsKezdoKategoriak =
    [
        "Ételek",
        "Italok",
        "Etetőanyag és csali",
        "Üzemanyag / Utazás",
        "Horgászjegy / Napijegy",
        "Szállás / Helyfoglalás"
    ];

    public List<Tura> Turak { get; private set; } = [];
    public Tura? AktualisTura { get; private set; }

    private List<Resztvevo> _osszesResztvevo = [];
    private Dictionary<Guid, RegisztraltFelhasznalo> _profilokByUserId = [];

    public List<Resztvevo> Resztvevok { get; private set; } = [];
    public List<KoltsegTetel> Koltsegek { get; private set; } = [];
    public string Penznem { get; private set; } = "Ft";

    // Keresés és szűrés
    private string _keresesiKifejezes = string.Empty;
    public string KeresesiKifejezes
    {
        get => _keresesiKifejezes;
        set
        {
            _keresesiKifejezes = value ?? string.Empty;
            NotifyStateChanged();
        }
    }

    public KoltsegSzuroMod SzuroMod { get; private set; } = KoltsegSzuroMod.Mind;
    public Guid? SzurtResztvevoId { get; private set; }

    // Űrlap állapot
    public bool IsFormOpen { get; private set; }
    public bool IsEditMode { get; private set; }
    public KoltsegTetel SzerkesztettTetel { get; private set; } = new();

    public string SelectedResztvevoIdString
    {
        get => SzerkesztettTetel.ResztvevoId?.ToString("D") ?? string.Empty;
        set => SzerkesztettTetel.ResztvevoId = Guid.TryParse(value, out var id) ? id : null;
    }

    public string SelectedFizetoIdString
    {
        get => SzerkesztettTetel.FizetoResztvevoId?.ToString("D") ?? string.Empty;
        set => SzerkesztettTetel.FizetoResztvevoId = Guid.TryParse(value, out var id) ? id : null;
    }

    // Törlés megerősítés
    public KoltsegTetel? TorlesreVaroTetel { get; private set; }

    public KoltsegekViewModel(
        ITuraService turaService,
        ISettingsService settingsService,
        IActiveTuraState activeTuraState,
        ILogger<KoltsegekViewModel> logger)
        : base(logger)
    {
        _turaService = turaService ?? throw new ArgumentNullException(nameof(turaService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _activeTuraState = activeTuraState ?? throw new ArgumentNullException(nameof(activeTuraState));
    }

    // --- Összesítők és KPI-k ---

    public decimal OsszKoltseg => Koltsegek.Sum(k => k.Osszeg);
    public decimal KozosOsszKoltseg => Koltsegek.Where(k => k.KozosE).Sum(k => k.Osszeg);
    public decimal EgyeniOsszKoltseg => Koltsegek.Where(k => !k.KozosE).Sum(k => k.Osszeg);
    public int AktivLetszam => Resztvevok.Count;

    public decimal KozosKoltsegPerFo => AktivLetszam > 0
        ? KozosOsszKoltseg / AktivLetszam
        : 0m;

    public decimal AtlagKoltsegPerFo => AktivLetszam > 0
        ? OsszKoltseg / AktivLetszam
        : 0m;

    public decimal NapiKoltseg => AktualisTura is not null && AktualisTura.NapokSzama > 0
        ? OsszKoltseg / AktualisTura.NapokSzama
        : OsszKoltseg;

    public bool VanFizetoMegadva => Koltsegek.Any(k => k.FizetoResztvevoId.HasValue);
    public bool MindenTetelnelVanFizeto => Koltsegek.Count > 0 && Koltsegek.All(k => k.FizetoResztvevoId.HasValue);

    public IReadOnlyList<KoltsegTetel> SzurtKoltsegek
    {
        get
        {
            IEnumerable<KoltsegTetel> query = Koltsegek;

            query = SzuroMod switch
            {
                KoltsegSzuroMod.CsakKozos => query.Where(k => k.KozosE),
                KoltsegSzuroMod.AdottSzemely when SzurtResztvevoId.HasValue =>
                    query.Where(k => !k.KozosE && k.ResztvevoId == SzurtResztvevoId.Value),
                KoltsegSzuroMod.AdottSzemely => query.Where(k => !k.KozosE),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(KeresesiKifejezes))
            {
                var term = KeresesiKifejezes.Trim();
                query = query.Where(k =>
                    k.Nev.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(k.Megjegyzes) && k.Megjegyzes.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    GetErintettNev(k).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (GetFizetoNev(k)?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return query.ToList();
        }
    }

    public IReadOnlyList<SzemelyKoltsegOsszesites> SzemelyenkentiLeosztas
    {
        get
        {
            if (_osszesResztvevo.Count == 0)
            {
                return [];
            }

            var erintettIds = Koltsegek
                .SelectMany(k => new[] { k.ResztvevoId, k.FizetoResztvevoId })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            var szereplok = _osszesResztvevo
                .Where(r => r.AktivE || erintettIds.Contains(r.Id))
                .ToList();

            if (szereplok.Count == 0)
            {
                return [];
            }

            var aktivCount = Resztvevok.Count;
            var kozosReszPerAktiv = aktivCount > 0 ? KozosOsszKoltseg / aktivCount : 0m;

            var validResztvevoIds = _osszesResztvevo.Select(r => r.Id).ToHashSet();
            var fizetettKozosOsszeg = aktivCount > 0
                ? Koltsegek.Where(k => k.KozosE && k.FizetoResztvevoId.HasValue && validResztvevoIds.Contains(k.FizetoResztvevoId.Value)).Sum(k => k.Osszeg)
                : 0m;
            var elszamolhatoKozosPerAktiv = aktivCount > 0 ? fizetettKozosOsszeg / aktivCount : 0m;

            var total = OsszKoltseg;
            var eredmeny = new List<SzemelyKoltsegOsszesites>(szereplok.Count);

            foreach (var r in szereplok)
            {
                var kozosResz = r.AktivE ? kozosReszPerAktiv : 0m;
                var egyeni = Koltsegek
                    .Where(k => !k.KozosE && k.ResztvevoId == r.Id)
                    .Sum(k => k.Osszeg);

                var osszes = kozosResz + egyeni;
                var befizetett = Koltsegek
                    .Where(k => k.FizetoResztvevoId == r.Id && (aktivCount > 0 || !k.KozosE))
                    .Sum(k => k.Osszeg);

                var elszamolhatoEgyeni = Koltsegek
                    .Where(k => !k.KozosE && k.ResztvevoId == r.Id && k.FizetoResztvevoId.HasValue && validResztvevoIds.Contains(k.FizetoResztvevoId.Value))
                    .Sum(k => k.Osszeg);

                var elszamolhato = (r.AktivE ? elszamolhatoKozosPerAktiv : 0m) + elszamolhatoEgyeni;
                var egyenleg = befizetett - elszamolhato;
                var arany = total > 0m ? Math.Round((double)(osszes / total) * 100.0, 1) : 0.0;

                eredmeny.Add(new SzemelyKoltsegOsszesites(
                    r.Id,
                    GetDisplayNev(r),
                    r.Szerepkor,
                    GetProfilkepUrl(r),
                    r.AktivE,
                    kozosResz,
                    egyeni,
                    osszes,
                    befizetett,
                    elszamolhato,
                    egyenleg,
                    arany));
            }

            return eredmeny
                .OrderByDescending(s => s.OsszesKoltseg)
                .ThenBy(s => s.Nev, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<ElszamolasiUtalas> ElszamolasiUtalasok
    {
        get
        {
            if (!VanFizetoMegadva)
            {
                return [];
            }

            var epsilon = string.Equals(Penznem, "Ft", StringComparison.OrdinalIgnoreCase) ? 1m : 0.01m;
            var decimals = string.Equals(Penznem, "Ft", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
            var lista = SzemelyenkentiLeosztas;

            var hitelezok = lista
                .Where(s => s.Egyenleg > epsilon)
                .OrderByDescending(s => s.Egyenleg)
                .Select(s => new { s.Nev, Osszeg = s.Egyenleg })
                .ToList();

            var adosok = lista
                .Where(s => s.Egyenleg < -epsilon)
                .OrderBy(s => s.Egyenleg)
                .Select(s => new { s.Nev, Osszeg = -s.Egyenleg })
                .ToList();

            if (hitelezok.Count == 0 || adosok.Count == 0)
            {
                return [];
            }

            var utalasok = new List<ElszamolasiUtalas>();
            var cBalances = hitelezok.Select(h => h.Osszeg).ToArray();
            var dBalances = adosok.Select(a => a.Osszeg).ToArray();

            var ci = 0;
            var di = 0;

            while (ci < cBalances.Length && di < dBalances.Length)
            {
                var rawAmount = Math.Min(cBalances[ci], dBalances[di]);
                var roundedAmount = Math.Round(rawAmount, decimals, MidpointRounding.AwayFromZero);

                if (roundedAmount >= epsilon)
                {
                    utalasok.Add(new ElszamolasiUtalas(adosok[di].Nev, hitelezok[ci].Nev, roundedAmount));
                }

                cBalances[ci] -= rawAmount;
                dBalances[di] -= rawAmount;

                if (cBalances[ci] <= epsilon)
                {
                    ci++;
                }

                if (dBalances[di] <= epsilon)
                {
                    di++;
                }
            }

            return utalasok;
        }
    }

    // --- Betöltés és frissítés ---

    public async Task LoadAsync(Guid? routeTuraId = null, CancellationToken ct = default)
    {
        await ExecuteAsync(async () =>
        {
            Penznem = await _settingsService.GetCurrencyAsync();
            var turak = await _turaService.GetTurakAsync(ct);
            Turak = turak.ToList();

            var targetId = routeTuraId ?? _activeTuraState.SelectedTuraId ?? Turak.FirstOrDefault()?.Id;
            if (targetId.HasValue && Turak.All(t => t.Id != targetId.Value))
            {
                targetId = Turak.FirstOrDefault()?.Id;
            }

            await LoadSelectedTuraDataAsync(targetId, ct);
        }, "Nem sikerült betölteni a túra költségeit.", useFullPageLoading: true);
    }

    public async Task RefreshSilentlyAsync(bool isManual = false, CancellationToken ct = default)
    {
        if (IsLoading || IsBusy)
        {
            return;
        }

        if (!isManual && (IsFormOpen || TorlesreVaroTetel is not null))
        {
            return;
        }

        try
        {
            var turak = await _turaService.GetTurakAsync(ct);
            Turak = turak.ToList();

            var targetId = AktualisTura?.Id ?? _activeTuraState.SelectedTuraId ?? Turak.FirstOrDefault()?.Id;
            if (targetId.HasValue && Turak.All(t => t.Id != targetId.Value))
            {
                targetId = Turak.FirstOrDefault()?.Id;
            }

            await LoadSelectedTuraDataAsync(targetId, ct);
            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            // Normál megszakítás
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Csendes frissítés sikertelen a költségek oldalon.");
        }
    }

    public async Task ChangeTuraAsync(Guid turaId, CancellationToken ct = default)
    {
        ClearMessages();
        IsFormOpen = false;
        SzuroMod = KoltsegSzuroMod.Mind;
        SzurtResztvevoId = null;

        await ExecuteAsync(
            () => LoadSelectedTuraDataAsync(turaId, ct),
            "Nem sikerült betölteni a kiválasztott túra költségeit.",
            useFullPageLoading: true);
    }

    public async Task SetPenznemAsync(string penznem)
    {
        if (string.Equals(Penznem, penznem, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _settingsService.SetCurrencyAsync(penznem);
        Penznem = await _settingsService.GetCurrencyAsync();
        NotifyStateChanged();
    }

    private async Task LoadSelectedTuraDataAsync(Guid? turaId, CancellationToken ct)
    {
        _activeTuraState.SetSelectedTura(turaId);
        AktualisTura = turaId.HasValue ? Turak.FirstOrDefault(t => t.Id == turaId.Value) : null;

        if (AktualisTura is null)
        {
            _osszesResztvevo = [];
            Resztvevok = [];
            Koltsegek = [];
            return;
        }

        var resztvevokTask = _turaService.GetResztvevokAsync(AktualisTura.Id, ct);
        var koltsegekTask = _turaService.GetKoltsegekAsync(AktualisTura.Id, ct);
        var profilesTask = TryLoadProfilesAsync(ct);

        await Task.WhenAll(resztvevokTask, koltsegekTask, profilesTask);

        _osszesResztvevo = (await resztvevokTask).ToList();
        Resztvevok = _osszesResztvevo.Where(r => r.AktivE).ToList();
        Koltsegek = (await koltsegekTask).ToList();
        _profilokByUserId = await profilesTask;
    }

    private async Task<Dictionary<Guid, RegisztraltFelhasznalo>> TryLoadProfilesAsync(CancellationToken ct)
    {
        try
        {
            var users = await _turaService.GetRegisztraltFelhasznalokAsync(ct);
            return users.ToDictionary(u => u.Id, u => u);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "A felhasználói profilok betöltése sikertelen a költségek nézetben.");
            return _profilokByUserId;
        }
    }

    // --- Megjelenítő és formázó segédek ---

    public string GetDisplayNev(Resztvevo resztvevo)
    {
        if (resztvevo.UserId.HasValue &&
            _profilokByUserId.TryGetValue(resztvevo.UserId.Value, out var profil))
        {
            return profil.MegjelenitesiNev;
        }

        return resztvevo.Nev;
    }

    public string? GetProfilkepUrl(Resztvevo resztvevo)
    {
        if (resztvevo.UserId.HasValue &&
            _profilokByUserId.TryGetValue(resztvevo.UserId.Value, out var profil))
        {
            return profil.ProfilkepUrl;
        }

        return null;
    }

    public string GetErintettNev(KoltsegTetel tetel)
    {
        if (tetel.KozosE || tetel.ResztvevoId is null)
        {
            return "Közös (mindenki)";
        }

        var r = _osszesResztvevo.FirstOrDefault(x => x.Id == tetel.ResztvevoId.Value);
        return r is not null ? GetDisplayNev(r) : "Ismeretlen résztvevő";
    }

    public string? GetFizetoNev(KoltsegTetel tetel)
    {
        if (!tetel.FizetoResztvevoId.HasValue)
        {
            return null;
        }

        var r = _osszesResztvevo.FirstOrDefault(x => x.Id == tetel.FizetoResztvevoId.Value);
        return r is not null ? GetDisplayNev(r) : null;
    }

    public string FormatOsszeg(decimal osszeg)
    {
        if (string.Equals(Penznem, "Ft", StringComparison.OrdinalIgnoreCase))
        {
            return $"{Math.Round(osszeg, 0, MidpointRounding.AwayFromZero).ToString("N0", HuCulture)} Ft";
        }

        var rounded = Math.Round(osszeg, 2, MidpointRounding.AwayFromZero);
        var formatted = rounded == decimal.Truncate(rounded)
            ? rounded.ToString("N0", HuCulture)
            : rounded.ToString("N2", HuCulture);

        return $"{formatted} {Penznem}";
    }

    public string FormatEgyenleg(decimal egyenleg)
    {
        var epsilon = string.Equals(Penznem, "Ft", StringComparison.OrdinalIgnoreCase) ? 1m : 0.01m;
        if (Math.Abs(egyenleg) < epsilon)
        {
            return "Kiegyenlítve";
        }

        var prefix = egyenleg > 0m ? "+" : "-";
        return $"{prefix}{FormatOsszeg(Math.Abs(egyenleg))}";
    }

    // --- Szűrés parancsok ---

    public void SetFilterMind()
    {
        SzuroMod = KoltsegSzuroMod.Mind;
        SzurtResztvevoId = null;
        NotifyStateChanged();
    }

    public void SetFilterCsakKozos()
    {
        SzuroMod = KoltsegSzuroMod.CsakKozos;
        SzurtResztvevoId = null;
        NotifyStateChanged();
    }

    public void SetFilterSzemely(Guid? resztvevoId)
    {
        SzuroMod = KoltsegSzuroMod.AdottSzemely;
        SzurtResztvevoId = resztvevoId;
        NotifyStateChanged();
    }

    public void ResetFilters()
    {
        _keresesiKifejezes = string.Empty;
        SzuroMod = KoltsegSzuroMod.Mind;
        SzurtResztvevoId = null;
        NotifyStateChanged();
    }

    // --- CRUD műveletek ---

    public void OpenCreateForm(string? javasoltNev = null)
    {
        if (AktualisTura is null)
        {
            return;
        }

        ClearMessages();
        SzerkesztettTetel = new KoltsegTetel
        {
            Id = Guid.NewGuid(),
            TuraId = AktualisTura.Id,
            Nev = javasoltNev ?? string.Empty,
            Osszeg = 0m,
            KozosE = true,
            ResztvevoId = null,
            FizetoResztvevoId = null,
            Megjegyzes = null
        };
        IsEditMode = false;
        IsFormOpen = true;
        NotifyStateChanged();
    }

    public void OpenEditForm(KoltsegTetel tetel)
    {
        ClearMessages();
        SzerkesztettTetel = new KoltsegTetel
        {
            Id = tetel.Id,
            TuraId = tetel.TuraId,
            Nev = tetel.Nev,
            Osszeg = tetel.Osszeg,
            KozosE = tetel.KozosE,
            ResztvevoId = tetel.KozosE ? null : tetel.ResztvevoId,
            FizetoResztvevoId = tetel.FizetoResztvevoId,
            Megjegyzes = tetel.Megjegyzes
        };
        IsEditMode = true;
        IsFormOpen = true;
        NotifyStateChanged();
    }

    public void ApplyKategoriaJavaslat(string nev)
    {
        SzerkesztettTetel.Nev = nev;
        NotifyStateChanged();
    }

    public void OnKozosChanged(bool kozosE)
    {
        SzerkesztettTetel.KozosE = kozosE;
        if (kozosE)
        {
            SzerkesztettTetel.ResztvevoId = null;
        }
        else if (SzerkesztettTetel.ResztvevoId is null && Resztvevok.Count > 0)
        {
            SzerkesztettTetel.ResztvevoId = Resztvevok[0].Id;
        }

        NotifyStateChanged();
    }

    public void CloseForm()
    {
        IsFormOpen = false;
        NotifyStateChanged();
    }

    public async Task SaveTetelAsync(CancellationToken ct = default)
    {
        if (AktualisTura is null)
        {
            return;
        }

        SzerkesztettTetel.TuraId = AktualisTura.Id;
        if (SzerkesztettTetel.KozosE)
        {
            SzerkesztettTetel.ResztvevoId = null;
        }

        var ok = await ExecuteAsync(async () =>
        {
            if (IsEditMode)
            {
                await _turaService.UpdateKoltsegAsync(SzerkesztettTetel, ct);
            }
            else
            {
                await _turaService.AddKoltsegAsync(SzerkesztettTetel, ct);
            }

            IsFormOpen = false;
            Koltsegek = (await _turaService.GetKoltsegekAsync(AktualisTura.Id, ct)).ToList();
        }, IsEditMode ? "Nem sikerült módosítani a költségmezőt." : "Nem sikerült hozzáadni a költségmezőt.");

        if (ok)
        {
            SetSuccess(IsEditMode ? "A költségmező frissült." : "Új költségmező rögzítve.");
        }
    }

    public async Task RequestDeleteAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ClearMessages();
        var confirmRequired = await _settingsService.GetConfirmBeforeDeleteAsync();
        if (confirmRequired)
        {
            TorlesreVaroTetel = tetel;
            NotifyStateChanged();
            return;
        }

        await DeleteInternalAsync(tetel, ct);
    }

    public void CancelDelete()
    {
        TorlesreVaroTetel = null;
        NotifyStateChanged();
    }

    public async Task ConfirmDeleteAsync(CancellationToken ct = default)
    {
        if (TorlesreVaroTetel is null)
        {
            return;
        }

        var target = TorlesreVaroTetel;
        TorlesreVaroTetel = null;
        await DeleteInternalAsync(target, ct);
    }

    private async Task DeleteInternalAsync(KoltsegTetel tetel, CancellationToken ct)
    {
        if (AktualisTura is null)
        {
            return;
        }

        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.DeleteKoltsegAsync(tetel.Id, ct);
            Koltsegek = (await _turaService.GetKoltsegekAsync(AktualisTura.Id, ct)).ToList();
        }, "Nem sikerült törölni a költségmezőt.");

        if (ok)
        {
            SetSuccess($"A(z) „{tetel.Nev}” költségmező törölve lett.");
        }
    }
}
