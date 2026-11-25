using System.Globalization;
using FishingPlanner.Models;
using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

public enum StatisztikaNezetMod
{
    TuraStatisztika,
    EvVegiOsszegzes
}

/// <summary>
/// A statisztika, horgász ranglista és év végi összegzés képernyő állapotkezelését végző ViewModel.
/// </summary>
public sealed class StatisztikaViewModel : ViewModelBase
{
    private static readonly CultureInfo HuCulture = CultureInfo.GetCultureInfo("hu-HU");

    private readonly ITuraService _turaService;
    private readonly IStatisztikaService _statisztikaService;
    private readonly IActiveTuraState _activeTuraState;
    private readonly ISettingsService _settingsService;

    public StatisztikaNezetMod AktivNezet { get; private set; } = StatisztikaNezetMod.TuraStatisztika;

    public List<Tura> Turak { get; private set; } = [];
    public Tura? AktualisTura { get; private set; }
    public TuraStatisztikaOsszesito? Osszesito { get; private set; }

    // Év végi összegzés állapot
    public List<int> ElerhetoEvek { get; private set; } = [DateTime.Today.Year];
    public int KivalasztottEv { get; private set; } = DateTime.Today.Year;
    public EvVegiOsszesito? EvOsszesito { get; private set; }
    public string Penznem { get; private set; } = "Ft";

    public TuraStatisztikaOsszesito? MegjelenitettStatisztika =>
        AktivNezet == StatisztikaNezetMod.EvVegiOsszegzes
            ? EvOsszesito?.Statisztika
            : Osszesito;

