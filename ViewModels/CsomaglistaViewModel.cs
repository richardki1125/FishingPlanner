using FishingPlanner.Models;
using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

public enum CsomagSzuroMod
{
    Mind,
    CsakKozos,
    AdottSzemely
}

public enum CsomagNezetMod
{
    TuraCsomaglista,
    Sablonok
}

/// <summary>
/// A csomaglista (túra-specifikus lista + újrahasználható csomaglista sablonok) állapotkezeléséért és CRUD műveleteiért felelős ViewModel.
/// </summary>
public sealed class CsomaglistaViewModel : ViewModelBase
{
    private readonly ITuraService _turaService;
    private readonly ISettingsService _settingsService;
    private readonly IActiveTuraState _activeTuraState;

    public static readonly IReadOnlyList<string> Kategoriak =
    [
        "Botok és orsók",
        "Táborozás",
        "Etetés és csali",
        "Csónakázás",
        "Konyha",
        "Ruházat",
        "Elektronika",
        "Egyéb"
    ];

    public CsomagNezetMod AktivNezet { get; private set; } = CsomagNezetMod.TuraCsomaglista;

    // --- Túra csomaglista adatok ---
    public List<Tura> Turak { get; private set; } = [];
    public Tura? AktualisTura { get; private set; }
    private List<Resztvevo> _osszesResztvevo = [];
    public List<Resztvevo> Resztvevok { get; private set; } = [];
    public List<FelszerelesTetel> Felszerelesek { get; private set; } = [];

    // --- Újrahasználható csomaglisták (sablonok) adatok ---
    public List<CsomaglistaSablon> Sablonok { get; private set; } = [];
    public List<CsomaglistaSablonTetel> SablonTetelek { get; private set; } = [];
    public Guid? KivalasztottSablonId { get; private set; }
    public bool SablonTablaHianyzik { get; private set; }

    public CsomaglistaSablon? KivalasztottSablon =>
        KivalasztottSablonId.HasValue
            ? Sablonok.FirstOrDefault(s => s.Id == KivalasztottSablonId.Value)
            : null;

    public IReadOnlyList<CsomaglistaSablonTetel> KivalasztottSablonTetelei =>
        KivalasztottSablonId.HasValue
            ? SablonTetelek.Where(t => t.SablonId == KivalasztottSablonId.Value).ToList()
            : [];

    // Keresés és szűrés (túra csomaglista)
    private string _keresesiKifejezes = string.Empty;
    public string KeresesiKifejezes
    {
        get => _keresesiKifejezes;
        set
        {
            _keresesiKifejezes = value;
            NotifyStateChanged();
        }
    }

    public CsomagSzuroMod SzuroMod { get; private set; } = CsomagSzuroMod.Mind;
    public Guid? SzurtResztvevoId { get; private set; }

    private string _szurtKategoria = string.Empty;
    public string SzurtKategoria
    {
        get => _szurtKategoria;
        set
        {
            _szurtKategoria = value ?? string.Empty;
            NotifyStateChanged();
        }
    }

    public bool CsakNemBepakoltak { get; private set; }

