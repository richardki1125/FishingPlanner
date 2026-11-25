using FishingPlanner.Models;

namespace FishingPlanner.Services;

/// <summary>
/// A helyi gyorsítótár, offline állapot és felhős szinkronizálás kezelésére szolgáló interfész.
/// </summary>
public interface ISyncStatusService
{
    bool IsOffline { get; }
    int PendingSyncCount { get; }
    DateTimeOffset? LastSyncedAtUtc { get; }

    event Action? SyncStatusChanged;

    Task<SyncResult> SyncNowAsync(CancellationToken ct = default);
    Task<SyncResult> PushAllLocalToCloudAsync(CancellationToken ct = default);
    Task<FishingPlannerBackup> BuildSnapshotAsync(CancellationToken ct = default);
    Task<SyncResult> ImportSnapshotAsync(FishingPlannerBackup backup, CancellationToken ct = default);
}
