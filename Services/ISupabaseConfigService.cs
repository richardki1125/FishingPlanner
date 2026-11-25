using FishingPlanner.Models;

namespace FishingPlanner.Services;

/// <summary>
/// A Supabase csatlakozási adatok (URL, Publishable/Anon kulcs) betöltésére szolgáló interfész.
/// </summary>
public interface ISupabaseConfigService
{
    Task<SupabaseConfig> GetConfigAsync(CancellationToken ct = default);
}
