using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FishingPlanner.Models;
using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

/// <summary>
/// A profiloldal szerkeszthető űrlapmodellje (megjelenített név + profilkép).
/// </summary>
public sealed class ProfilFormModel : IValidatableObject
{
    public string MegjelenitettNev { get; set; } = string.Empty;
    public string? ProfilkepUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var trimmed = MegjelenitettNev?.Trim() ?? string.Empty;
        if (trimmed.Length > 0 && (trimmed.Length < 2 || trimmed.Length > 40))
        {
            yield return new ValidationResult(
                "A megjelenített név 2 és 40 karakter között legyen (vagy hagyd üresen az alapértelmezett felhasználónévhez).",
                [nameof(MegjelenitettNev)]);
        }
    }
}

/// <summary>
/// Egy naptárcellát leíró megtekintési modell a horgásznaptárhoz.
/// </summary>
public sealed record NaptarNapCell(
    DateTime Datum,
    bool AktualisHonapE,
    bool MaE,
    bool TuraNapE,
    bool JovoTuraNapE,
    bool KivalasztottE,
    IReadOnlyList<Tura> Turak);

/// <summary>
/// A felhasználó saját profiljának, megjelenített nevének, profilképének és
/// a vízparton eltöltött napokat megjelenítő horgásznaptárnak a ViewModel-je.
/// </summary>
public sealed class ProfilViewModel : ViewModelBase
{
    private static readonly CultureInfo HungarianCulture = CultureInfo.GetCultureInfo("hu-HU");

    private readonly IAuthService _authService;
    private readonly IMediaService _mediaService;
    private readonly ITuraService _turaService;

    private string? _eredetiProfilkepUrl;
    private List<Tura> _turak = [];
    private Dictionary<Guid, List<Resztvevo>> _resztvevokTuraSzerint = [];
    private Dictionary<DateTime, List<Tura>> _vizpartiNapok = [];

    public ProfilFormModel Form { get; private set; } = new();
    public string Felhasznalonev => _authService.CurrentUser?.Felhasznalonev ?? string.Empty;
    public string MegjelenitesiNev => _authService.CurrentUser?.MegjelenitesiNev ?? "Horgász";
    public Guid CurrentUserId => _authService.CurrentUser?.UserId ?? Guid.Empty;
    public bool IsAdmin => _authService.IsAdmin;
    public bool IsCaptureSupported => _mediaService.IsCaptureSupported;

