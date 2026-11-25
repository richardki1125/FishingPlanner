using FishingPlanner.Models;

namespace FishingPlanner.Services;

/// <summary>
/// A túraadatok JSON biztonsági mentésének exportálását és visszaállítását kezelő interfész.
/// </summary>
public interface IBackupService
{
    Task<BackupOperationResult> ExportBackupAsync(CancellationToken ct = default);
    Task<BackupOperationResult> ImportBackupAsync(CancellationToken ct = default);
}
