using System.Text.Json;
using FishingPlanner.Models;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// A teljes adatbázis-állapot JSON exportját (megosztás / helyi mentés) és
/// visszaállítását (helyi tükör + Supabase újratöltés) végző szolgáltatás.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly ISyncStatusService _syncService;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        ISyncStatusService syncService,
        ILogger<BackupService> logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BackupOperationResult> ExportBackupAsync(CancellationToken ct = default)
    {
        try
        {
            var snapshot = await _syncService.BuildSnapshotAsync(ct);
            var fileName = $"FishingPlanner_Backup_{DateTime.Now:yyyyMMdd_HHmm}.json";
            var json = JsonSerializer.Serialize(snapshot, SupabaseJsonContext.Default.FishingPlannerBackup);

#if WINDOWS
            var docsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FishingPlanner_Backups");
            Directory.CreateDirectory(docsDir);
            var targetPath = Path.Combine(docsDir, fileName);
            await File.WriteAllTextAsync(targetPath, json, ct);

            return new BackupOperationResult(
                true,
                $"Biztonsági mentés elmentve ({snapshot.Turak.Count} túra, {snapshot.Fogasok.Count} fogás): {targetPath}",
                targetPath);
#else
            var cachePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(cachePath, json, ct);

            await MainThread.InvokeOnMainThreadAsync(() =>
                Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "FishingPlanner biztonsági mentés mentése / megosztása",
                    File = new ShareFile(cachePath, "application/json")
                }));

            return new BackupOperationResult(
                true,
                $"Biztonsági mentés elkészült ({snapshot.Turak.Count} túra, {snapshot.Fogasok.Count} fogás).",
                cachePath);
#endif
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a biztonsági mentés exportálása közben.");
            return new BackupOperationResult(false, $"Nem sikerült elkészíteni a mentést: {ex.Message}");
        }
    }

    public async Task<BackupOperationResult> ImportBackupAsync(CancellationToken ct = default)
    {
        try
        {
            var jsonFileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, ["application/json", "text/plain", "*/*"] },
                { DevicePlatform.WinUI, [".json"] }
            });

            var file = await MainThread.InvokeOnMainThreadAsync(() =>
                FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Válaszd ki a FishingPlanner .json biztonsági mentést",
                    FileTypes = jsonFileTypes
                }));

            if (file is null)
            {
                return new BackupOperationResult(false, "A visszaállítás meg lett szakítva.");
            }

            await using var stream = await file.OpenReadAsync();
            var backup = await JsonSerializer.DeserializeAsync(
                stream,
                SupabaseJsonContext.Default.FishingPlannerBackup,
                ct);

            if (backup is null || (backup.Turak.Count == 0 && backup.Fogasok.Count == 0 && backup.Felszerelesek.Count == 0 && backup.Koltsegek.Count == 0 && backup.CsomaglistaSablonok.Count == 0))
            {
                return new BackupOperationResult(false, "A kiválasztott fájl nem tartalmaz érvényes FishingPlanner mentést.");
            }

            var result = await _syncService.ImportSnapshotAsync(backup, ct);
            return new BackupOperationResult(result.Success, result.Message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Érvénytelen JSON formátumú mentésfájl.");
            return new BackupOperationResult(false, "A kiválasztott fájl sérült vagy nem érvényes JSON mentés.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hiba történt a biztonsági mentés visszatöltése közben.");
            return new BackupOperationResult(false, $"Nem sikerült visszatölteni a mentést: {ex.Message}");
        }
    }
}