    public IReadOnlyList<string> ElerhetoKategoriak => Kategoriak
        .Concat(Felszerelesek.Select(f => f.Kategoria).Where(k => !string.IsNullOrWhiteSpace(k)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    // Túra felszerelés űrlap állapot
    public bool IsFormOpen { get; private set; }
    public bool IsEditMode { get; private set; }
    public FelszerelesTetel SzerkesztettTetel { get; private set; } = new();
    public FelszerelesTetel? TorlesreVaroTetel { get; private set; }

    // Sablon létrehozás / szerkesztés modal
    public bool IsSablonFormOpen { get; private set; }
    public bool IsSablonEditMode { get; private set; }
    public CsomaglistaSablon SzerkesztettSablon { get; private set; } = new();
    public CsomaglistaSablon? TorlesreVaroSablon { get; private set; }

    // Sablon tétel létrehozás / szerkesztés modal
    public bool IsSablonTetelFormOpen { get; private set; }
    public bool IsSablonTetelEditMode { get; private set; }
    public CsomaglistaSablonTetel SzerkesztettSablonTetel { get; private set; } = new();
    public CsomaglistaSablonTetel? TorlesreVaroSablonTetel { get; private set; }

    // Sablon(ok) hozzáadása meglévő túrához modal
    public bool IsImportSablonOpen { get; private set; }
    public HashSet<Guid> ImportKivalasztottSablonIds { get; } = [];
    public string ImportEgyeniResztvevoIdString { get; set; } = string.Empty;

    // Aktuális túra mentése sablonként modal
    public bool IsSaveAsSablonOpen { get; private set; }
    public CsomaglistaSablon MentesSablonForm { get; private set; } = new();

    public CsomaglistaViewModel(
        ITuraService turaService,
        ISettingsService settingsService,
        IActiveTuraState activeTuraState,
        ILogger<CsomaglistaViewModel> logger)
        : base(logger)
    {
        _turaService = turaService ?? throw new ArgumentNullException(nameof(turaService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _activeTuraState = activeTuraState ?? throw new ArgumentNullException(nameof(activeTuraState));
    }

    public IReadOnlyList<FelszerelesTetel> SzurtFelszerelesek
    {
        get
        {
            IEnumerable<FelszerelesTetel> query = Felszerelesek;

            query = SzuroMod switch
            {
                CsomagSzuroMod.CsakKozos => query.Where(f => f.KozosE),
                CsomagSzuroMod.AdottSzemely when SzurtResztvevoId.HasValue =>
                    query.Where(f => !f.KozosE && f.ResztvevoId == SzurtResztvevoId.Value),
                CsomagSzuroMod.AdottSzemely => query.Where(f => !f.KozosE),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(SzurtKategoria))
            {
                query = query.Where(f =>
                    string.Equals(f.Kategoria, SzurtKategoria, StringComparison.OrdinalIgnoreCase));
            }

            if (CsakNemBepakoltak)
            {
                query = query.Where(f => !f.Bepakolva);
            }

            if (!string.IsNullOrWhiteSpace(KeresesiKifejezes))
            {
                var term = KeresesiKifejezes.Trim();
                query = query.Where(f =>
                    f.Nev.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    f.Kategoria.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    GetTulajdonosNev(f).Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }
    }

    public int KozosDb => Felszerelesek.Count(f => f.KozosE);
    public int EgyeniDb => Felszerelesek.Count(f => !f.KozosE);
    public int OsszesMennyisegDb => Felszerelesek.Sum(f => f.Mennyiseg);
    public int BepakoltDb => Felszerelesek.Count(f => f.Bepakolva);
    public int OsszesDb => Felszerelesek.Count;
    public double BepakolasiSzazalek => OsszesDb > 0 ? Math.Round((double)BepakoltDb / OsszesDb * 100.0, 1) : 0.0;

    public int GetSablonTetelSzam(Guid sablonId) =>
        SablonTetelek.Count(t => t.SablonId == sablonId);

    public int ImportKivalasztottTetelSzam =>
        SablonTetelek.Count(t => ImportKivalasztottSablonIds.Contains(t.SablonId));

    public void SetNezet(CsomagNezetMod mod)
    {
        if (AktivNezet == mod)
        {
            return;
        }

        AktivNezet = mod;
        ClearMessages();
        NotifyStateChanged();
    }

    public void SelectSablon(Guid sablonId)
    {
        KivalasztottSablonId = sablonId;
        NotifyStateChanged();
    }

    public async Task LoadAsync(Guid? routeTuraId = null, bool openSablonokTab = false, CancellationToken ct = default)
    {
        if (openSablonokTab)
        {
            AktivNezet = CsomagNezetMod.Sablonok;
        }

        await ExecuteAsync(async () =>
        {
            var turak = await _turaService.GetTurakAsync(ct);
            Turak = turak.ToList();

            var targetId = routeTuraId ?? _activeTuraState.SelectedTuraId ?? Turak.FirstOrDefault()?.Id;
            if (targetId.HasValue && Turak.All(t => t.Id != targetId.Value))
            {
                targetId = Turak.FirstOrDefault()?.Id;
            }

            await Task.WhenAll(
                LoadSelectedTuraDataAsync(targetId, ct),
                LoadSablonokSafeAsync(ct));
        }, "Nem sikerült betölteni a csomaglistát.", useFullPageLoading: true);
    }

    /// <summary>
    /// Háttérben vagy lehúzásra (pull-to-refresh) történő csendes adatfrissítés oldalvillanás nélkül.
    /// </summary>
    public async Task RefreshSilentlyAsync(bool isManual = false, CancellationToken ct = default)
    {
        if (IsLoading || IsBusy)
        {
            return;
        }

        if (!isManual && HasAnyOpenModal)
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

            await Task.WhenAll(
                LoadSelectedTuraDataAsync(targetId, ct),
                LoadSablonokSafeAsync(ct));
            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            // Normál megszakítás
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Csendes frissítés sikertelen a csomaglistán.");
        }
    }

    private bool HasAnyOpenModal =>
        IsFormOpen ||
        IsSablonFormOpen ||
        IsSablonTetelFormOpen ||
        IsImportSablonOpen ||
        IsSaveAsSablonOpen ||
        TorlesreVaroTetel is not null ||
        TorlesreVaroSablon is not null ||
        TorlesreVaroSablonTetel is not null;

    public async Task ChangeTuraAsync(Guid turaId, CancellationToken ct = default)
    {
        ClearMessages();
        IsFormOpen = false;
        SzuroMod = CsomagSzuroMod.Mind;
        SzurtResztvevoId = null;
        _szurtKategoria = string.Empty;

        await ExecuteAsync(
            () => LoadSelectedTuraDataAsync(turaId, ct),
            "Nem sikerült betölteni a kiválasztott túra csomaglistáját.",
            useFullPageLoading: true);
    }

    private async Task LoadSelectedTuraDataAsync(Guid? turaId, CancellationToken ct)
    {
        _activeTuraState.SetSelectedTura(turaId);
        AktualisTura = turaId.HasValue ? Turak.FirstOrDefault(t => t.Id == turaId.Value) : null;

        if (AktualisTura is null)
        {
            _osszesResztvevo = [];
            Resztvevok = [];
            Felszerelesek = [];
            return;
        }

        var resztvevokTask = _turaService.GetResztvevokAsync(AktualisTura.Id, ct);
        var felszerelesekTask = _turaService.GetFelszerelesekAsync(AktualisTura.Id, ct);
        await Task.WhenAll(resztvevokTask, felszerelesekTask);

        _osszesResztvevo = (await resztvevokTask).ToList();
        Resztvevok = _osszesResztvevo.Where(r => r.AktivE).ToList();
        Felszerelesek = (await felszerelesekTask).ToList();
    }

    private async Task LoadSablonokSafeAsync(CancellationToken ct)
    {
        try
        {
            var sablonokTask = _turaService.GetCsomaglistaSablonokAsync(ct);
            var tetelekTask = _turaService.GetAllCsomaglistaSablonTetelekAsync(ct);
            await Task.WhenAll(sablonokTask, tetelekTask);

            Sablonok = (await sablonokTask).ToList();
            SablonTetelek = (await tetelekTask).ToList();
            SablonTablaHianyzik = false;

            if (KivalasztottSablonId is null || Sablonok.All(s => s.Id != KivalasztottSablonId.Value))
            {
                KivalasztottSablonId = Sablonok.FirstOrDefault()?.Id;
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("add_csomaglista_sablonok.sql", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning(ex, "A csomaglista sablon táblák még nincsenek létrehozva.");
            SablonTablaHianyzik = true;
            Sablonok = [];
            SablonTetelek = [];
            KivalasztottSablonId = null;
        }
    }

    public string GetTulajdonosNev(FelszerelesTetel tetel)
    {
        if (tetel.KozosE || tetel.ResztvevoId is null)
        {
            return "Közös felszerelés";
        }

        return _osszesResztvevo.FirstOrDefault(r => r.Id == tetel.ResztvevoId)?.Nev ?? "Ismeretlen résztvevő";
    }

    // --- Szűrés parancsok ---

    public void SetFilterMind()
    {
        SzuroMod = CsomagSzuroMod.Mind;
        SzurtResztvevoId = null;
        NotifyStateChanged();
    }

    public void SetFilterCsakKozos()
    {
        SzuroMod = CsomagSzuroMod.CsakKozos;
        SzurtResztvevoId = null;
        NotifyStateChanged();
    }

    public void SetFilterSzemely(Guid? resztvevoId)
    {
        SzuroMod = CsomagSzuroMod.AdottSzemely;
        SzurtResztvevoId = resztvevoId;
        NotifyStateChanged();
    }

    public void ToggleCsakNemBepakoltak()
    {
        CsakNemBepakoltak = !CsakNemBepakoltak;
        NotifyStateChanged();
    }

    public void ResetFilters()
    {
        _keresesiKifejezes = string.Empty;
        SzuroMod = CsomagSzuroMod.Mind;
        SzurtResztvevoId = null;
        _szurtKategoria = string.Empty;
        CsakNemBepakoltak = false;
        NotifyStateChanged();
    }

    // --- Túra felszerelés CRUD + Pipálás ---

    public void OpenCreateForm()
    {
        if (AktualisTura is null)
        {
            return;
        }

        ClearMessages();
        SzerkesztettTetel = new FelszerelesTetel
        {
            Id = Guid.NewGuid(),
            TuraId = AktualisTura.Id,
            KozosE = true,
            ResztvevoId = null,
            Kategoria = Kategoriak[0],
            Mennyiseg = 1,
            Bepakolva = false
        };
        IsEditMode = false;
        IsFormOpen = true;
        NotifyStateChanged();
    }

    public void OpenEditForm(FelszerelesTetel tetel)
    {
        ClearMessages();
        SzerkesztettTetel = new FelszerelesTetel
        {
            Id = tetel.Id,
            TuraId = tetel.TuraId,
            ResztvevoId = tetel.KozosE ? null : tetel.ResztvevoId,
            Nev = tetel.Nev,
            Kategoria = tetel.Kategoria,
            Mennyiseg = tetel.Mennyiseg,
            Bepakolva = tetel.Bepakolva,
            KozosE = tetel.KozosE
        };
        IsEditMode = true;
        IsFormOpen = true;
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
                await _turaService.UpdateFelszerelesAsync(SzerkesztettTetel, ct);
            }
            else
            {
                await _turaService.AddFelszerelesAsync(SzerkesztettTetel, ct);
            }

            IsFormOpen = false;
            Felszerelesek = (await _turaService.GetFelszerelesekAsync(AktualisTura.Id, ct)).ToList();
        }, IsEditMode ? "Nem sikerült módosítani a felszerelést." : "Nem sikerült hozzáadni a felszerelést.");

        if (ok)
        {
            SetSuccess(IsEditMode ? "A felszerelés frissült." : "Új felszerelés hozzáadva a csomaglistához.");
        }
    }

    public async Task ToggleBepakolvaAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        if (AktualisTura is null)
        {
            return;
        }

        var ujAllapot = !tetel.Bepakolva;
        await ExecuteAsync(async () =>
        {
            await _turaService.ToggleBepakolvaAsync(tetel.Id, ujAllapot, ct);
            tetel.Bepakolva = ujAllapot;
        }, "Nem sikerült frissíteni a bepakolási állapotot.");
    }

    public async Task RequestDeleteAsync(FelszerelesTetel tetel, CancellationToken ct = default)
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

    private async Task DeleteInternalAsync(FelszerelesTetel tetel, CancellationToken ct)
    {
        if (AktualisTura is null)
        {
            return;
        }

        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.DeleteFelszerelesAsync(tetel.Id, ct);
            Felszerelesek = (await _turaService.GetFelszerelesekAsync(AktualisTura.Id, ct)).ToList();
        }, "Nem sikerült törölni a felszerelési tételt.");

        if (ok)
        {
            SetSuccess($"A(z) „{tetel.Nev}” tétel törölve lett.");
        }
    }

    // --- Újrahasználható csomaglisták (sablonok) CRUD ---

    public void OpenCreateSablonForm()
    {
        ClearMessages();
        SzerkesztettSablon = new CsomaglistaSablon
        {
            Id = Guid.NewGuid()
        };
        IsSablonEditMode = false;
        IsSablonFormOpen = true;
        NotifyStateChanged();
    }

    public void OpenEditSablonForm(CsomaglistaSablon sablon)
    {
        ClearMessages();
        SzerkesztettSablon = new CsomaglistaSablon
        {
            Id = sablon.Id,
            Nev = sablon.Nev,
            Leiras = sablon.Leiras,
            CreatedAt = sablon.CreatedAt
        };
        IsSablonEditMode = true;
        IsSablonFormOpen = true;
        NotifyStateChanged();
    }

    public void CloseSablonForm()
    {
        IsSablonFormOpen = false;
        NotifyStateChanged();
    }

    public async Task SaveSablonAsync(CancellationToken ct = default)
    {
        Guid? savedId = null;

        var ok = await ExecuteAsync(async () =>
        {
            if (IsSablonEditMode)
            {
                var updated = await _turaService.UpdateCsomaglistaSablonAsync(SzerkesztettSablon, ct);
                savedId = updated.Id;
            }
            else
            {
                var created = await _turaService.CreateCsomaglistaSablonAsync(SzerkesztettSablon, ct);
                savedId = created.Id;
            }

            IsSablonFormOpen = false;
            await LoadSablonokSafeAsync(ct);
            if (savedId.HasValue)
            {
                KivalasztottSablonId = savedId.Value;
            }
        }, IsSablonEditMode ? "Nem sikerült módosítani a csomaglistát." : "Nem sikerült létrehozni a csomaglistát.");

        if (ok)
        {
            SetSuccess(IsSablonEditMode ? "A csomaglista adatai frissültek." : "Új újrahasználható csomaglista létrejött.");
        }
    }

    public void RequestDeleteSablon(CsomaglistaSablon sablon)
    {
        ClearMessages();
        TorlesreVaroSablon = sablon;
        NotifyStateChanged();
    }

    public void CancelDeleteSablon()
    {
        TorlesreVaroSablon = null;
        NotifyStateChanged();
    }

    public async Task ConfirmDeleteSablonAsync(CancellationToken ct = default)
    {
        if (TorlesreVaroSablon is null)
        {
            return;
        }

        var target = TorlesreVaroSablon;
        TorlesreVaroSablon = null;

        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.DeleteCsomaglistaSablonAsync(target.Id, ct);
            await LoadSablonokSafeAsync(ct);
        }, "Nem sikerült törölni a csomaglistát.");

