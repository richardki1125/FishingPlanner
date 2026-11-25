using System.Text.Json;
using FishingPlanner.Models;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// Offline-First dekorátor az ITuraService interfészhez:
/// - Minden olvasás és írás helyi JSON tükörbe is mentődik a készülékre (FileSystem.AppDataDirectory).
/// - Ha nincs internet vagy elérhetetlen a Supabase, az app helyben tovább működik és sorba állítja a módosításokat.
/// - Újracsatlakozáskor automatikusan (vagy kézzel) felszinkronizálja a függőben lévő műveleteket és offline fotókat.
/// </summary>
public sealed class OfflineSyncTuraService : ITuraService, ISyncStatusService
{
    private const string CacheFileName = "fishingplanner_offline_cache.json";

    private readonly SupabaseTuraService _remote;
    private readonly IMediaService _mediaService;
    private readonly ILogger<OfflineSyncTuraService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _cacheFilePath;

    private OfflineCacheStore? _store;

    public bool IsOffline { get; private set; }
    public int PendingSyncCount => _store?.PendingOperations.Count ?? 0;
    public DateTimeOffset? LastSyncedAtUtc => _store?.LastSyncedAtUtc;

    public event Action? SyncStatusChanged;

    public OfflineSyncTuraService(
        SupabaseTuraService remote,
        IMediaService mediaService,
        ILogger<OfflineSyncTuraService> logger)
    {
        _remote = remote ?? throw new ArgumentNullException(nameof(remote));
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheFilePath = Path.Combine(FileSystem.AppDataDirectory, CacheFileName);
    }

    // --- Túrák ---

