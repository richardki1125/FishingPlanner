using System.ComponentModel.DataAnnotations;
using FishingPlanner.Models;
using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

public enum TuraDatumSzuroMod
{
    Mind,
    AktualisEsKozelgo,
    Lejart
}

/// <summary>
/// A regisztrált felhasználók listájából történő résztvevő-hozzáadás űrlapmodellje.
/// </summary>
public sealed class AddResztvevoFormModel : IValidatableObject
{
    public string SelectedUserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "A szerepkör megadása kötelező.")]
    [StringLength(60, ErrorMessage = "A szerepkör legfeljebb 60 karakter lehet.")]
    public string Szerepkor { get; set; } = "Horgász";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Guid.TryParse(SelectedUserId, out var userId) || userId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Válassz ki egy regisztrált felhasználót a listából.",
                [nameof(SelectedUserId)]);
        }
    }
}

/// <summary>
/// A túrák listázását, dátumszűrését, CRUD műveleteit és a regisztrált résztvevők hozzárendelését végző ViewModel.
/// </summary>
public sealed class TuraListaViewModel : ViewModelBase
{
    private readonly ITuraService _turaService;
    private readonly ISettingsService _settingsService;
    private readonly IAuthService _authService;
    private readonly IActiveTuraState _activeTuraState;

    public List<Tura> Turak { get; private set; } = [];
    public List<RegisztraltFelhasznalo> RegisztraltFelhasznalok { get; private set; } = [];
    public Dictionary<Guid, List<Resztvevo>> ResztvevokTuraSzerint { get; private set; } = [];
    public List<CsomaglistaSablon> CsomaglistaSablonok { get; private set; } = [];
    public List<CsomaglistaSablonTetel> CsomaglistaSablonTetelek { get; private set; } = [];
    public HashSet<Guid> KivalasztottSablonIds { get; } = [];

    // Dátum szerinti szűrés és rendezés
    public TuraDatumSzuroMod DatumSzuroMod { get; private set; } = TuraDatumSzuroMod.Mind;

    private DateTime? _szuroDatumTol;
    public DateTime? SzuroDatumTol
    {
        get => _szuroDatumTol;
        set
        {
            _szuroDatumTol = value;
            NotifyStateChanged();
        }
    }

    private DateTime? _szuroDatumIg;
    public DateTime? SzuroDatumIg
    {
        get => _szuroDatumIg;
        set
        {
            _szuroDatumIg = value;
            NotifyStateChanged();
        }
    }

    public bool LegujabbElol { get; private set; } = true;

    public bool HasActiveFilters =>
        DatumSzuroMod != TuraDatumSzuroMod.Mind ||
        SzuroDatumTol.HasValue ||
        SzuroDatumIg.HasValue;

    public IReadOnlyList<Tura> SzurtTurak
    {
        get
        {
            IEnumerable<Tura> query = Turak;

            query = DatumSzuroMod switch
            {
                TuraDatumSzuroMod.AktualisEsKozelgo => query.Where(t => !t.LejartE),
                TuraDatumSzuroMod.Lejart => query.Where(t => t.LejartE),
                _ => query
            };

            if (SzuroDatumTol.HasValue)
            {
                var tol = SzuroDatumTol.Value.Date;
                query = query.Where(t => t.VegDatum.Date >= tol);
            }

            if (SzuroDatumIg.HasValue)
            {
                var ig = SzuroDatumIg.Value.Date;
                query = query.Where(t => t.KezdoDatum.Date <= ig);
            }

            query = LegujabbElol
                ? query.OrderByDescending(t => t.KezdoDatum)
                : query.OrderBy(t => t.KezdoDatum);

            return query.ToList();
        }
    }

    public int AktualisTurakDb => Turak.Count(t => !t.LejartE);
    public int LejartTurakDb => Turak.Count(t => t.LejartE);

    // Túra űrlap állapot
    public bool IsTuraFormOpen { get; private set; }
    public bool IsEditMode { get; private set; }
    public Tura SzerkesztettTura { get; private set; } = new();

    // Résztvevők kezelése panel
    public Tura? ResztvevoKezeltTura { get; private set; }
    public AddResztvevoFormModel ResztvevoForm { get; private set; } = new();

