namespace FishingPlanner.Models;

/// <summary>
/// Offline munka során sorba állított, későbbi felhős szinkronizálásra váró művelet.
/// </summary>
public sealed class PendingSyncOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OperationType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public bool? BoolValue { get; set; }
    public Tura? TuraPayload { get; set; }
    public Resztvevo? ResztvevoPayload { get; set; }
    public FelszerelesTetel? FelszerelesPayload { get; set; }
    public Fogas? FogasPayload { get; set; }
    public KoltsegTetel? KoltsegPayload { get; set; }
    public CsomaglistaSablon? CsomaglistaSablonPayload { get; set; }
    public CsomaglistaSablonTetel? CsomaglistaSablonTetelPayload { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A telefon/PC saját tárhelyére mentett helyi adatbázis-tükör és szinkronizációs sor.
/// </summary>
public sealed class OfflineCacheStore
{
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public List<RegisztraltFelhasznalo> RegisztraltFelhasznalok { get; set; } = [];
    public List<HalKategoria> HalKategoriak { get; set; } = [];
    public List<HalAlkategoria> HalAlkategoriak { get; set; } = [];
    public List<Tura> Turak { get; set; } = [];
    public List<Resztvevo> Resztvevok { get; set; } = [];
    public List<FelszerelesTetel> Felszerelesek { get; set; } = [];
    public List<Fogas> Fogasok { get; set; } = [];
    public List<KoltsegTetel> Koltsegek { get; set; } = [];
    public List<CsomaglistaSablon> CsomaglistaSablonok { get; set; } = [];
    public List<CsomaglistaSablonTetel> CsomaglistaSablonTetelek { get; set; } = [];
    public List<PendingSyncOperation> PendingOperations { get; set; } = [];
}

/// <summary>
/// Exportálható és visszatölthető teljes biztonsági mentés (.json) formátuma.
/// </summary>
public sealed class FishingPlannerBackup
{
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<Tura> Turak { get; set; } = [];
    public List<Resztvevo> Resztvevok { get; set; } = [];
    public List<FelszerelesTetel> Felszerelesek { get; set; } = [];
    public List<Fogas> Fogasok { get; set; } = [];
    public List<KoltsegTetel> Koltsegek { get; set; } = [];
    public List<CsomaglistaSablon> CsomaglistaSablonok { get; set; } = [];
    public List<CsomaglistaSablonTetel> CsomaglistaSablonTetelek { get; set; } = [];
}

public sealed record SyncResult(
    bool Success,
    int SyncedOperationsCount,
    int RemainingPendingCount,
    string Message);

public sealed record BackupOperationResult(
    bool Success,
    string Message,
    string? SavedFilePath = null);