    public async Task<IReadOnlyList<Tura>> GetTurakAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remoteTurak = await _remote.GetTurakAsync(ct);
                    store.Turak = remoteTurak.Select(Clone).ToList();
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                    return store.Turak.OrderByDescending(t => t.KezdoDatum).Select(Clone).ToList();
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetTurakAsync), helyi mentés betöltése.");
                    SetOnlineState(true);
                }
            }

            return store.Turak.OrderByDescending(t => t.KezdoDatum).Select(Clone).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Tura?> GetTuraByIdAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetTuraByIdAsync(turaId, ct);
                    if (remote is not null)
                    {
                        UpsertLocal(store.Turak, Clone(remote), x => x.Id == remote.Id);
                        store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                        SetOnlineState(false);
                        await PersistStoreAsync(store, ct);
                        return Clone(remote);
                    }
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetTuraByIdAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            var local = store.Turak.FirstOrDefault(t => t.Id == turaId);
            return local is null ? null : Clone(local);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Tura> CreateTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tura);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tura);
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            UpsertLocal(store.Turak, Clone(item), x => x.Id == item.Id);

            try
            {
                var created = await _remote.CreateTuraAsync(item, ct);
                UpsertLocal(store.Turak, Clone(created), x => x.Id == created.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(created);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "Túra offline mentve a helyi tárhelyre: {Nev}", item.Nev);
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertTura",
                    EntityId = item.Id,
                    TuraPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Tura> UpdateTuraAsync(Tura tura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tura);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tura);
            UpsertLocal(store.Turak, Clone(item), x => x.Id == item.Id);

            try
            {
                var updated = await _remote.UpdateTuraAsync(item, ct);
                UpsertLocal(store.Turak, Clone(updated), x => x.Id == updated.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(updated);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "Túra módosítás offline sorba állítva: {Id}", item.Id);
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertTura",
                    EntityId = item.Id,
                    TuraPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteTuraAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            store.Turak.RemoveAll(t => t.Id == turaId);
            store.Resztvevok.RemoveAll(r => r.TuraId == turaId);
            store.Felszerelesek.RemoveAll(f => f.TuraId == turaId);
            store.Fogasok.RemoveAll(f => f.TuraId == turaId);
            store.Koltsegek.RemoveAll(k => k.TuraId == turaId);

            try
            {
                await _remote.DeleteTuraAsync(turaId, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "Túra törlés offline sorba állítva: {TuraId}", turaId);
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "DeleteTura",
                    EntityId = turaId
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Regisztrált felhasználók ---

    public async Task<IReadOnlyList<RegisztraltFelhasznalo>> GetRegisztraltFelhasznalokAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);

            try
            {
                var remoteUsers = await _remote.GetRegisztraltFelhasznalokAsync(ct);
                store.RegisztraltFelhasznalok = remoteUsers.ToList();
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return store.RegisztraltFelhasznalok.ToList();
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "Offline mód aktív (GetRegisztraltFelhasznalokAsync), helyi lista használata.");
                SetOnlineState(true);
                return store.RegisztraltFelhasznalok.ToList();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Hal főkategóriák és alkategóriák ---

    public async Task<IReadOnlyList<HalKategoriaCsoport>> GetHalKategoriakAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);

            try
            {
                var remoteGroups = await _remote.GetHalKategoriakAsync(ct);
                store.HalKategoriak = remoteGroups
                    .Select(g => new HalKategoria { Id = g.Kategoria.Id, Nev = g.Kategoria.Nev, Sorrend = g.Kategoria.Sorrend })
                    .ToList();
                store.HalAlkategoriak = remoteGroups
                    .SelectMany(g => g.Alkategoriak)
                    .Select(a => new HalAlkategoria { Id = a.Id, KategoriaId = a.KategoriaId, Nev = a.Nev, Sorrend = a.Sorrend })
                    .ToList();

                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return remoteGroups;
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "Offline mód aktív (GetHalKategoriakAsync), helyi halkatalógus használata.");
                SetOnlineState(true);
                return HalKatalogusDefaults.BuildCsoportok(store.HalKategoriak, store.HalAlkategoriak);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Résztvevők ---

    public async Task<IReadOnlyList<Resztvevo>> GetResztvevokAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetResztvevokAsync(turaId, ct);
                    store.Resztvevok.RemoveAll(r => r.TuraId == turaId);
                    store.Resztvevok.AddRange(remote.Select(Clone));
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetResztvevokAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            return store.Resztvevok
                .Where(r => r.TuraId == turaId)
                .OrderBy(r => r.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Resztvevo> AddResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resztvevo);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(resztvevo);
            item.AktivE = true;

            // Ha helyben már van inaktív rekord ugyanahhoz a túrához + userhez, azt reaktiváljuk offline módban is
            var korabbiHelyi = item.UserId.HasValue && item.UserId.Value != Guid.Empty
                ? store.Resztvevok.FirstOrDefault(r => r.TuraId == item.TuraId && r.UserId == item.UserId)
                : null;

            if (korabbiHelyi is not null)
            {
                item.Id = korabbiHelyi.Id;
            }
            else if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            try
            {
                var created = await _remote.AddResztvevoAsync(item, ct);
                UpsertLocal(store.Resztvevok, Clone(created), x => x.Id == created.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(created);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "Résztvevő offline mentve: {Nev}", item.Nev);
                UpsertLocal(store.Resztvevok, Clone(item), x => x.Id == item.Id);
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertResztvevo",
                    EntityId = item.Id,
                    ResztvevoPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Resztvevo> UpdateResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resztvevo);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(resztvevo);
            UpsertLocal(store.Resztvevok, Clone(item), x => x.Id == item.Id);

            try
            {
                var updated = await _remote.UpdateResztvevoAsync(item, ct);
                UpsertLocal(store.Resztvevok, Clone(updated), x => x.Id == updated.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(updated);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertResztvevo",
                    EntityId = item.Id,
                    ResztvevoPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteResztvevoAsync(Guid resztvevoId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            // Adatmegőrző eltávolítás: a résztvevő csak inaktívvá válik, a fogásai és felszerelései MEGMARADNAK!
            var helyi = store.Resztvevok.FirstOrDefault(r => r.Id == resztvevoId);
            if (helyi is not null)
            {
                helyi.AktivE = false;
            }

            try
            {
                await _remote.DeleteResztvevoAsync(resztvevoId, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "DeleteResztvevo",
                    EntityId = resztvevoId
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Felszerelések ---

    public async Task<IReadOnlyList<FelszerelesTetel>> GetFelszerelesekAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetFelszerelesekAsync(turaId, ct);
                    store.Felszerelesek.RemoveAll(f => f.TuraId == turaId);
                    store.Felszerelesek.AddRange(remote.Select(Clone));
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetFelszerelesekAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            return store.Felszerelesek
                .Where(f => f.TuraId == turaId)
                .OrderByDescending(f => f.KozosE)
                .ThenBy(f => f.Kategoria)
                .ThenBy(f => f.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FelszerelesTetel> AddFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tetel);
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            if (item.KozosE)
            {
                item.ResztvevoId = null;
            }

            UpsertLocal(store.Felszerelesek, Clone(item), x => x.Id == item.Id);

            try
            {
                var created = await _remote.AddFelszerelesAsync(item, ct);
                UpsertLocal(store.Felszerelesek, Clone(created), x => x.Id == created.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(created);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertFelszereles",
                    EntityId = item.Id,
                    FelszerelesPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FelszerelesTetel> UpdateFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tetel);
            if (item.KozosE)
            {
                item.ResztvevoId = null;
            }

            UpsertLocal(store.Felszerelesek, Clone(item), x => x.Id == item.Id);

            try
            {
                var updated = await _remote.UpdateFelszerelesAsync(item, ct);
                UpsertLocal(store.Felszerelesek, Clone(updated), x => x.Id == updated.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(updated);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertFelszereles",
                    EntityId = item.Id,
                    FelszerelesPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ToggleBepakolvaAsync(Guid tetelId, bool bepakolva, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var local = store.Felszerelesek.FirstOrDefault(f => f.Id == tetelId);
            if (local is not null)
            {
                local.Bepakolva = bepakolva;
            }

            try
            {
                await _remote.ToggleBepakolvaAsync(tetelId, bepakolva, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "ToggleBepakolva",
                    EntityId = tetelId,
                    BoolValue = bepakolva
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteFelszerelesAsync(Guid tetelId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            store.Felszerelesek.RemoveAll(f => f.Id == tetelId);

            try
            {
                await _remote.DeleteFelszerelesAsync(tetelId, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "DeleteFelszereles",
                    EntityId = tetelId
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Fogások ---

    public async Task<IReadOnlyList<Fogas>> GetFogasokAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetFogasokAsync(turaId, ct);
                    store.Fogasok.RemoveAll(f => f.TuraId == turaId);
                    store.Fogasok.AddRange(remote.Select(Clone));
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetFogasokAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            return store.Fogasok
                .Where(f => f.TuraId == turaId)
                .OrderByDescending(f => f.Idopont)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Fogas> AddFogasAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(fogas);
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            UpsertLocal(store.Fogasok, Clone(item), x => x.Id == item.Id);

            try
            {
                item.KepUrl = await _mediaService.UploadDataUriIfNeededAsync(item.KepUrl, ct);
                var created = await _remote.AddFogasAsync(item, ct);
                UpsertLocal(store.Fogasok, Clone(created), x => x.Id == created.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(created);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertFogas",
                    EntityId = item.Id,
                    FogasPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Fogas> UpdateFogasAsync(Fogas fogas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fogas);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(fogas);
            UpsertLocal(store.Fogasok, Clone(item), x => x.Id == item.Id);

            try
            {
                item.KepUrl = await _mediaService.UploadDataUriIfNeededAsync(item.KepUrl, ct);
                var updated = await _remote.UpdateFogasAsync(item, ct);
                UpsertLocal(store.Fogasok, Clone(updated), x => x.Id == updated.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(updated);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertFogas",
                    EntityId = item.Id,
                    FogasPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteFogasAsync(Guid fogasId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            store.Fogasok.RemoveAll(f => f.Id == fogasId);

            try
            {
                await _remote.DeleteFogasAsync(fogasId, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "DeleteFogas",
                    EntityId = fogasId
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Költségek ---

    public async Task<IReadOnlyList<KoltsegTetel>> GetKoltsegekAsync(Guid turaId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetKoltsegekAsync(turaId, ct);
                    store.Koltsegek.RemoveAll(k => k.TuraId == turaId);
                    store.Koltsegek.AddRange(remote.Select(Clone));
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetKoltsegekAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            return store.Koltsegek
                .Where(k => k.TuraId == turaId)
                .OrderByDescending(k => k.KozosE)
                .ThenBy(k => k.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KoltsegTetel> AddKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tetel);
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            if (item.KozosE)
            {
                item.ResztvevoId = null;
            }

            UpsertLocal(store.Koltsegek, Clone(item), x => x.Id == item.Id);

            try
            {
                var created = await _remote.AddKoltsegAsync(item, ct);
                UpsertLocal(store.Koltsegek, Clone(created), x => x.Id == created.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(created);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertKoltseg",
                    EntityId = item.Id,
                    KoltsegPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KoltsegTetel> UpdateKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tetel);
            if (item.KozosE)
            {
                item.ResztvevoId = null;
            }

            UpsertLocal(store.Koltsegek, Clone(item), x => x.Id == item.Id);

            try
            {
                var updated = await _remote.UpdateKoltsegAsync(item, ct);
                UpsertLocal(store.Koltsegek, Clone(updated), x => x.Id == updated.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(updated);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertKoltseg",
                    EntityId = item.Id,
                    KoltsegPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteKoltsegAsync(Guid koltsegId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            store.Koltsegek.RemoveAll(k => k.Id == koltsegId);

            try
            {
                await _remote.DeleteKoltsegAsync(koltsegId, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "DeleteKoltseg",
                    EntityId = koltsegId
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Újrahasználható csomaglisták (sablonok) ---

    public async Task<IReadOnlyList<CsomaglistaSablon>> GetCsomaglistaSablonokAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetCsomaglistaSablonokAsync(ct);
                    store.CsomaglistaSablonok = remote.Select(Clone).ToList();
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetCsomaglistaSablonokAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            return store.CsomaglistaSablonok
                .OrderBy(s => s.Nev, StringComparer.CurrentCultureIgnoreCase)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablon> CreateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sablon);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(sablon);
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            UpsertLocal(store.CsomaglistaSablonok, Clone(item), x => x.Id == item.Id);

            try
            {
                var created = await _remote.CreateCsomaglistaSablonAsync(item, ct);
                UpsertLocal(store.CsomaglistaSablonok, Clone(created), x => x.Id == created.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(created);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertCsomaglistaSablon",
                    EntityId = item.Id,
                    CsomaglistaSablonPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablon> UpdateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sablon);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(sablon);
            UpsertLocal(store.CsomaglistaSablonok, Clone(item), x => x.Id == item.Id);

            try
            {
                var updated = await _remote.UpdateCsomaglistaSablonAsync(item, ct);
                UpsertLocal(store.CsomaglistaSablonok, Clone(updated), x => x.Id == updated.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(updated);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertCsomaglistaSablon",
                    EntityId = item.Id,
                    CsomaglistaSablonPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteCsomaglistaSablonAsync(Guid sablonId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            store.CsomaglistaSablonok.RemoveAll(s => s.Id == sablonId);
            store.CsomaglistaSablonTetelek.RemoveAll(t => t.SablonId == sablonId);

            try
            {
                await _remote.DeleteCsomaglistaSablonAsync(sablonId, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "DeleteCsomaglistaSablon",
                    EntityId = sablonId
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CsomaglistaSablonTetel>> GetCsomaglistaSablonTetelekAsync(Guid sablonId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetCsomaglistaSablonTetelekAsync(sablonId, ct);
                    store.CsomaglistaSablonTetelek.RemoveAll(t => t.SablonId == sablonId);
                    store.CsomaglistaSablonTetelek.AddRange(remote.Select(Clone));
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetCsomaglistaSablonTetelekAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            return store.CsomaglistaSablonTetelek
                .Where(t => t.SablonId == sablonId)
                .OrderByDescending(t => t.KozosE)
                .ThenBy(t => t.Kategoria)
                .ThenBy(t => t.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CsomaglistaSablonTetel>> GetAllCsomaglistaSablonTetelekAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var canReadRemote = await TryFlushPendingInternalAsync(store, ct);

            if (canReadRemote)
            {
                try
                {
                    var remote = await _remote.GetAllCsomaglistaSablonTetelekAsync(ct);
                    store.CsomaglistaSablonTetelek = remote.Select(Clone).ToList();
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mód aktív (GetAllCsomaglistaSablonTetelekAsync), helyi mentés használata.");
                    SetOnlineState(true);
                }
            }

            return store.CsomaglistaSablonTetelek
                .OrderByDescending(t => t.KozosE)
                .ThenBy(t => t.Kategoria)
                .ThenBy(t => t.Nev)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablonTetel> AddCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tetel);
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            UpsertLocal(store.CsomaglistaSablonTetelek, Clone(item), x => x.Id == item.Id);

            try
            {
                var created = await _remote.AddCsomaglistaSablonTetelAsync(item, ct);
                UpsertLocal(store.CsomaglistaSablonTetelek, Clone(created), x => x.Id == created.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(created);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertCsomaglistaSablonTetel",
                    EntityId = item.Id,
                    CsomaglistaSablonTetelPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CsomaglistaSablonTetel> UpdateCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tetel);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var item = Clone(tetel);
            UpsertLocal(store.CsomaglistaSablonTetelek, Clone(item), x => x.Id == item.Id);

            try
            {
                var updated = await _remote.UpdateCsomaglistaSablonTetelAsync(item, ct);
                UpsertLocal(store.CsomaglistaSablonTetelek, Clone(updated), x => x.Id == updated.Id);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
                return Clone(updated);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "UpsertCsomaglistaSablonTetel",
                    EntityId = item.Id,
                    CsomaglistaSablonTetelPayload = Clone(item)
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
                return Clone(item);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteCsomaglistaSablonTetelAsync(Guid tetelId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            store.CsomaglistaSablonTetelek.RemoveAll(t => t.Id == tetelId);

            try
            {
                await _remote.DeleteCsomaglistaSablonTetelAsync(tetelId, ct);
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                store.PendingOperations.Add(new PendingSyncOperation
                {
                    OperationType = "DeleteCsomaglistaSablonTetel",
                    EntityId = tetelId
                });
                SetOnlineState(true);
                await PersistStoreAsync(store, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- ISyncStatusService ---

    public async Task<SyncResult> SyncNowAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            var beforeCount = store.PendingOperations.Count;

            var flushed = await TryFlushPendingInternalAsync(store, ct);
            if (!flushed)
            {
                return new SyncResult(
                    false,
                    Math.Max(0, beforeCount - store.PendingOperations.Count),
                    store.PendingOperations.Count,
                    "A felhő jelenleg nem elérhető. A módosítások biztonságban vannak a telefonon.");
            }

            // Friss adatok lehúzása minden túrához, hogy a helyi tükör naprakész legyen
            try
            {
                var turak = await _remote.GetTurakAsync(ct);
                store.Turak = turak.Select(Clone).ToList();

                var allResztvevok = new List<Resztvevo>();
                var allFelszerelesek = new List<FelszerelesTetel>();
                var allFogasok = new List<Fogas>();
                var allKoltsegek = new List<KoltsegTetel>();

                foreach (var tura in store.Turak)
                {
                    var rTask = _remote.GetResztvevokAsync(tura.Id, ct);
                    var fTask = _remote.GetFelszerelesekAsync(tura.Id, ct);
                    var cTask = _remote.GetFogasokAsync(tura.Id, ct);
                    var kTask = TryGetRemoteKoltsegekOrEmptyAsync(tura.Id, ct);
                    await Task.WhenAll(rTask, fTask, cTask, kTask);

                    allResztvevok.AddRange((await rTask).Select(Clone));
                    allFelszerelesek.AddRange((await fTask).Select(Clone));
                    allFogasok.AddRange((await cTask).Select(Clone));
                    allKoltsegek.AddRange((await kTask).Select(Clone));
                }

                store.Resztvevok = allResztvevok;
                store.Felszerelesek = allFelszerelesek;
                store.Fogasok = allFogasok;
                store.Koltsegek = allKoltsegek;
                store.CsomaglistaSablonok = (await TryGetRemoteSablonokOrEmptyAsync(ct)).Select(Clone).ToList();
                store.CsomaglistaSablonTetelek = (await TryGetRemoteSablonTetelekOrEmptyAsync(ct)).Select(Clone).ToList();
                store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                SetOnlineState(false);
                await PersistStoreAsync(store, ct);
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "A szinkronizálás közben megszakadt a kapcsolat.");
                SetOnlineState(true);
                return new SyncResult(false, 0, store.PendingOperations.Count, "A szinkronizálás közben megszakadt a kapcsolat.");
            }

            var synced = Math.Max(0, beforeCount - store.PendingOperations.Count);
            return new SyncResult(
                true,
                synced,
                0,
                synced > 0
                    ? $"Sikeres szinkronizálás ({synced} offline módosítás feltöltve)."
                    : "Minden adat naprakész és elmentve a helyi tükörbe.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SyncResult> PushAllLocalToCloudAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);
            return await PushStoreToCloudInternalAsync(store, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FishingPlannerBackup> BuildSnapshotAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);

            // Ha online vagyunk és nincs függőben lévő offline módosítás, előtte frissítjük a tükröt a felhőből
            if (store.PendingOperations.Count == 0)
            {
                try
                {
                    var turak = await _remote.GetTurakAsync(ct);
                    var allResztvevok = new List<Resztvevo>();
                    var allFelszerelesek = new List<FelszerelesTetel>();
                    var allFogasok = new List<Fogas>();
                    var allKoltsegek = new List<KoltsegTetel>();

                    foreach (var tura in turak)
                    {
                        var rTask = _remote.GetResztvevokAsync(tura.Id, ct);
                        var fTask = _remote.GetFelszerelesekAsync(tura.Id, ct);
                        var cTask = _remote.GetFogasokAsync(tura.Id, ct);
                        var kTask = TryGetRemoteKoltsegekOrEmptyAsync(tura.Id, ct);
                        await Task.WhenAll(rTask, fTask, cTask, kTask);

                        allResztvevok.AddRange((await rTask).Select(Clone));
                        allFelszerelesek.AddRange((await fTask).Select(Clone));
                        allFogasok.AddRange((await cTask).Select(Clone));
                        allKoltsegek.AddRange((await kTask).Select(Clone));
                    }

                    store.Turak = turak.Select(Clone).ToList();
                    store.Resztvevok = allResztvevok;
                    store.Felszerelesek = allFelszerelesek;
                    store.Fogasok = allFogasok;
                    store.Koltsegek = allKoltsegek;
                    store.CsomaglistaSablonok = (await TryGetRemoteSablonokOrEmptyAsync(ct)).Select(Clone).ToList();
                    store.CsomaglistaSablonTetelek = (await TryGetRemoteSablonTetelekOrEmptyAsync(ct)).Select(Clone).ToList();
                    store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    SetOnlineState(false);
                    await PersistStoreAsync(store, ct);
                }
                catch (Exception ex) when (IsRecoverableOfflineException(ex))
                {
                    _logger.LogWarning(ex, "Offline mentés készül a helyi tükörből.");
                    SetOnlineState(true);
                }
            }

            return new FishingPlannerBackup
            {
                Version = 1,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Turak = store.Turak.Select(Clone).ToList(),
                Resztvevok = store.Resztvevok.Select(Clone).ToList(),
                Felszerelesek = store.Felszerelesek.Select(Clone).ToList(),
                Fogasok = store.Fogasok.Select(Clone).ToList(),
                Koltsegek = store.Koltsegek.Select(Clone).ToList(),
                CsomaglistaSablonok = store.CsomaglistaSablonok.Select(Clone).ToList(),
                CsomaglistaSablonTetelek = store.CsomaglistaSablonTetelek.Select(Clone).ToList()
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SyncResult> ImportSnapshotAsync(FishingPlannerBackup backup, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(backup);

        await _gate.WaitAsync(ct);
        try
        {
            var store = await EnsureStoreLoadedAsync(ct);

            foreach (var t in backup.Turak)
            {
                UpsertLocal(store.Turak, Clone(t), x => x.Id == t.Id);
            }

            foreach (var r in backup.Resztvevok)
            {
                UpsertLocal(store.Resztvevok, Clone(r), x => x.Id == r.Id);
            }

            foreach (var f in backup.Felszerelesek)
            {
                UpsertLocal(store.Felszerelesek, Clone(f), x => x.Id == f.Id);
            }

            foreach (var c in backup.Fogasok)
            {
                UpsertLocal(store.Fogasok, Clone(c), x => x.Id == c.Id);
            }

            foreach (var k in backup.Koltsegek)
            {
                UpsertLocal(store.Koltsegek, Clone(k), x => x.Id == k.Id);
            }

            foreach (var s in backup.CsomaglistaSablonok)
            {
                UpsertLocal(store.CsomaglistaSablonok, Clone(s), x => x.Id == s.Id);
            }

            foreach (var st in backup.CsomaglistaSablonTetelek)
            {
                UpsertLocal(store.CsomaglistaSablonTetelek, Clone(st), x => x.Id == st.Id);
            }

            await PersistStoreAsync(store, ct);

            // Megpróbáljuk azonnal feltölteni a visszaállított adatokat a Supabase adatbázisba is
            var pushResult = await PushStoreToCloudInternalAsync(store, ct);
            if (pushResult.Success)
            {
                return new SyncResult(
                    true,
                    pushResult.SyncedOperationsCount,
                    0,
                    $"Visszaállítva és felhőbe mentve: {backup.Turak.Count} túra, {backup.Resztvevok.Count} résztvevő, {backup.Felszerelesek.Count} felszerelés, {backup.Koltsegek.Count} költség, {backup.Fogasok.Count} fogás.");
            }

            // Ha jelenleg offline van, sorba állítjuk a feltöltést későbbre
            QueueFullStoreForSync(store);
            await PersistStoreAsync(store, ct);
            return new SyncResult(
                true,
                0,
                store.PendingOperations.Count,
                $"Helyben visszaállítva ({backup.Turak.Count} túra, {backup.Fogasok.Count} fogás). Amint újra elérhető a felhő, szinkronizálódik.");
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Belső szinkronizáló és tárhely segédek ---

    private async Task<SyncResult> PushStoreToCloudInternalAsync(OfflineCacheStore store, CancellationToken ct)
    {
        try
        {
            var uploadedCount = 0;

            foreach (var t in store.Turak)
            {
                await _remote.UpsertTuraAsync(t, ct);
                uploadedCount++;
            }

            foreach (var r in store.Resztvevok)
            {
                await _remote.UpsertResztvevoAsync(r, ct);
                uploadedCount++;
            }

            foreach (var f in store.Felszerelesek)
            {
                await _remote.UpsertFelszerelesAsync(f, ct);
                uploadedCount++;
            }

            foreach (var c in store.Fogasok)
            {
                c.KepUrl = await _mediaService.UploadDataUriIfNeededAsync(c.KepUrl, ct);
                await _remote.UpsertFogasAsync(c, ct);
                uploadedCount++;
            }

            foreach (var k in store.Koltsegek)
            {
                try
                {
                    await _remote.UpsertKoltsegAsync(k, ct);
                    uploadedCount++;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("add_koltsegek.sql", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "A költségek feltöltése kihagyva, mert a koltseg_tetelek tábla még nincs létrehozva.");
                    break;
                }
            }

            foreach (var s in store.CsomaglistaSablonok)
            {
                try
                {
                    await _remote.UpsertCsomaglistaSablonAsync(s, ct);
                    uploadedCount++;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("add_csomaglista_sablonok.sql", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "A csomaglista sablonok feltöltése kihagyva, mert a tábla még nincs létrehozva.");
                    break;
                }
            }

            foreach (var st in store.CsomaglistaSablonTetelek)
            {
                try
                {
                    await _remote.UpsertCsomaglistaSablonTetelAsync(st, ct);
                    uploadedCount++;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("add_csomaglista_sablonok.sql", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            store.PendingOperations.Clear();
            store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            SetOnlineState(false);
            await PersistStoreAsync(store, ct);

            return new SyncResult(
                true,
                uploadedCount,
                0,
                $"Az összes helyi adat ({store.Turak.Count} túra, {store.Felszerelesek.Count} felszerelés, {store.Koltsegek.Count} költség, {store.Fogasok.Count} fogás) sikeresen feltöltve a felhőbe.");
        }
        catch (Exception ex) when (IsRecoverableOfflineException(ex))
        {
            _logger.LogWarning(ex, "A helyi adatok felhőbe töltése sikertelen (offline).");
            SetOnlineState(true);
            return new SyncResult(false, 0, store.PendingOperations.Count, "A felhő jelenleg nem elérhető. A helyi adatok sértetlenek maradtak.");
        }
    }

    private static void QueueFullStoreForSync(OfflineCacheStore store)
    {
        store.PendingOperations.Clear();

        foreach (var t in store.Turak)
        {
            store.PendingOperations.Add(new PendingSyncOperation { OperationType = "UpsertTura", EntityId = t.Id, TuraPayload = Clone(t) });
        }

        foreach (var r in store.Resztvevok)
        {
            store.PendingOperations.Add(new PendingSyncOperation { OperationType = "UpsertResztvevo", EntityId = r.Id, ResztvevoPayload = Clone(r) });
        }

        foreach (var f in store.Felszerelesek)
        {
            store.PendingOperations.Add(new PendingSyncOperation { OperationType = "UpsertFelszereles", EntityId = f.Id, FelszerelesPayload = Clone(f) });
        }

        foreach (var c in store.Fogasok)
        {
            store.PendingOperations.Add(new PendingSyncOperation { OperationType = "UpsertFogas", EntityId = c.Id, FogasPayload = Clone(c) });
        }

        foreach (var k in store.Koltsegek)
        {
            store.PendingOperations.Add(new PendingSyncOperation { OperationType = "UpsertKoltseg", EntityId = k.Id, KoltsegPayload = Clone(k) });
        }

        foreach (var s in store.CsomaglistaSablonok)
        {
            store.PendingOperations.Add(new PendingSyncOperation { OperationType = "UpsertCsomaglistaSablon", EntityId = s.Id, CsomaglistaSablonPayload = Clone(s) });
        }

        foreach (var st in store.CsomaglistaSablonTetelek)
        {
            store.PendingOperations.Add(new PendingSyncOperation { OperationType = "UpsertCsomaglistaSablonTetel", EntityId = st.Id, CsomaglistaSablonTetelPayload = Clone(st) });
        }
    }

    private async Task<bool> TryFlushPendingInternalAsync(OfflineCacheStore store, CancellationToken ct)
    {
        if (store.PendingOperations.Count == 0)
        {
            return true;
        }

        var remaining = new List<PendingSyncOperation>(store.PendingOperations);
        var changed = false;

        foreach (var op in store.PendingOperations.ToList())
        {
            try
            {
                switch (op.OperationType)
                {
                    case "UpsertTura" when op.TuraPayload is not null:
                        await _remote.UpsertTuraAsync(op.TuraPayload, ct);
                        break;
                    case "DeleteTura":
                        await _remote.DeleteTuraAsync(op.EntityId, ct);
                        break;
                    case "UpsertResztvevo" when op.ResztvevoPayload is not null:
                        await _remote.UpsertResztvevoAsync(op.ResztvevoPayload, ct);
                        break;
                    case "DeleteResztvevo":
                        await _remote.DeleteResztvevoAsync(op.EntityId, ct);
                        break;
                    case "UpsertFelszereles" when op.FelszerelesPayload is not null:
                        await _remote.UpsertFelszerelesAsync(op.FelszerelesPayload, ct);
                        break;
                    case "ToggleBepakolva" when op.BoolValue.HasValue:
                        await _remote.ToggleBepakolvaAsync(op.EntityId, op.BoolValue.Value, ct);
                        break;
                    case "DeleteFelszereles":
                        await _remote.DeleteFelszerelesAsync(op.EntityId, ct);
                        break;
                    case "UpsertFogas" when op.FogasPayload is not null:
                        op.FogasPayload.KepUrl = await _mediaService.UploadDataUriIfNeededAsync(op.FogasPayload.KepUrl, ct);
                        await _remote.UpsertFogasAsync(op.FogasPayload, ct);
                        var localFogas = store.Fogasok.FirstOrDefault(x => x.Id == op.FogasPayload.Id);
                        if (localFogas is not null)
                        {
                            localFogas.KepUrl = op.FogasPayload.KepUrl;
                        }
                        break;
                    case "DeleteFogas":
                        await _remote.DeleteFogasAsync(op.EntityId, ct);
                        break;
                    case "UpsertKoltseg" when op.KoltsegPayload is not null:
                        await _remote.UpsertKoltsegAsync(op.KoltsegPayload, ct);
                        break;
                    case "DeleteKoltseg":
                        await _remote.DeleteKoltsegAsync(op.EntityId, ct);
                        break;
                    case "UpsertCsomaglistaSablon" when op.CsomaglistaSablonPayload is not null:
                        await _remote.UpsertCsomaglistaSablonAsync(op.CsomaglistaSablonPayload, ct);
                        break;
                    case "DeleteCsomaglistaSablon":
                        await _remote.DeleteCsomaglistaSablonAsync(op.EntityId, ct);
                        break;
                    case "UpsertCsomaglistaSablonTetel" when op.CsomaglistaSablonTetelPayload is not null:
                        await _remote.UpsertCsomaglistaSablonTetelAsync(op.CsomaglistaSablonTetelPayload, ct);
                        break;
                    case "DeleteCsomaglistaSablonTetel":
                        await _remote.DeleteCsomaglistaSablonTetelAsync(op.EntityId, ct);
                        break;
                }

                remaining.Remove(op);
                changed = true;
            }
            catch (Exception ex) when (IsRecoverableOfflineException(ex))
            {
                _logger.LogWarning(ex, "Offline sor szinkronizálása megszakadt: {Op}", op.OperationType);
                store.PendingOperations = remaining;
                SetOnlineState(true);
                if (changed)
                {
                    await PersistStoreAsync(store, ct);
                }

                return false;
            }
            catch (Exception ex)
            {
                // Pl. már törölt sor vagy sémaeltérés esetén eldobjuk a hibás tételt, hogy ne blokkolja a többit
                _logger.LogWarning(ex, "Egy függőben lévő művelet kihagyva szinkronizáláskor: {Op} ({Id})", op.OperationType, op.EntityId);
                remaining.Remove(op);
                changed = true;
            }
        }

        store.PendingOperations = remaining;
        if (changed)
        {
            store.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            SetOnlineState(false);
            await PersistStoreAsync(store, ct);
        }

        return store.PendingOperations.Count == 0;
    }

    private async Task<OfflineCacheStore> EnsureStoreLoadedAsync(CancellationToken ct)
    {
        if (_store is not null)
        {
            return _store;
        }

        try
        {
            if (File.Exists(_cacheFilePath))
            {
                await using var stream = File.OpenRead(_cacheFilePath);
                var loaded = await JsonSerializer.DeserializeAsync(
                    stream,
                    SupabaseJsonContext.Default.OfflineCacheStore,
                    ct);

                if (loaded is not null)
                {
                    _store = loaded;
                    SyncStatusChanged?.Invoke();
                    return _store;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "A helyi gyorsítótár fájl sérült vagy olvashatatlan, új üres tükör indul.");
        }

        _store = new OfflineCacheStore();
        return _store;
    }

    private async Task PersistStoreAsync(OfflineCacheStore store, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = _cacheFilePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    store,
                    SupabaseJsonContext.Default.OfflineCacheStore,
                    ct);
            }

            File.Move(tempPath, _cacheFilePath, overwrite: true);
            SyncStatusChanged?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Nem sikerült menteni a helyi gyorsítótárat.");
        }
    }

    private void SetOnlineState(bool isOffline)
    {
        if (IsOffline != isOffline)
        {
            IsOffline = isOffline;
            SyncStatusChanged?.Invoke();
        }
    }

    private static bool IsRecoverableOfflineException(Exception ex)
    {
        if (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return true;
        }

        if (ex is InvalidOperationException inv)
        {
            var msg = inv.Message;
            return msg.Contains("szerverhiba", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("hálózat", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("munkamenet", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void UpsertLocal<T>(List<T> list, T item, Predicate<T> match)
    {
        var idx = list.FindIndex(match);
        if (idx >= 0)
        {
            list[idx] = item;
        }
        else
        {
            list.Add(item);
        }
    }

    private static Tura Clone(Tura t) => new()
    {
        Id = t.Id,
        Nev = t.Nev,
        Helyszin = t.Helyszin,
        KezdoDatum = t.KezdoDatum,
        VegDatum = t.VegDatum
    };

    private static Resztvevo Clone(Resztvevo r) => new()
    {
        Id = r.Id,
        TuraId = r.TuraId,
        UserId = r.UserId,
        Nev = r.Nev,
        Szerepkor = r.Szerepkor,
        AktivE = r.AktivE
    };

    private static FelszerelesTetel Clone(FelszerelesTetel f) => new()
    {
        Id = f.Id,
        TuraId = f.TuraId,
        ResztvevoId = f.ResztvevoId,
        Nev = f.Nev,
        Kategoria = f.Kategoria,
        Mennyiseg = f.Mennyiseg,
        Bepakolva = f.Bepakolva,
        KozosE = f.KozosE
    };

    private static Fogas Clone(Fogas f) => new()
    {
        Id = f.Id,
        TuraId = f.TuraId,
        ResztvevoId = f.ResztvevoId,
        Halfaj = f.Halfaj,
        DarabAlapuE = f.DarabAlapuE,
        Mennyiseg = f.DarabAlapuE ? Math.Max(1, f.Mennyiseg) : 1,
        VisszaengedettE = f.VisszaengedettE,
        SulyKg = f.DarabAlapuE ? null : f.SulyKg,
        HosszCm = f.DarabAlapuE ? null : f.HosszCm,
        Idopont = f.Idopont,
        Csali = f.Csali,
        Megjegyzes = f.Megjegyzes,
        KepUrl = f.KepUrl
    };

    private async Task<IReadOnlyList<KoltsegTetel>> TryGetRemoteKoltsegekOrEmptyAsync(Guid turaId, CancellationToken ct)
    {
        try
        {
            return await _remote.GetKoltsegekAsync(turaId, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("add_koltsegek.sql", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "A koltseg_tetelek tábla még nincs létrehozva, szinkronizálásnál kihagyva.");
            return [];
        }
    }

    private static KoltsegTetel Clone(KoltsegTetel k) => new()
    {
        Id = k.Id,
        TuraId = k.TuraId,
        Nev = k.Nev,
        Osszeg = k.Osszeg,
        KozosE = k.KozosE,
        ResztvevoId = k.KozosE ? null : k.ResztvevoId,
        FizetoResztvevoId = k.FizetoResztvevoId,
        Megjegyzes = k.Megjegyzes
    };

    private async Task<IReadOnlyList<CsomaglistaSablon>> TryGetRemoteSablonokOrEmptyAsync(CancellationToken ct)
    {
        try
        {
            return await _remote.GetCsomaglistaSablonokAsync(ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("add_csomaglista_sablonok.sql", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "A csomaglista_sablonok tábla még nincs létrehozva, szinkronizálásnál kihagyva.");
            return [];
        }
    }

    private async Task<IReadOnlyList<CsomaglistaSablonTetel>> TryGetRemoteSablonTetelekOrEmptyAsync(CancellationToken ct)
    {
        try
        {
            return await _remote.GetAllCsomaglistaSablonTetelekAsync(ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("add_csomaglista_sablonok.sql", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }
    }

    private static CsomaglistaSablon Clone(CsomaglistaSablon s) => new()
    {
        Id = s.Id,
        Nev = s.Nev,
        Leiras = s.Leiras,
        CreatedAt = s.CreatedAt
    };

    private static CsomaglistaSablonTetel Clone(CsomaglistaSablonTetel t) => new()
    {
        Id = t.Id,
        SablonId = t.SablonId,
        Nev = t.Nev,
        Kategoria = t.Kategoria,
        Mennyiseg = Math.Max(1, t.Mennyiseg),
        KozosE = t.KozosE
    };
}
