using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// A felhasználói beállításokat a MAUI Preferences tárhelyen keresztül kezelő szolgáltatás.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string DarkModeKey = "settings_dark_mode";
    private const string DefaultNapokSzamaKey = "settings_default_napok_szama";
    private const string ConfirmBeforeDeleteKey = "settings_confirm_before_delete";
    private const string CurrencyKey = "settings_currency";

    private const bool DefaultDarkMode = false;
    private const int DefaultNapokSzama = 7;
    private const bool DefaultConfirmBeforeDelete = true;
    private const string DefaultCurrency = "Ft";

    private readonly IPreferences _preferences;
    private readonly ILogger<SettingsService> _logger;

    public event Action<bool>? ThemeChanged;

    public SettingsService(ILogger<SettingsService> logger, IPreferences? preferences = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preferences = preferences ?? Preferences.Default;
    }

    public Task<bool> GetIsDarkModeAsync()
    {
        try
        {
            return Task.FromResult(_preferences.Get(DarkModeKey, DefaultDarkMode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült kiolvasni a sötét téma beállítást.");
            return Task.FromResult(DefaultDarkMode);
        }
    }

    public Task SetIsDarkModeAsync(bool isDarkMode)
    {
        try
        {
            _preferences.Set(DarkModeKey, isDarkMode);
            ThemeChanged?.Invoke(isDarkMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült menteni a sötét téma beállítást.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<int> GetDefaultNapokSzamaAsync()
    {
        try
        {
            return Task.FromResult(_preferences.Get(DefaultNapokSzamaKey, DefaultNapokSzama));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült kiolvasni az alapértelmezett túrahosszt.");
            return Task.FromResult(DefaultNapokSzama);
        }
    }

    public Task SetDefaultNapokSzamaAsync(int napokSzama)
    {
        if (napokSzama is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(napokSzama),
                "Az alapértelmezett túrahossznak 1 és 30 nap között kell lennie.");
        }

        try
        {
            _preferences.Set(DefaultNapokSzamaKey, napokSzama);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült menteni az alapértelmezett túrahosszt.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<bool> GetConfirmBeforeDeleteAsync()
    {
        try
        {
            return Task.FromResult(_preferences.Get(ConfirmBeforeDeleteKey, DefaultConfirmBeforeDelete));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült kiolvasni a törlési megerősítés beállítást.");
            return Task.FromResult(DefaultConfirmBeforeDelete);
        }
    }

    public Task SetConfirmBeforeDeleteAsync(bool confirm)
    {
        try
        {
            _preferences.Set(ConfirmBeforeDeleteKey, confirm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült menteni a törlési megerősítés beállítást.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<string> GetCurrencyAsync()
    {
        try
        {
            var value = _preferences.Get(CurrencyKey, DefaultCurrency);
            return Task.FromResult(NormalizeCurrency(value));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült kiolvasni a pénznem beállítást.");
            return Task.FromResult(DefaultCurrency);
        }
    }

    public Task SetCurrencyAsync(string currency)
    {
        var normalized = NormalizeCurrency(currency);
        try
        {
            _preferences.Set(CurrencyKey, normalized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült menteni a pénznem beállítást.");
            throw;
        }

        return Task.CompletedTask;
    }

    private static string NormalizeCurrency(string? currency) =>
        currency?.Trim().ToUpperInvariant() switch
        {
            "EUR" => "EUR",
            "RON" => "RON",
            _ => DefaultCurrency
        };

    public Task ResetToDefaultsAsync()
    {
        try
        {
            _preferences.Set(DarkModeKey, DefaultDarkMode);
            _preferences.Set(DefaultNapokSzamaKey, DefaultNapokSzama);
            _preferences.Set(ConfirmBeforeDeleteKey, DefaultConfirmBeforeDelete);
            _preferences.Set(CurrencyKey, DefaultCurrency);
            ThemeChanged?.Invoke(DefaultDarkMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nem sikerült visszaállítani az alapértelmezett beállításokat.");
            throw;
        }

        return Task.CompletedTask;
    }
}
