namespace FishingPlanner.Services;

/// <summary>
/// Kép kiválasztását, tömörítését, felhős feltöltését és törlését kezelő interfész.
/// </summary>
public interface IMediaService
{
    bool IsCaptureSupported { get; }

    /// <summary>
    /// Meglévő fotó kiválasztása a galériából, automatikus átméretezéssel és JPEG tömörítéssel.
    /// Online módban Supabase Storage URL-t, offline módban helyi Data URI-t ad vissza.
    /// </summary>
    Task<string?> PickPhotoAsync(CancellationToken ct = default);

    /// <summary>
    /// Új fotó készítése kamerával, automatikus átméretezéssel és JPEG tömörítéssel.
    /// </summary>
    Task<string?> CapturePhotoAsync(CancellationToken ct = default);

    /// <summary>
    /// Offline Data URI feltöltése a Supabase Storage-ba szinkronizáláskor; visszatér a publikus URL-lel.
    /// </summary>
    Task<string?> UploadDataUriIfNeededAsync(string? kepUrl, CancellationToken ct = default);

    /// <summary>
    /// Korábbi (lecserélt vagy törölt fogáshoz tartozó) fotó eltávolítása a Supabase Storage bucketből.
    /// </summary>
    Task DeletePhotoAsync(string? kepUrl, CancellationToken ct = default);
}
