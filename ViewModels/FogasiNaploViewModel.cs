using FishingPlanner.Models;
using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

public enum FogasAllapotSzuro
{
    Mind,
    Visszaengedett,
    Megtartott
}

public enum FogasRendezesMod
{
    DatumCsokkeno,
    DatumNovekvo,
    LegnagyobbElol
}

/// <summary>
/// A fogási napló állapotkezelését, képfeltöltését (MediaPicker) és CRUD műveleteit végző ViewModel.
/// </summary>
public sealed class FogasiNaploViewModel : ViewModelBase
{
    private readonly ITuraService _turaService;
    private readonly IMediaService _mediaService;
    private readonly IActiveTuraState _activeTuraState;

    public const string EgyeniHalfajKulcs = "__egyeni__";

    public List<Tura> Turak { get; private set; } = [];
    public Tura? AktualisTura { get; private set; }
    private List<Resztvevo> _osszesResztvevo = [];
    public List<Resztvevo> Resztvevok { get; private set; } = [];
    public List<Fogas> Fogasok { get; private set; } = [];
    public List<RegisztraltFelhasznalo> Profilok { get; private set; } = [];
    public List<HalKategoriaCsoport> HalKategoriak { get; private set; } = HalKatalogusDefaults.GetDefaultCsoportok();

    // 2 szintű halfaj választó állapot (Főkategória -> Alkategória)
    public string KivalasztottKategoriaId { get; private set; } = string.Empty;
    public string KivalasztottAlkategoriaId { get; private set; } = string.Empty;
    public bool EgyeniHalfajMod => string.Equals(KivalasztottKategoriaId, EgyeniHalfajKulcs, StringComparison.Ordinal);
    private bool _felhasznaloValasztottHalat;

    public IReadOnlyList<HalAlkategoria> AktivAlkategoriak
    {
        get
        {
            if (EgyeniHalfajMod || !Guid.TryParse(KivalasztottKategoriaId, out var katId))
            {
                return [];
            }

            return HalKategoriak.FirstOrDefault(g => g.Kategoria.Id == katId)?.Alkategoriak ?? [];
        }
    }

    // Szűrés
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

    public Guid? SzurtResztvevoId { get; private set; }
    public FogasAllapotSzuro AllapotSzuro { get; private set; } = FogasAllapotSzuro.Mind;
    public FogasRendezesMod RendezesMod { get; private set; } = FogasRendezesMod.DatumCsokkeno;

    // Űrlap állapot
    public bool IsFormOpen { get; private set; }
    public bool IsEditMode { get; private set; }
    public Fogas SzerkesztettFogas { get; private set; } = new();
    private string? _eredetiKepUrl;
    public bool IsCaptureSupported => _mediaService.IsCaptureSupported;

    // Törlés megerősítés
    public Fogas? TorlesreVaroFogas { get; private set; }

    // Teljes képernyős galéria képnézegető (Lightbox)
    public Fogas? MegtekintettKepFogas { get; private set; }
    public bool IsPhotoZoomed { get; private set; }

    public IReadOnlyList<Fogas> KepesFogasok =>
        SzurtFogasok.Where(f => !string.IsNullOrWhiteSpace(f.KepUrl)).ToList();