    public StatisztikaViewModel(
        ITuraService turaService,
        IStatisztikaService statisztikaService,
        IActiveTuraState activeTuraState,
        ISettingsService settingsService,
        ILogger<StatisztikaViewModel> logger)
        : base(logger)
    {
        _turaService = turaService ?? throw new ArgumentNullException(nameof(turaService));
        _statisztikaService = statisztikaService ?? throw new ArgumentNullException(nameof(statisztikaService));
        _activeTuraState = activeTuraState ?? throw new ArgumentNullException(nameof(activeTuraState));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public async Task LoadAsync(Guid? routeTuraId = null, CancellationToken ct = default)
    {
        if (routeTuraId.HasValue)
        {
            AktivNezet = StatisztikaNezetMod.TuraStatisztika;
        }

        await ExecuteAsync(async () =>
        {
            Penznem = await _settingsService.GetCurrencyAsync();
            var turak = await _turaService.GetTurakAsync(ct);
            Turak = turak.ToList();
            UpdateElerhetoEvek();

            if (AktivNezet == StatisztikaNezetMod.EvVegiOsszegzes)
            {
                await ComputeForYearAsync(KivalasztottEv, ct);
            }
            else
            {
                var targetId = routeTuraId ?? _activeTuraState.SelectedTuraId ?? Turak.FirstOrDefault()?.Id;
                if (targetId.HasValue && Turak.All(t => t.Id != targetId.Value))
                {
                    targetId = Turak.FirstOrDefault()?.Id;
                }

                await ComputeForTuraAsync(targetId, ct);
            }
        }, "Nem sikerült kiszámítani a statisztikákat.", useFullPageLoading: true);
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

        try
        {
            Penznem = await _settingsService.GetCurrencyAsync();
            var turak = await _turaService.GetTurakAsync(ct);
            Turak = turak.ToList();
            UpdateElerhetoEvek();

            if (AktivNezet == StatisztikaNezetMod.EvVegiOsszegzes)
            {
                await ComputeForYearAsync(KivalasztottEv, ct);
            }
            else
            {
                var targetId = AktualisTura?.Id ?? _activeTuraState.SelectedTuraId ?? Turak.FirstOrDefault()?.Id;
                if (targetId.HasValue && Turak.All(t => t.Id != targetId.Value))
                {
                    targetId = Turak.FirstOrDefault()?.Id;
                }

                await ComputeForTuraAsync(targetId, ct);
            }

            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            // Normál megszakítás
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Csendes frissítés sikertelen a statisztika oldalon.");
        }
    }

    public async Task SetNezetAsync(StatisztikaNezetMod mod, CancellationToken ct = default)
    {
        if (AktivNezet == mod)
        {
            return;
        }

        ClearMessages();
        AktivNezet = mod;

        await ExecuteAsync(async () =>
        {
            Penznem = await _settingsService.GetCurrencyAsync();
            if (AktivNezet == StatisztikaNezetMod.EvVegiOsszegzes)
            {
                await ComputeForYearAsync(KivalasztottEv, ct);
            }
            else
            {
                var targetId = AktualisTura?.Id ?? _activeTuraState.SelectedTuraId ?? Turak.FirstOrDefault()?.Id;
                if (targetId.HasValue && Turak.All(t => t.Id != targetId.Value))
                {
                    targetId = Turak.FirstOrDefault()?.Id;
                }

                await ComputeForTuraAsync(targetId, ct);
            }
        }, "Nem sikerült betölteni a kiválasztott statisztikai nézetet.", useFullPageLoading: true);
    }

    public async Task ChangeTuraAsync(Guid turaId, CancellationToken ct = default)
    {
        ClearMessages();
        AktivNezet = StatisztikaNezetMod.TuraStatisztika;
        await ExecuteAsync(
            () => ComputeForTuraAsync(turaId, ct),
            "Nem sikerült kiszámítani a kiválasztott túra statisztikáit.",
            useFullPageLoading: true);
    }

    public async Task ChangeEvAsync(int ev, CancellationToken ct = default)
    {
        ClearMessages();
        AktivNezet = StatisztikaNezetMod.EvVegiOsszegzes;
        KivalasztottEv = ev;

        await ExecuteAsync(
            () => ComputeForYearAsync(ev, ct),
            $"Nem sikerült kiszámítani a(z) {ev}. évi összegzést.",
            useFullPageLoading: true);
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

    private void UpdateElerhetoEvek()
    {
        var evek = Turak
            .SelectMany(t => new[] { t.KezdoDatum.Year, t.VegDatum.Year })
            .Append(DateTime.Today.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        ElerhetoEvek = evek;
        if (!ElerhetoEvek.Contains(KivalasztottEv))
        {
            KivalasztottEv = ElerhetoEvek.FirstOrDefault(DateTime.Today.Year);
        }
    }

    private async Task ComputeForTuraAsync(Guid? turaId, CancellationToken ct)
    {
        _activeTuraState.SetSelectedTura(turaId);
        AktualisTura = turaId.HasValue ? Turak.FirstOrDefault(t => t.Id == turaId.Value) : null;

        if (AktualisTura is null)
        {
            Osszesito = null;
            return;
        }

        var resztvevokTask = _turaService.GetResztvevokAsync(AktualisTura.Id, ct);
        var fogasokTask = _turaService.GetFogasokAsync(AktualisTura.Id, ct);
        var halKategoriakTask = _turaService.GetHalKategoriakAsync(ct);
        var koltsegekTask = TryGetKoltsegekAsync(AktualisTura.Id, ct);
        var profilokTask = TryGetProfilokAsync(ct);

        await Task.WhenAll(resztvevokTask, fogasokTask, halKategoriakTask, koltsegekTask, profilokTask);

        Osszesito = _statisztikaService.SzamolStatisztika(
            AktualisTura,
            await resztvevokTask,
            await fogasokTask,
            await halKategoriakTask,
            await koltsegekTask,
            await profilokTask);
    }

    private async Task ComputeForYearAsync(int ev, CancellationToken ct)
    {
        KivalasztottEv = ev;

        var evTurai = Turak
            .Where(t => t.KezdoDatum.Year == ev || t.VegDatum.Year == ev)
            .OrderByDescending(t => t.KezdoDatum)
            .ToList();

        if (evTurai.Count == 0)
        {
            EvOsszesito = new EvVegiOsszesito
            {
                Ev = ev,
                TurakSzama = 0
            };
            return;
        }

        var halKategoriakTask = _turaService.GetHalKategoriakAsync(ct);
        var profilokTask = TryGetProfilokAsync(ct);

        var osszesResztvevo = new List<Resztvevo>();
        var osszesFogas = new List<Fogas>();
        var osszesKoltseg = new List<KoltsegTetel>();

        foreach (var tura in evTurai)
        {
            var rTask = _turaService.GetResztvevokAsync(tura.Id, ct);
            var fTask = _turaService.GetFogasokAsync(tura.Id, ct);
            var kTask = TryGetKoltsegekAsync(tura.Id, ct);
            await Task.WhenAll(rTask, fTask, kTask);

            osszesResztvevo.AddRange(await rTask);
            osszesFogas.AddRange(await fTask);
            osszesKoltseg.AddRange(await kTask);
        }

        await Task.WhenAll(halKategoriakTask, profilokTask);

        EvOsszesito = _statisztikaService.SzamolEvVegiStatisztika(
            ev,
            evTurai,
            osszesResztvevo,
            osszesFogas,
            osszesKoltseg,
            await halKategoriakTask,
            await profilokTask);
    }

    private async Task<IReadOnlyList<KoltsegTetel>> TryGetKoltsegekAsync(Guid turaId, CancellationToken ct)
    {
        try
        {
            return await _turaService.GetKoltsegekAsync(turaId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "A költségek lekérdezése kihagyva az év végi összegzésnél ({TuraId}).", turaId);
            return [];
        }
    }

    private async Task<IReadOnlyList<RegisztraltFelhasznalo>> TryGetProfilokAsync(CancellationToken ct)
    {
        try
        {
            return await _turaService.GetRegisztraltFelhasznalokAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "A profilok lekérdezése sikertelen az év végi összegzésnél.");
            return [];
        }
    }
}