    // Törlés megerősítés állapot
    public Tura? TorlesreVaroTura { get; private set; }
    public Resztvevo? TorlesreVaroResztvevo { get; private set; }

    public TuraListaViewModel(
        ITuraService turaService,
        ISettingsService settingsService,
        IAuthService authService,
        IActiveTuraState activeTuraState,
        ILogger<TuraListaViewModel> logger)
        : base(logger)
    {
        _turaService = turaService ?? throw new ArgumentNullException(nameof(turaService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _activeTuraState = activeTuraState ?? throw new ArgumentNullException(nameof(activeTuraState));
    }

    public IReadOnlyList<RegisztraltFelhasznalo> ValaszthatoFelhasznalok
    {
        get
        {
            if (ResztvevoKezeltTura is null)
            {
                return [];
            }

            var marHozzaadottak = GetResztvevok(ResztvevoKezeltTura.Id);
            var marHozzaadottUserIdk = marHozzaadottak
                .Where(r => r.UserId.HasValue)
                .Select(r => r.UserId!.Value)
                .ToHashSet();
            var marHozzaadottNevek = marHozzaadottak
                .Select(r => r.Nev)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return RegisztraltFelhasznalok
                .Where(u => !marHozzaadottUserIdk.Contains(u.Id) && !marHozzaadottNevek.Contains(u.Felhasznalonev))
                .ToList();
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await ExecuteAsync(async () =>
        {
            await ReloadSilentlyAsync(ct);
        }, "Nem sikerült betölteni a horgásztúrákat.", useFullPageLoading: true);
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

        if (!isManual && (IsTuraFormOpen || ResztvevoKezeltTura is not null || TorlesreVaroTura is not null || TorlesreVaroResztvevo is not null))
        {
            return;
        }

        try
        {
            await ReloadSilentlyAsync(ct);
            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            // Normál megszakítás
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Csendes frissítés sikertelen a túralistán.");
        }
    }

    public IReadOnlyList<Resztvevo> GetResztvevok(Guid turaId) =>
        ResztvevokTuraSzerint.TryGetValue(turaId, out var lista)
            ? lista.Where(r => r.AktivE).ToList()
            : [];

    public void SelectTura(Guid turaId)
    {
        _activeTuraState.SetSelectedTura(turaId);
        NotifyStateChanged();
    }

    public Guid? ActiveTuraId => _activeTuraState.SelectedTuraId;

    public int GetSablonTetelSzam(Guid sablonId) =>
        CsomaglistaSablonTetelek.Count(t => t.SablonId == sablonId);

    public bool IsSablonKivalasztva(Guid sablonId) =>
        KivalasztottSablonIds.Contains(sablonId);

    public int KivalasztottSablonTetelekSzama =>
        CsomaglistaSablonTetelek.Count(t => KivalasztottSablonIds.Contains(t.SablonId));

    public void ToggleSablonKivalasztas(Guid sablonId)
    {
        if (!KivalasztottSablonIds.Add(sablonId))
        {
            KivalasztottSablonIds.Remove(sablonId);
        }

        NotifyStateChanged();
    }

    // --- Dátumszűrő parancsok ---

    public void SetDatumSzuroMod(TuraDatumSzuroMod mod)
    {
        DatumSzuroMod = mod;
        NotifyStateChanged();
    }

    public void ToggleRendezes()
    {
        LegujabbElol = !LegujabbElol;
        NotifyStateChanged();
    }

    public void ResetFilters()
    {
        DatumSzuroMod = TuraDatumSzuroMod.Mind;
        _szuroDatumTol = null;
        _szuroDatumIg = null;
        NotifyStateChanged();
    }

    // --- Túra CRUD ---

    public async Task OpenCreateTuraFormAsync()
    {
        ClearMessages();
        KivalasztottSablonIds.Clear();
        var defaultNapok = await _settingsService.GetDefaultNapokSzamaAsync();
        await LoadSablonokSafeAsync();
        SzerkesztettTura = new Tura
        {
            Id = Guid.NewGuid(),
            KezdoDatum = DateTime.Today,
            VegDatum = DateTime.Today.AddDays(Math.Max(0, defaultNapok - 1))
        };
        IsEditMode = false;
        IsTuraFormOpen = true;
        NotifyStateChanged();
    }

    public void OpenEditTuraForm(Tura tura)
    {
        ClearMessages();
        KivalasztottSablonIds.Clear();
        SzerkesztettTura = new Tura
        {
            Id = tura.Id,
            Nev = tura.Nev,
            Helyszin = tura.Helyszin,
            KezdoDatum = tura.KezdoDatum,
            VegDatum = tura.VegDatum
        };
        IsEditMode = true;
        IsTuraFormOpen = true;
        NotifyStateChanged();
    }

    public void CloseTuraForm()
    {
        IsTuraFormOpen = false;
        NotifyStateChanged();
    }

    public async Task SaveTuraAsync(CancellationToken ct = default)
    {
        var hozzaadottTetelDb = 0;
        var kivalasztottListaDb = KivalasztottSablonIds.Count;

        var ok = await ExecuteAsync(async () =>
        {
            Guid targetTuraId;
            Guid? alapResztvevoId = null;

            if (IsEditMode)
            {
                await _turaService.UpdateTuraAsync(SzerkesztettTura, ct);
                targetTuraId = SzerkesztettTura.Id;
                alapResztvevoId = GetResztvevok(targetTuraId).FirstOrDefault()?.Id;
            }
            else
            {
                var created = await _turaService.CreateTuraAsync(SzerkesztettTura, ct);
                targetTuraId = created.Id;
                _activeTuraState.SetSelectedTura(created.Id);

                var currentUser = _authService.CurrentUser;
                if (currentUser is not null && !string.IsNullOrWhiteSpace(currentUser.MegjelenitesiNev))
                {
                    var szervezo = await _turaService.AddResztvevoAsync(new Resztvevo
                    {
                        Id = Guid.NewGuid(),
                        TuraId = created.Id,
                        UserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
                        Nev = currentUser.MegjelenitesiNev,
                        Szerepkor = "Szervező"
                    }, ct);
                    alapResztvevoId = szervezo.Id;
                }
            }

            if (KivalasztottSablonIds.Count > 0)
            {
                var tetelek = CsomaglistaSablonTetelek
                    .Where(t => KivalasztottSablonIds.Contains(t.SablonId))
                    .ToList();

                foreach (var st in tetelek)
                {
                    var legyenKozos = st.KozosE || !alapResztvevoId.HasValue;
                    await _turaService.AddFelszerelesAsync(new FelszerelesTetel
                    {
                        Id = Guid.NewGuid(),
                        TuraId = targetTuraId,
                        Nev = st.Nev,
                        Kategoria = st.Kategoria,
                        Mennyiseg = Math.Max(1, st.Mennyiseg),
                        KozosE = legyenKozos,
                        ResztvevoId = legyenKozos ? null : alapResztvevoId,
                        Bepakolva = false
                    }, ct);
                    hozzaadottTetelDb++;
                }
            }

            KivalasztottSablonIds.Clear();
            IsTuraFormOpen = false;
            await ReloadSilentlyAsync(ct);
        }, IsEditMode ? "Nem sikerült módosítani a túrát." : "Nem sikerült létrehozni a túrát.");

        if (ok)
        {
            if (hozzaadottTetelDb > 0)
            {
                SetSuccess(IsEditMode
                    ? $"A túra frissült, és {hozzaadottTetelDb} felszerelés hozzáadva ({kivalasztottListaDb} csomaglistából)."
                    : $"Az új horgásztúra létrejött ({hozzaadottTetelDb} felszerelés hozzáadva {kivalasztottListaDb} csomaglistából).");
            }
            else
            {
                SetSuccess(IsEditMode ? "A túra adatai frissültek." : "Az új horgásztúra sikeresen létrejött.");
            }
        }
    }

    public async Task RequestDeleteTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ClearMessages();
        var confirmRequired = await _settingsService.GetConfirmBeforeDeleteAsync();
        if (confirmRequired)
        {
            TorlesreVaroTura = tura;
            NotifyStateChanged();
            return;
        }

        await DeleteTuraInternalAsync(tura, ct);
    }

    public void CancelDeleteTura()
    {
        TorlesreVaroTura = null;
        NotifyStateChanged();
    }

    public async Task ConfirmDeleteTuraAsync(CancellationToken ct = default)
    {
        if (TorlesreVaroTura is null)
        {
            return;
        }

        var target = TorlesreVaroTura;
        TorlesreVaroTura = null;
        await DeleteTuraInternalAsync(target, ct);
    }

    private async Task DeleteTuraInternalAsync(Tura tura, CancellationToken ct)
    {
        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.DeleteTuraAsync(tura.Id, ct);
            if (ResztvevoKezeltTura?.Id == tura.Id)
            {
                ResztvevoKezeltTura = null;
            }

            await ReloadSilentlyAsync(ct);
        }, "Nem sikerült törölni a túrát.");

        if (ok)
        {
            SetSuccess($"A(z) „{tura.Nev}” túra törölve lett.");
        }
    }

