namespace FishingPlanner.Services;

/// <summary>
/// Az alkalmazás tartós, eszközszintű felhasználói beállításainak kezelésére szolgáló interfész.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Akkor aktiválódik, amikor a világos/sötét téma beállítás megváltozik.
    /// </summary>
    event Action<bool>? ThemeChanged;

    Task<bool> GetIsDarkModeAsync();
    Task SetIsDarkModeAsync(bool isDarkMode);

    Task<int> GetDefaultNapokSzamaAsync();
    Task SetDefaultNapokSzamaAsync(int napokSzama);

    Task<bool> GetConfirmBeforeDeleteAsync();
    Task SetConfirmBeforeDeleteAsync(bool confirm);

    Task<string> GetCurrencyAsync();
    Task SetCurrencyAsync(string currency);

    Task ResetToDefaultsAsync();
}