    // Regisztrált felhasználók és horgásznaptár állapot
    public List<RegisztraltFelhasznalo> RegisztraltFelhasznalok { get; private set; } = [];
    public Guid MegtekintettProfilId { get; private set; }
    public DateTime NaptarHonap { get; private set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime? KivalasztottNap { get; private set; }

    public int EltoltottNapokDb => _vizpartiNapok.Count;
    public int ResztvettTurakDb { get; private set; }

    public int AktualisHonapNapokDb => _vizpartiNapok.Keys.Count(d =>
        d.Year == NaptarHonap.Year && d.Month == NaptarHonap.Month);

    public string NaptarHonapCim
    {
        get
        {
            var raw = NaptarHonap.ToString("yyyy. MMMM", HungarianCulture);
            return raw;
        }
    }

    public RegisztraltFelhasznalo? MegtekintettProfil =>
        RegisztraltFelhasznalok.FirstOrDefault(u => u.Id == MegtekintettProfilId);

    public IReadOnlyList<Tura> KivalasztottNapTurai =>
        KivalasztottNap.HasValue && _vizpartiNapok.TryGetValue(KivalasztottNap.Value.Date, out var lista)
            ? lista
            : [];

    public ProfilViewModel(
        IAuthService authService,
        IMediaService mediaService,
        ITuraService turaService,
        ILogger<ProfilViewModel> logger)
        : base(logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _turaService = turaService ?? throw new ArgumentNullException(nameof(turaService));
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var user = _authService.CurrentUser;
        _eredetiProfilkepUrl = user?.ProfilkepUrl;
        Form = new ProfilFormModel
        {
            MegjelenitettNev = user?.MegjelenitettNev ?? string.Empty,
            ProfilkepUrl = user?.ProfilkepUrl
        };

        if (MegtekintettProfilId == Guid.Empty && user is not null)
        {
            MegtekintettProfilId = user.UserId;
        }

        await ExecuteAsync(async () =>
        {
            await LoadCalendarDataInternalAsync(ct);
        }, "Nem sikerült betölteni a horgásznaptár adatait.");
    }

    public async Task RefreshSilentlyAsync(bool isManual = false, CancellationToken ct = default)
    {
        if (IsLoading || IsBusy)
        {
            return;
        }

        try
        {
            await LoadCalendarDataInternalAsync(ct);
            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            // Normál megszakítás
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Csendes frissítés sikertelen a profil naptáron.");
        }
    }

    public void SelectCalendarUser(Guid userId)
    {
        if (MegtekintettProfilId == userId)
        {
            return;
        }

        MegtekintettProfilId = userId;
        KivalasztottNap = null;
        RebuildCalendarDays();
        NotifyStateChanged();
    }

    public void PrevMonth()
    {
        NaptarHonap = NaptarHonap.AddMonths(-1);
        KivalasztottNap = null;
        NotifyStateChanged();
    }

    public void NextMonth()
    {
        NaptarHonap = NaptarHonap.AddMonths(1);
        KivalasztottNap = null;
        NotifyStateChanged();
    }

    public void GoToToday()
    {
        NaptarHonap = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        KivalasztottNap = _vizpartiNapok.ContainsKey(DateTime.Today) ? DateTime.Today : null;
        NotifyStateChanged();
    }

    public void SelectDay(DateTime datum)
    {
        var day = datum.Date;
        if (!_vizpartiNapok.ContainsKey(day))
        {
            KivalasztottNap = null;
        }
        else if (KivalasztottNap == day)
        {
            KivalasztottNap = null;
        }
        else
        {
            KivalasztottNap = day;
            if (day.Year != NaptarHonap.Year || day.Month != NaptarHonap.Month)
            {
                NaptarHonap = new DateTime(day.Year, day.Month, 1);
            }
        }

        NotifyStateChanged();
    }

    public IReadOnlyList<NaptarNapCell> GetHonapCellak()
    {
        var firstDayOfMonth = new DateTime(NaptarHonap.Year, NaptarHonap.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(NaptarHonap.Year, NaptarHonap.Month);

        // Hétfő = 0, Kedd = 1, ..., Vasárnap = 6
        var startOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;
        var totalCells = startOffset + daysInMonth <= 35 ? 35 : 42;
        var gridStart = firstDayOfMonth.AddDays(-startOffset);
        var today = DateTime.Today;

        var cells = new List<NaptarNapCell>(totalCells);
        for (var i = 0; i < totalCells; i++)
        {
            var date = gridStart.AddDays(i);
            var hasTrip = _vizpartiNapok.TryGetValue(date, out var turak) && turak.Count > 0;

            cells.Add(new NaptarNapCell(
                Datum: date,
                AktualisHonapE: date.Month == NaptarHonap.Month && date.Year == NaptarHonap.Year,
                MaE: date == today,
                TuraNapE: hasTrip,
                JovoTuraNapE: hasTrip && date > today,
                KivalasztottE: KivalasztottNap.HasValue && KivalasztottNap.Value.Date == date,
                Turak: hasTrip ? turak! : []));
        }

        return cells;
    }

    public async Task PickAvatarAsync(CancellationToken ct = default)
    {
        await ExecuteAsync(async () =>
        {
            var url = await _mediaService.PickPhotoAsync(ct);
            if (!string.IsNullOrWhiteSpace(url))
            {
                Form.ProfilkepUrl = url;
            }
        }, "Nem sikerült kiválasztani a profilképet a galériából.");
    }

    public async Task CaptureAvatarAsync(CancellationToken ct = default)
    {
        await ExecuteAsync(async () =>
        {
            var url = await _mediaService.CapturePhotoAsync(ct);
            if (!string.IsNullOrWhiteSpace(url))
            {
                Form.ProfilkepUrl = url;
            }
        }, "Nem sikerült elkészíteni a profilképet a kamerával.");
    }

    public void RemoveAvatar()
    {
        Form.ProfilkepUrl = null;
        NotifyStateChanged();
    }

    public async Task SaveProfileAsync(CancellationToken ct = default)
    {
        var ok = await ExecuteAsync(async () =>
        {
            var regiKepTorlendo = !string.IsNullOrWhiteSpace(_eredetiProfilkepUrl) &&
                !string.Equals(_eredetiProfilkepUrl, Form.ProfilkepUrl, StringComparison.Ordinal);

            var result = await _authService.UpdateProfileAsync(Form.MegjelenitettNev, Form.ProfilkepUrl, ct);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Nem sikerült menteni a profilt.");
            }

            if (regiKepTorlendo)
            {
                await _mediaService.DeletePhotoAsync(_eredetiProfilkepUrl, ct);
            }

            _eredetiProfilkepUrl = Form.ProfilkepUrl;
            await LoadCalendarDataInternalAsync(ct);
        }, "Nem sikerült elmenteni a profil adatokat.");

        if (ok)
        {
            SetSuccess("A profilod sikeresen frissült!");
        }
    }

    public async Task SignOutAsync()
    {
        await ExecuteAsync(async () =>
        {
            await _authService.SignOutAsync();
        }, "Hiba történt kijelentkezés közben.");
    }

    private async Task LoadCalendarDataInternalAsync(CancellationToken ct)
    {
        var turakTask = _turaService.GetTurakAsync(ct);
        var profilokTask = _turaService.GetRegisztraltFelhasznalokAsync(ct);
        await Task.WhenAll(turakTask, profilokTask);

        _turak = (await turakTask).ToList();
        var profilok = (await profilokTask).ToList();

        // Ha a bejelentkezett felhasználó még nincs a listában (pl. offline), biztosan megjelenjen
        var current = _authService.CurrentUser;
        if (current is not null && current.UserId != Guid.Empty && profilok.All(p => p.Id != current.UserId))
        {
            profilok.Insert(0, new RegisztraltFelhasznalo(
                current.UserId,
                current.Felhasznalonev,
                current.MegjelenitettNev,
                current.ProfilkepUrl));
        }

        // Saját profil legyen elöl a választóban
        RegisztraltFelhasznalok = profilok
            .OrderByDescending(p => current is not null && p.Id == current.UserId)
            .ThenBy(p => p.MegjelenitesiNev, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (MegtekintettProfilId == Guid.Empty || RegisztraltFelhasznalok.All(p => p.Id != MegtekintettProfilId))
        {
            MegtekintettProfilId = current?.UserId ?? RegisztraltFelhasznalok.FirstOrDefault()?.Id ?? Guid.Empty;
        }

        var map = new Dictionary<Guid, List<Resztvevo>>();
        var resztvevoTasks = _turak.Select(async t => (t.Id, List: (await _turaService.GetResztvevokAsync(t.Id, ct)).ToList()));
        var results = await Task.WhenAll(resztvevoTasks);
        foreach (var (turaId, lista) in results)
        {
            map[turaId] = lista;
        }

        _resztvevokTuraSzerint = map;
        RebuildCalendarDays();
    }

    private void RebuildCalendarDays()
    {
        var dayMap = new Dictionary<DateTime, List<Tura>>();
        var resztvettTurak = 0;

        var profil = MegtekintettProfil;
        if (profil is null)
        {
            _vizpartiNapok = dayMap;
            ResztvettTurakDb = 0;
            return;
        }

        foreach (var tura in _turak)
        {
            if (!_resztvevokTuraSzerint.TryGetValue(tura.Id, out var resztvevok))
            {
                continue;
            }

            var resztVett = resztvevok.Any(r => r.AktivE && MatchesUser(r, profil));
            if (!resztVett)
            {
                continue;
            }

            resztvettTurak++;
            var start = tura.KezdoDatum.Date;
            var end = tura.VegDatum.Date < start ? start : tura.VegDatum.Date;

            for (var day = start; day <= end; day = day.AddDays(1))
            {
                if (!dayMap.TryGetValue(day, out var turakAzonANapon))
                {
                    turakAzonANapon = [];
                    dayMap[day] = turakAzonANapon;
                }

                if (turakAzonANapon.All(t => t.Id != tura.Id))
                {
                    turakAzonANapon.Add(tura);
                }
            }
        }

        _vizpartiNapok = dayMap;
        ResztvettTurakDb = resztvettTurak;
    }

    private static bool MatchesUser(Resztvevo r, RegisztraltFelhasznalo user)
    {
        if (r.UserId.HasValue && r.UserId.Value == user.Id)
        {
            return true;
        }

        if (r.UserId.HasValue && r.UserId.Value != Guid.Empty)
        {
            return false;
        }

        return string.Equals(r.Nev.Trim(), user.Felhasznalonev.Trim(), StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(user.MegjelenitettNev) &&
                string.Equals(r.Nev.Trim(), user.MegjelenitettNev.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