    public int MegtekintettKepIndex
    {
        get
        {
            if (MegtekintettKepFogas is null)
            {
                return -1;
            }

            var lista = KepesFogasok;
            for (var i = 0; i < lista.Count; i++)
            {
                if (lista[i].Id == MegtekintettKepFogas.Id)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public FogasiNaploViewModel(
        ITuraService turaService,
        IMediaService mediaService,
        IActiveTuraState activeTuraState,
        ILogger<FogasiNaploViewModel> logger)
        : base(logger)
    {
        _turaService = turaService ?? throw new ArgumentNullException(nameof(turaService));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _activeTuraState = activeTuraState ?? throw new ArgumentNullException(nameof(activeTuraState));
    }

    public IReadOnlyList<Fogas> SzurtFogasok
    {
        get
        {
            IEnumerable<Fogas> query = Fogasok;

            if (SzurtResztvevoId.HasValue)
            {
                query = query.Where(f => f.ResztvevoId == SzurtResztvevoId.Value);
            }

            query = AllapotSzuro switch
            {
                FogasAllapotSzuro.Visszaengedett => query.Where(f => f.VisszaengedettE),
                FogasAllapotSzuro.Megtartott => query.Where(f => !f.VisszaengedettE),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(KeresesiKifejezes))
            {
                var term = KeresesiKifejezes.Trim();
                query = query.Where(f =>
                    f.Halfaj.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (GetHalKategoriaNev(f.Halfaj)?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    f.Csali.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (f.Megjegyzes?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    GetHorgaszNev(f.ResztvevoId).Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return RendezesMod switch
            {
                FogasRendezesMod.DatumNovekvo => query
                    .OrderBy(f => f.Idopont)
                    .ToList(),
                FogasRendezesMod.LegnagyobbElol => query
                    .OrderByDescending(f => !f.DarabAlapuE && f.SulyKg.HasValue ? f.SulyKg.Value : -1.0)
                    .ThenByDescending(f => f.HosszCm ?? 0)
                    .ThenByDescending(f => f.EffektiveDb)
                    .ThenByDescending(f => f.Idopont)
                    .ToList(),
                _ => query
                    .OrderByDescending(f => f.Idopont)
                    .ToList()
            };
        }
    }

    public int OsszesHalDb => Fogasok.Sum(f => f.EffektiveDb);
    public int VisszaengedettHalDb => Fogasok.Where(f => f.VisszaengedettE).Sum(f => f.EffektiveDb);
    public int MegtartottHalDb => Fogasok.Where(f => !f.VisszaengedettE).Sum(f => f.EffektiveDb);

    public double OsszesFogottSulyKg => Math.Round(
        Fogasok.Where(f => !f.DarabAlapuE && f.SulyKg.HasValue).Sum(f => f.SulyKg!.Value), 2);

    public double LegnagyobbSulyKg
    {
        get
        {
            var sulyok = Fogasok.Where(f => !f.DarabAlapuE && f.SulyKg.HasValue).Select(f => f.SulyKg!.Value).ToList();
            return sulyok.Count > 0 ? Math.Round(sulyok.Max(), 2) : 0.0;
        }
    }

    public async Task LoadAsync(Guid? routeTuraId = null, CancellationToken ct = default)
    {
        await ExecuteAsync(async () =>
        {
            var turakTask = _turaService.GetTurakAsync(ct);
            var kategoriakTask = _turaService.GetHalKategoriakAsync(ct);
            var profilokTask = _turaService.GetRegisztraltFelhasznalokAsync(ct);
            await Task.WhenAll(turakTask, kategoriakTask, profilokTask);

            Turak = (await turakTask).ToList();
            Profilok = (await profilokTask).ToList();
            var betoltottKategoriak = (await kategoriakTask).ToList();
            if (betoltottKategoriak.Count > 0)
            {
                HalKategoriak = betoltottKategoriak;
            }

            var targetId = routeTuraId ?? _activeTuraState.SelectedTuraId ?? Turak.FirstOrDefault()?.Id;
            if (targetId.HasValue && Turak.All(t => t.Id != targetId.Value))
            {
                targetId = Turak.FirstOrDefault()?.Id;
            }

            await LoadSelectedTuraDataAsync(targetId, ct);
        }, "Nem sikerült betölteni a fogási naplót.", useFullPageLoading: true);
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

        if (!isManual && (IsFormOpen || TorlesreVaroFogas is not null || MegtekintettKepFogas is not null))
        {
            return;
        }

        try
        {
            var turakTask = _turaService.GetTurakAsync(ct);
            var kategoriakTask = _turaService.GetHalKategoriakAsync(ct);
            var profilokTask = _turaService.GetRegisztraltFelhasznalokAsync(ct);
            await Task.WhenAll(turakTask, kategoriakTask, profilokTask);

            Turak = (await turakTask).ToList();
            Profilok = (await profilokTask).ToList();
            var betoltottKategoriak = (await kategoriakTask).ToList();
            if (betoltottKategoriak.Count > 0)
            {
                HalKategoriak = betoltottKategoriak;
            }

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
            Logger.LogWarning(ex, "Csendes frissítés sikertelen a fogási naplón.");
        }
    }

    public async Task ChangeTuraAsync(Guid turaId, CancellationToken ct = default)
    {
        ClearMessages();
        IsFormOpen = false;
        SzurtResztvevoId = null;

        await ExecuteAsync(
            () => LoadSelectedTuraDataAsync(turaId, ct),
            "Nem sikerült betölteni a kiválasztott túra fogásait.",
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
            Fogasok = [];
            return;
        }

        var resztvevokTask = _turaService.GetResztvevokAsync(AktualisTura.Id, ct);
        var fogasokTask = _turaService.GetFogasokAsync(AktualisTura.Id, ct);
        await Task.WhenAll(resztvevokTask, fogasokTask);

        _osszesResztvevo = (await resztvevokTask).ToList();
        Resztvevok = _osszesResztvevo.Where(r => r.AktivE).ToList();
        Fogasok = (await fogasokTask).ToList();
    }

    public string GetHorgaszNev(Guid resztvevoId)
    {
        var resztvevo = _osszesResztvevo.FirstOrDefault(r => r.Id == resztvevoId);
        if (resztvevo is null)
        {
            return "Ismeretlen horgász";
        }

        var profil = FindProfilForResztvevo(resztvevo);
        return profil?.MegjelenitesiNev ?? resztvevo.Nev;
    }

    public string? GetHorgaszProfilkepUrl(Guid resztvevoId)
    {
        var resztvevo = _osszesResztvevo.FirstOrDefault(r => r.Id == resztvevoId);
        if (resztvevo is null)
        {
            return null;
        }

        return FindProfilForResztvevo(resztvevo)?.ProfilkepUrl;
    }

    private RegisztraltFelhasznalo? FindProfilForResztvevo(Resztvevo resztvevo)
    {
        if (resztvevo.UserId.HasValue)
        {
            var byId = Profilok.FirstOrDefault(p => p.Id == resztvevo.UserId.Value);
            if (byId is not null)
            {
                return byId;
            }
        }

        return Profilok.FirstOrDefault(p =>
            string.Equals(p.Felhasznalonev, resztvevo.Nev, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(p.MegjelenitettNev) &&
             string.Equals(p.MegjelenitettNev.Trim(), resztvevo.Nev.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Megkeresi a fogás halfajához tartozó főkategória nevét (pl. Tükörponty -> Ponty).
    /// </summary>
    public string? GetHalKategoriaNev(string halfaj)
    {
        if (string.IsNullOrWhiteSpace(halfaj))
        {
            return null;
        }

        var trimmed = halfaj.Trim();
        foreach (var csoport in HalKategoriak)
        {
            if (csoport.Alkategoriak.Any(a => string.Equals(a.Nev, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return csoport.Kategoria.Nev;
            }
        }

        return null;
    }

    public void OnKategoriaChanged(string? kategoriaId)
    {
        _felhasznaloValasztottHalat = true;
        var val = kategoriaId ?? string.Empty;
        KivalasztottKategoriaId = val;

        if (string.Equals(val, EgyeniHalfajKulcs, StringComparison.Ordinal))
        {
            KivalasztottAlkategoriaId = string.Empty;
            SzerkesztettFogas.Halfaj = string.Empty;
            NotifyStateChanged();
            return;
        }

        if (Guid.TryParse(val, out var katGuid))
        {
            var csoport = HalKategoriak.FirstOrDefault(g => g.Kategoria.Id == katGuid);
            var elsoAlkat = csoport?.Alkategoriak.FirstOrDefault();
            if (elsoAlkat is not null)
            {
                KivalasztottAlkategoriaId = elsoAlkat.Id.ToString("D");
                SzerkesztettFogas.Halfaj = elsoAlkat.Nev;
            }
            else if (csoport is not null)
            {
                KivalasztottAlkategoriaId = string.Empty;
                SzerkesztettFogas.Halfaj = csoport.Kategoria.Nev;
            }
        }

        NotifyStateChanged();
    }

    public void OnAlkategoriaChanged(string? alkategoriaId)
    {
        _felhasznaloValasztottHalat = true;
        var val = alkategoriaId ?? string.Empty;
        KivalasztottAlkategoriaId = val;

        if (Guid.TryParse(val, out var alGuid))
        {
            var alkat = AktivAlkategoriak.FirstOrDefault(a => a.Id == alGuid);
            if (alkat is not null)
            {
                SzerkesztettFogas.Halfaj = alkat.Nev;
            }
        }

        NotifyStateChanged();
    }

    public void SetResztvevoFilter(Guid? resztvevoId)
    {
        SzurtResztvevoId = resztvevoId;
        NotifyStateChanged();
    }

    public void SetAllapotSzuro(FogasAllapotSzuro szuro)
    {
        AllapotSzuro = szuro;
        NotifyStateChanged();
    }

    public void SetRendezesMod(string? modNev)
    {
        if (Enum.TryParse<FogasRendezesMod>(modNev, out var parsed))
        {
            RendezesMod = parsed;
            NotifyStateChanged();
        }
    }

    public void ResetFilters()
    {
        _keresesiKifejezes = string.Empty;
        SzurtResztvevoId = null;
        AllapotSzuro = FogasAllapotSzuro.Mind;
        RendezesMod = FogasRendezesMod.DatumCsokkeno;
        NotifyStateChanged();
    }

    // --- CRUD + Képfeltöltés ---

    public void OpenCreateForm()
    {
        if (AktualisTura is null)
        {
            return;
        }

        ClearMessages();
        _felhasznaloValasztottHalat = false;
        SzerkesztettFogas = new Fogas
        {
            Id = Guid.NewGuid(),
            TuraId = AktualisTura.Id,
            ResztvevoId = Resztvevok.FirstOrDefault()?.Id ?? Guid.Empty,
            Idopont = DateTime.Now,
            DarabAlapuE = false,
            Mennyiseg = 1,
            VisszaengedettE = true,
            SulyKg = 2.5,
            HosszCm = 45.0
        };
        SelectDefaultHalByCategoryName("Ponty");
        IsEditMode = false;
        _eredetiKepUrl = null;
        IsFormOpen = true;
        NotifyStateChanged();
    }

    public void OpenEditForm(Fogas fogas)
    {
        ClearMessages();
        _felhasznaloValasztottHalat = true;
        _eredetiKepUrl = fogas.KepUrl;
        SzerkesztettFogas = new Fogas
        {
            Id = fogas.Id,
            TuraId = fogas.TuraId,
            ResztvevoId = fogas.ResztvevoId,
            Halfaj = fogas.Halfaj,
            DarabAlapuE = fogas.DarabAlapuE,
            Mennyiseg = fogas.DarabAlapuE ? Math.Max(1, fogas.Mennyiseg) : 1,
            VisszaengedettE = fogas.VisszaengedettE,
            SulyKg = fogas.DarabAlapuE ? null : fogas.SulyKg,
            HosszCm = fogas.DarabAlapuE ? null : fogas.HosszCm,
            Idopont = fogas.Idopont,
            Csali = fogas.Csali,
            Megjegyzes = fogas.Megjegyzes,
            KepUrl = fogas.KepUrl
        };
        SyncHalSelectionFromHalfaj(fogas.Halfaj);
        IsEditMode = true;
        IsFormOpen = true;
        NotifyStateChanged();
    }

    public void SetMeresiMod(bool darabAlapuE)
    {
        SzerkesztettFogas.DarabAlapuE = darabAlapuE;
        if (darabAlapuE)
        {
            SzerkesztettFogas.SulyKg = null;
            SzerkesztettFogas.HosszCm = null;
            if (SzerkesztettFogas.Mennyiseg < 1)
            {
                SzerkesztettFogas.Mennyiseg = 1;
            }

            if (!IsEditMode && !_felhasznaloValasztottHalat)
            {
                SelectDefaultHalByCategoryName("Kárász");
            }
        }
        else
        {
            SzerkesztettFogas.Mennyiseg = 1;
            SzerkesztettFogas.SulyKg ??= 2.5;
            SzerkesztettFogas.HosszCm ??= 45.0;

            if (!IsEditMode && !_felhasznaloValasztottHalat)
            {
                SelectDefaultHalByCategoryName("Ponty");
            }
        }

        NotifyStateChanged();
    }

    private void SelectDefaultHalByCategoryName(string preferredCategoryName)
    {
        var csoport = HalKategoriak.FirstOrDefault(g =>
                string.Equals(g.Kategoria.Nev, preferredCategoryName, StringComparison.OrdinalIgnoreCase))
            ?? HalKategoriak.FirstOrDefault();

        if (csoport is null)
        {
            KivalasztottKategoriaId = EgyeniHalfajKulcs;
            KivalasztottAlkategoriaId = string.Empty;
            return;
        }

        KivalasztottKategoriaId = csoport.Kategoria.Id.ToString("D");
        var elsoAlkat = csoport.Alkategoriak.FirstOrDefault();
        if (elsoAlkat is not null)
        {
            KivalasztottAlkategoriaId = elsoAlkat.Id.ToString("D");
            SzerkesztettFogas.Halfaj = elsoAlkat.Nev;
        }
        else
        {
            KivalasztottAlkategoriaId = string.Empty;
            SzerkesztettFogas.Halfaj = csoport.Kategoria.Nev;
        }
    }

    private void SyncHalSelectionFromHalfaj(string halfaj)
    {
        var trimmed = halfaj?.Trim() ?? string.Empty;
        foreach (var csoport in HalKategoriak)
        {
            var match = csoport.Alkategoriak.FirstOrDefault(a =>
                string.Equals(a.Nev, trimmed, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                KivalasztottKategoriaId = csoport.Kategoria.Id.ToString("D");
                KivalasztottAlkategoriaId = match.Id.ToString("D");
                return;
            }
        }

        // Ha korábbi vagy egyedi halfajnév szerepel a bejegyzésben, egyéni módban megtartjuk pontosan
        KivalasztottKategoriaId = EgyeniHalfajKulcs;
        KivalasztottAlkategoriaId = string.Empty;
    }

    public void SetVisszaengedett(bool visszaengedettE)
    {
        SzerkesztettFogas.VisszaengedettE = visszaengedettE;
        NotifyStateChanged();
    }

    public void CloseForm()
    {
        IsFormOpen = false;
        NotifyStateChanged();
    }

    // --- Galéria képnézegető (Lightbox) ---

    public void OpenPhotoViewer(Fogas fogas)
    {
        if (string.IsNullOrWhiteSpace(fogas.KepUrl))
        {
            return;
        }

        MegtekintettKepFogas = fogas;
        IsPhotoZoomed = false;
        NotifyStateChanged();
    }

    public void ClosePhotoViewer()
    {
        MegtekintettKepFogas = null;
        IsPhotoZoomed = false;
        NotifyStateChanged();
    }

    public void TogglePhotoZoom()
    {
        IsPhotoZoomed = !IsPhotoZoomed;
        NotifyStateChanged();
    }

    public void ShowPreviousPhoto()
    {
        var lista = KepesFogasok;
        if (lista.Count <= 1)
        {
            return;
        }

        var idx = MegtekintettKepIndex;
        var prevIdx = idx <= 0 ? lista.Count - 1 : idx - 1;
        MegtekintettKepFogas = lista[prevIdx];
        IsPhotoZoomed = false;
        NotifyStateChanged();
    }

    public void ShowNextPhoto()
    {
        var lista = KepesFogasok;
        if (lista.Count <= 1)
        {
            return;
        }

        var idx = MegtekintettKepIndex;
        var nextIdx = idx < 0 || idx >= lista.Count - 1 ? 0 : idx + 1;
        MegtekintettKepFogas = lista[nextIdx];
        IsPhotoZoomed = false;
        NotifyStateChanged();
    }

    public async Task PickPhotoAsync(CancellationToken ct = default)
    {
        await ExecuteAsync(async () =>
        {
            var dataUri = await _mediaService.PickPhotoAsync(ct);
            if (!string.IsNullOrWhiteSpace(dataUri))
            {
                SzerkesztettFogas.KepUrl = dataUri;
            }
        }, "Nem sikerült kiválasztani a képet a galériából.");
    }

    public async Task CapturePhotoAsync(CancellationToken ct = default)
    {
        await ExecuteAsync(async () =>
        {
            var dataUri = await _mediaService.CapturePhotoAsync(ct);
            if (!string.IsNullOrWhiteSpace(dataUri))
            {
                SzerkesztettFogas.KepUrl = dataUri;
            }
        }, "Nem sikerült elkészíteni a fotót a kamerával.");
    }

    public void RemovePhoto()
    {
        SzerkesztettFogas.KepUrl = null;
        NotifyStateChanged();
    }

    public async Task SaveFogasAsync(CancellationToken ct = default)
    {
        if (AktualisTura is null)
        {
            return;
        }

        SzerkesztettFogas.TuraId = AktualisTura.Id;
        if (SzerkesztettFogas.DarabAlapuE)
        {
            SzerkesztettFogas.SulyKg = null;
            SzerkesztettFogas.HosszCm = null;
        }
        else
        {
            SzerkesztettFogas.Mennyiseg = 1;
        }

        var ok = await ExecuteAsync(async () =>
        {
            var regiKepTorlendo = IsEditMode &&
                !string.IsNullOrWhiteSpace(_eredetiKepUrl) &&
                !string.Equals(_eredetiKepUrl, SzerkesztettFogas.KepUrl, StringComparison.Ordinal);

            if (IsEditMode)
            {
                await _turaService.UpdateFogasAsync(SzerkesztettFogas, ct);
            }
            else
            {
                await _turaService.AddFogasAsync(SzerkesztettFogas, ct);
            }

            if (regiKepTorlendo)
            {
                await _mediaService.DeletePhotoAsync(_eredetiKepUrl, ct);
            }

            IsFormOpen = false;
            _eredetiKepUrl = null;
            Fogasok = (await _turaService.GetFogasokAsync(AktualisTura.Id, ct)).ToList();
        }, IsEditMode ? "Nem sikerült módosítani a fogást." : "Nem sikerült rögzíteni a fogást.");

        if (ok)
        {
            SetSuccess(IsEditMode ? "A fogás adatai frissültek." : "Új fogás rögzítve a naplóba!");
        }
    }

    public async Task IncrementMennyisegAsync(Fogas fogas, CancellationToken ct = default)
    {
        if (AktualisTura is null || !fogas.DarabAlapuE || fogas.Mennyiseg >= 500)
        {
            return;
        }

        var frissitett = new Fogas
        {
            Id = fogas.Id,
            TuraId = fogas.TuraId,
            ResztvevoId = fogas.ResztvevoId,
            Halfaj = fogas.Halfaj,
            DarabAlapuE = true,
            Mennyiseg = fogas.Mennyiseg + 1,
            VisszaengedettE = fogas.VisszaengedettE,
            SulyKg = null,
            HosszCm = null,
            Idopont = fogas.Idopont,
            Csali = fogas.Csali,
            Megjegyzes = fogas.Megjegyzes,
            KepUrl = fogas.KepUrl
        };

        await ExecuteAsync(async () =>
        {
            await _turaService.UpdateFogasAsync(frissitett, ct);
            fogas.Mennyiseg = frissitett.Mennyiseg;
        }, "Nem sikerült növelni a darabszámot.");
    }

    public Task RequestDeleteAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);
        ClearMessages();
        TorlesreVaroFogas = fogas;
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public void CancelDelete()
    {
        TorlesreVaroFogas = null;
        NotifyStateChanged();
    }

    public async Task ConfirmDeleteAsync(CancellationToken ct = default)
    {
        if (TorlesreVaroFogas is null)
        {
            return;
        }

        var target = TorlesreVaroFogas;
        TorlesreVaroFogas = null;
        await DeleteInternalAsync(target, ct);
    }

    private async Task DeleteInternalAsync(Fogas fogas, CancellationToken ct)
    {
        if (AktualisTura is null)
        {
            return;
        }

        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.DeleteFogasAsync(fogas.Id, ct);
            if (!string.IsNullOrWhiteSpace(fogas.KepUrl))
            {
                await _mediaService.DeletePhotoAsync(fogas.KepUrl, ct);
            }

            Fogasok = (await _turaService.GetFogasokAsync(AktualisTura.Id, ct)).ToList();
        }, "Nem sikerült törölni a fogást.");

        if (ok)
        {
            var mertek = fogas.DarabAlapuE ? $"{fogas.Mennyiseg} db" : $"{fogas.SulyKg:0.##} kg";
            SetSuccess($"A(z) {fogas.Halfaj} ({mertek}) bejegyzés törölve lett.");
        }
    }
}