    // --- Résztvevők kezelése (regisztrált felhasználókból) ---

    public async Task OpenResztvevokPanelAsync(Tura tura, CancellationToken ct = default)
    {
        ClearMessages();
        ResztvevoKezeltTura = tura;

        await ExecuteAsync(async () =>
        {
            var usersTask = _turaService.GetRegisztraltFelhasznalokAsync(ct);
            var resztvevokTask = _turaService.GetResztvevokAsync(tura.Id, ct);
            await Task.WhenAll(usersTask, resztvevokTask);

            RegisztraltFelhasznalok = (await usersTask).ToList();
            ResztvevokTuraSzerint[tura.Id] = (await resztvevokTask).ToList();
            ResetResztvevoForm();
        }, "Nem sikerült betölteni a regisztrált felhasználók listáját.");
    }

    public async Task RefreshRegisztraltFelhasznalokAsync(CancellationToken ct = default)
    {
        if (ResztvevoKezeltTura is null)
        {
            return;
        }

        ClearMessages();
        var turaId = ResztvevoKezeltTura.Id;

        await ExecuteAsync(async () =>
        {
            var usersTask = _turaService.GetRegisztraltFelhasznalokAsync(ct);
            var resztvevokTask = _turaService.GetResztvevokAsync(turaId, ct);
            await Task.WhenAll(usersTask, resztvevokTask);

            RegisztraltFelhasznalok = (await usersTask).ToList();
            ResztvevokTuraSzerint[turaId] = (await resztvevokTask).ToList();
            ResetResztvevoForm();
        }, "Nem sikerült frissíteni a regisztrált felhasználók listáját.");
    }

