using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

/// <summary>
/// Adminisztrátori adatbiztonság, offline szinkronizálás, JSON biztonsági mentés és fiókkezelés ViewModel-je.
/// </summary>
public sealed class BeallitasokViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISyncStatusService _syncService;
    private readonly IBackupService _backupService;

    public string? CurrentUserName => _authService.CurrentUser?.Felhasznalonev;
    public bool IsAdmin => _authService.IsAdmin;

    public bool IsOffline => _syncService.IsOffline;
    public int PendingSyncCount => _syncService.PendingSyncCount;
    public string LastSyncedText => _syncService.LastSyncedAtUtc.HasValue
        ? _syncService.LastSyncedAtUtc.Value.ToLocalTime().ToString("yyyy.MM.dd. HH:mm")
        : "Még nem történt szinkronizálás";

    public BeallitasokViewModel(
        IAuthService authService,
        ISyncStatusService syncService,
        IBackupService backupService,
        ILogger<BeallitasokViewModel> logger)
        : base(logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
    }

    public Task LoadAsync()
    {
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public async Task RefreshSilentlyAsync(bool isManual = false, CancellationToken ct = default)
    {
        if (!isManual || !IsAdmin || IsLoading || IsBusy)
        {
            return;
        }

        try
        {
            await _syncService.SyncNowAsync(ct);
            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            // Normál megszakítás
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Szinkronizálás sikertelen a beállítások lehúzásakor.");
        }
    }

    public async Task SyncNowAsync()
    {
        if (!IsAdmin)
        {
            return;
        }

        Models.SyncResult? result = null;
        var ok = await ExecuteAsync(async () =>
        {
            result = await _syncService.SyncNowAsync();
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }
        }, "A szinkronizálás nem sikerült.");

        if (ok && result is not null)
        {
            SetSuccess(result.Message);
        }
    }

    public async Task PushAllLocalToCloudAsync()
    {
        if (!IsAdmin)
        {
            return;
        }

        Models.SyncResult? result = null;
        var ok = await ExecuteAsync(async () =>
        {
            result = await _syncService.PushAllLocalToCloudAsync();
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }
        }, "Nem sikerült feltölteni a helyi adatokat a felhőbe.");

        if (ok && result is not null)
        {
            SetSuccess(result.Message);
        }
    }

    public async Task ExportBackupAsync()
    {
        if (!IsAdmin)
        {
            return;
        }

        Models.BackupOperationResult? result = null;
        var ok = await ExecuteAsync(async () =>
        {
            result = await _backupService.ExportBackupAsync();
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }
        }, "Nem sikerült elkészíteni a biztonsági mentést.");

        if (ok && result is not null)
        {
            SetSuccess(result.Message);
        }
    }

    public async Task ImportBackupAsync()
    {
        if (!IsAdmin)
        {
            return;
        }

        Models.BackupOperationResult? result = null;
        var ok = await ExecuteAsync(async () =>
        {
            result = await _backupService.ImportBackupAsync();
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }
        }, "Nem sikerült visszatölteni a biztonsági mentést.");

        if (ok && result is not null)
        {
            SetSuccess(result.Message);
        }
    }

    public async Task SignOutAsync()
    {
        await ExecuteAsync(async () =>
        {
            await _authService.SignOutAsync();
        }, "Hiba történt kijelentkezés közben.");
    }
}