        if (ok)
        {
            SetSuccess($"A(z) „{target.Nev}” csomaglista törölve lett.");
        }
    }

    // --- Sablon tételek CRUD ---

    public void OpenCreateSablonTetelForm(Guid sablonId)
    {
        ClearMessages();
        KivalasztottSablonId = sablonId;
        SzerkesztettSablonTetel = new CsomaglistaSablonTetel
        {
            Id = Guid.NewGuid(),
            SablonId = sablonId,
            Kategoria = Kategoriak[0],
            Mennyiseg = 1,
            KozosE = true
        };
        IsSablonTetelEditMode = false;
        IsSablonTetelFormOpen = true;
        NotifyStateChanged();
    }

    public void OpenEditSablonTetelForm(CsomaglistaSablonTetel tetel)
    {
        ClearMessages();
        SzerkesztettSablonTetel = new CsomaglistaSablonTetel
        {
            Id = tetel.Id,
            SablonId = tetel.SablonId,
            Nev = tetel.Nev,
            Kategoria = tetel.Kategoria,
            Mennyiseg = tetel.Mennyiseg,
            KozosE = tetel.KozosE
        };
        IsSablonTetelEditMode = true;
        IsSablonTetelFormOpen = true;
        NotifyStateChanged();
    }

    public void OnSablonTetelKozosChanged(bool kozosE)
    {
        SzerkesztettSablonTetel.KozosE = kozosE;
        NotifyStateChanged();
    }

    public void CloseSablonTetelForm()
    {
        IsSablonTetelFormOpen = false;
        NotifyStateChanged();
    }

    public async Task SaveSablonTetelAsync(CancellationToken ct = default)
    {
        var ok = await ExecuteAsync(async () =>
        {
            if (IsSablonTetelEditMode)
            {
                await _turaService.UpdateCsomaglistaSablonTetelAsync(SzerkesztettSablonTetel, ct);
            }
            else
            {
                await _turaService.AddCsomaglistaSablonTetelAsync(SzerkesztettSablonTetel, ct);
            }

            IsSablonTetelFormOpen = false;
            await LoadSablonokSafeAsync(ct);
        }, IsSablonTetelEditMode ? "Nem sikerült módosítani a tételt." : "Nem sikerült hozzáadni a tételt a csomaglistához.");

        if (ok)
        {
            SetSuccess(IsSablonTetelEditMode ? "A tétel frissült." : "Új tétel hozzáadva a csomaglistához.");
        }
    }

    public void RequestDeleteSablonTetel(CsomaglistaSablonTetel tetel)
    {
        ClearMessages();
        TorlesreVaroSablonTetel = tetel;
        NotifyStateChanged();
    }

    public void CancelDeleteSablonTetel()
    {
        TorlesreVaroSablonTetel = null;
        NotifyStateChanged();
    }

    public async Task ConfirmDeleteSablonTetelAsync(CancellationToken ct = default)
    {
        if (TorlesreVaroSablonTetel is null)
        {
            return;
        }

        var target = TorlesreVaroSablonTetel;
        TorlesreVaroSablonTetel = null;

        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.DeleteCsomaglistaSablonTetelAsync(target.Id, ct);
            await LoadSablonokSafeAsync(ct);
        }, "Nem sikerült törölni a tételt.");

        if (ok)
        {
            SetSuccess($"A(z) „{target.Nev}” tétel törölve lett a sablonból.");
        }
    }

    // --- Sablon(ok) hozzáadása az aktív túrához ---

    public void OpenImportSablonModal(Guid? preselectedSablonId = null)
    {
        if (AktualisTura is null)
        {
            SetError("Először válassz vagy hozz létre egy túrát!");
            return;
        }

        ClearMessages();
        ImportKivalasztottSablonIds.Clear();
        if (preselectedSablonId.HasValue)
        {
            ImportKivalasztottSablonIds.Add(preselectedSablonId.Value);
        }

        ImportEgyeniResztvevoIdString = Resztvevok.FirstOrDefault()?.Id.ToString("D") ?? string.Empty;
        IsImportSablonOpen = true;
        NotifyStateChanged();
    }

    public void ToggleImportSablon(Guid sablonId)
    {
        if (!ImportKivalasztottSablonIds.Add(sablonId))
        {
            ImportKivalasztottSablonIds.Remove(sablonId);
        }

        NotifyStateChanged();
    }

    public void CloseImportSablonModal()
    {
        IsImportSablonOpen = false;
        NotifyStateChanged();
    }

    public async Task ApplySelectedSablonokToTuraAsync(CancellationToken ct = default)
    {
        if (AktualisTura is null)
        {
            return;
        }

        if (ImportKivalasztottSablonIds.Count == 0)
        {
            SetError("Válassz ki legalább egy csomaglistát!");
            return;
        }

        var tetelek = SablonTetelek
            .Where(t => ImportKivalasztottSablonIds.Contains(t.SablonId))
            .ToList();

        if (tetelek.Count == 0)
        {
            SetError("A kiválasztott csomaglisták még nem tartalmaznak tételeket.");
            return;
        }

        Guid? egyeniTulajdonosId = Guid.TryParse(ImportEgyeniResztvevoIdString, out var parsedId) && parsedId != Guid.Empty
            ? parsedId
            : Resztvevok.FirstOrDefault()?.Id;

        var listaCount = ImportKivalasztottSablonIds.Count;
        var hozzaadottDb = 0;

        var ok = await ExecuteAsync(async () =>
        {
            foreach (var st in tetelek)
            {
                var legyenKozos = st.KozosE || !egyeniTulajdonosId.HasValue;
                await _turaService.AddFelszerelesAsync(new FelszerelesTetel
                {
                    Id = Guid.NewGuid(),
                    TuraId = AktualisTura.Id,
                    Nev = st.Nev,
                    Kategoria = st.Kategoria,
                    Mennyiseg = Math.Max(1, st.Mennyiseg),
                    KozosE = legyenKozos,
                    ResztvevoId = legyenKozos ? null : egyeniTulajdonosId,
                    Bepakolva = false
                }, ct);
                hozzaadottDb++;
            }

            IsImportSablonOpen = false;
            AktivNezet = CsomagNezetMod.TuraCsomaglista;
            Felszerelesek = (await _turaService.GetFelszerelesekAsync(AktualisTura.Id, ct)).ToList();
        }, "Nem sikerült hozzáadni a kiválasztott csomaglistákat a túrához.");

        if (ok)
        {
            SetSuccess($"{hozzaadottDb} tétel hozzáadva a túrához ({listaCount} csomaglistából).");
        }
    }

    // --- Aktuális túra csomaglistájának mentése új sablonként ---

    public void OpenSaveAsSablonModal()
    {
        if (AktualisTura is null || Felszerelesek.Count == 0)
        {
            return;
        }

        ClearMessages();
        MentesSablonForm = new CsomaglistaSablon
        {
            Id = Guid.NewGuid(),
            Nev = $"{AktualisTura.Nev} lista",
            Leiras = $"Mentve a(z) {AktualisTura.Nev} túrából ({Felszerelesek.Count} tétel)"
        };
        IsSaveAsSablonOpen = true;
        NotifyStateChanged();
    }

    public void CloseSaveAsSablonModal()
    {
        IsSaveAsSablonOpen = false;
        NotifyStateChanged();
    }

    public async Task SaveCurrentTuraAsSablonAsync(CancellationToken ct = default)
    {
        if (AktualisTura is null || Felszerelesek.Count == 0)
        {
            return;
        }

        var tetelDb = Felszerelesek.Count;
        Guid? ujSablonId = null;

        var ok = await ExecuteAsync(async () =>
        {
            var created = await _turaService.CreateCsomaglistaSablonAsync(MentesSablonForm, ct);
            ujSablonId = created.Id;

            foreach (var f in Felszerelesek)
            {
                await _turaService.AddCsomaglistaSablonTetelAsync(new CsomaglistaSablonTetel
                {
                    Id = Guid.NewGuid(),
                    SablonId = created.Id,
                    Nev = f.Nev,
                    Kategoria = f.Kategoria,
                    Mennyiseg = Math.Max(1, f.Mennyiseg),
                    KozosE = f.KozosE
                }, ct);
            }

            IsSaveAsSablonOpen = false;
            await LoadSablonokSafeAsync(ct);
            if (ujSablonId.HasValue)
            {
                KivalasztottSablonId = ujSablonId.Value;
            }
        }, "Nem sikerült sablonként elmenteni a túra csomaglistáját.");

        if (ok)
        {
            SetSuccess($"A(z) „{MentesSablonForm.Nev}” csomaglista elmentve ({tetelDb} tétel) — mostantól új túrákhoz is hozzáadható!");
        }
    }
}