    public void CloseResztvevokPanel()
    {
        ResztvevoKezeltTura = null;
        NotifyStateChanged();
    }

    public async Task AddResztvevoAsync(CancellationToken ct = default)
    {
        if (ResztvevoKezeltTura is null)
        {
            return;
        }

        if (!Guid.TryParse(ResztvevoForm.SelectedUserId, out var selectedGuid) || selectedGuid == Guid.Empty)
        {
            SetError("Válassz ki egy regisztrált felhasználót a listából.");
            return;
        }

        var kivalasztott = RegisztraltFelhasznalok.FirstOrDefault(u => u.Id == selectedGuid);
        if (kivalasztott is null)
        {
            SetError("A kiválasztott regisztrált felhasználó nem található. Frissítsd a listát!");
            return;
        }

        var turaId = ResztvevoKezeltTura.Id;
        var marHozzaadottak = GetResztvevok(turaId);
        if (marHozzaadottak.Any(r =>
            r.UserId == kivalasztott.Id ||
            string.Equals(r.Nev.Trim(), kivalasztott.Felhasznalonev.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Nev.Trim(), kivalasztott.MegjelenitesiNev.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            SetError($"{kivalasztott.MegjelenitesiNev} már hozzá van adva ehhez a túrához.");
            return;
        }

        var szerepkor = string.IsNullOrWhiteSpace(ResztvevoForm.Szerepkor)
            ? "Horgász"
            : ResztvevoForm.Szerepkor.Trim();

        var hozzaadottNev = kivalasztott.MegjelenitesiNev;
        var ujResztvevo = new Resztvevo
        {
            Id = Guid.NewGuid(),
            TuraId = turaId,
            UserId = kivalasztott.Id,
            Nev = hozzaadottNev,
            Szerepkor = szerepkor
        };

        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.AddResztvevoAsync(ujResztvevo, ct);
            await ReloadSilentlyAsync(ct);
            ResetResztvevoForm();
        }, "Nem sikerült hozzáadni a résztvevőt.");

        if (ok)
        {
            SetSuccess($"{hozzaadottNev} hozzáadva a túrához.");
        }
    }

