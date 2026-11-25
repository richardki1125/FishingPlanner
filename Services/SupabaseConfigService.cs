using System.Text.Json;
using FishingPlanner.Models;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// A Supabase csatlakozási adatokat a gitignored Resources/Raw/supabase.config.json csomagfájlból tölti be.
/// </summary>
public sealed class SupabaseConfigService : ISupabaseConfigService
{
    private const string PackageConfigFileName = "supabase.config.json";

    private readonly ILogger<SupabaseConfigService> _logger;
    private SupabaseConfig? _cachedConfig;

    public SupabaseConfigService(ILogger<SupabaseConfigService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SupabaseConfig> GetConfigAsync(CancellationToken ct = default)
    {
        if (_cachedConfig is { IsConfigured: true })
        {
            return _cachedConfig;
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(PackageConfigFileName);
            var config = await JsonSerializer.DeserializeAsync(
                stream,
                SupabaseJsonContext.Default.SupabaseConfig,
                ct);

            if (config is not null)
            {
                config.SupabaseUrl = config.NormalizedUrl;
                config.AnonKey = config.AnonKey.Trim();

                if (config.IsConfigured)
                {
                    _cachedConfig = config;
                    return config;
                }
            }
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("A {FileName} konfigurációs fájl nem található.", PackageConfigFileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a {FileName} fájl betöltése közben.", PackageConfigFileName);
        }

        return new SupabaseConfig();
    }
}