    public async Task RequestDeleteResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ClearMessages();
        var confirmRequired = await _settingsService.GetConfirmBeforeDeleteAsync();
        if (confirmRequired)
        {
            TorlesreVaroResztvevo = resztvevo;
            NotifyStateChanged();
            return;
        }

        await DeleteResztvevoInternalAsync(resztvevo, ct);
    }

    public void CancelDeleteResztvevo()
    {
        TorlesreVaroResztvevo = null;
        NotifyStateChanged();
    }

    public async Task ConfirmDeleteResztvevoAsync(CancellationToken ct = default)
    {
        if (TorlesreVaroResztvevo is null)
        {
            return;
        }

        var target = TorlesreVaroResztvevo;
        TorlesreVaroResztvevo = null;
        await DeleteResztvevoInternalAsync(target, ct);
    }

    private async Task DeleteResztvevoInternalAsync(Resztvevo resztvevo, CancellationToken ct)
    {
        var ok = await ExecuteAsync(async () =>
        {
            await _turaService.DeleteResztvevoAsync(resztvevo.Id, ct);
            await ReloadSilentlyAsync(ct);
            ResetResztvevoForm();
        }, "Nem sikerült eltávolítani a résztvevőt.");

        if (ok)
        {
            SetSuccess($"{resztvevo.Nev} eltávolítva a túrából.");
        }
    }

    private void ResetResztvevoForm()
    {
        var elsoValaszthato = ValaszthatoFelhasznalok.FirstOrDefault();
        ResztvevoForm = new AddResztvevoFormModel
        {
            SelectedUserId = elsoValaszthato is null ? string.Empty : elsoValaszthato.Id.ToString("D"),
            Szerepkor = "Horgász"
        };
    }

    private async Task ReloadSilentlyAsync(CancellationToken ct)
    {
        var turakTask = _turaService.GetTurakAsync(ct);
        var felhasznalokTask = _turaService.GetRegisztraltFelhasznalokAsync(ct);
        var sablonokTask = LoadSablonokSafeAsync(ct);
        await Task.WhenAll(turakTask, felhasznalokTask, sablonokTask);

        Turak = (await turakTask).ToList();
        RegisztraltFelhasznalok = (await felhasznalokTask).ToList();

        var map = new Dictionary<Guid, List<Resztvevo>>();
        foreach (var tura in Turak)
        {
            var resztvevok = await _turaService.GetResztvevokAsync(tura.Id, ct);
            map[tura.Id] = resztvevok.ToList();
        }

        ResztvevokTuraSzerint = map;

        if (_activeTuraState.SelectedTuraId is null || Turak.All(t => t.Id != _activeTuraState.SelectedTuraId))
        {
            var defaultTura = Turak.FirstOrDefault(t => !t.LejartE) ?? Turak.FirstOrDefault();
            _activeTuraState.SetSelectedTura(defaultTura?.Id);
        }
    }

    private async Task LoadSablonokSafeAsync(CancellationToken ct = default)
    {
        try
        {
            var sablonokTask = _turaService.GetCsomaglistaSablonokAsync(ct);
            var tetelekTask = _turaService.GetAllCsomaglistaSablonTetelekAsync(ct);
            await Task.WhenAll(sablonokTask, tetelekTask);

            CsomaglistaSablonok = (await sablonokTask).ToList();
            CsomaglistaSablonTetelek = (await tetelekTask).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "A csomaglista sablonok betöltése sikertelen a túralistán.");
            CsomaglistaSablonok = [];
            CsomaglistaSablonTetelek = [];
        }
    }
}
