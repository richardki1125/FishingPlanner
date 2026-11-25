using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// MAUI MediaPicker alapú képszolgáltatás natív átméretezéssel (max. 1280 px) és 78%-os JPEG tömörítéssel,
/// Supabase Storage feltöltéssel, offline Data URI tartalékkal és árva képek automatikus törlésével.
/// </summary>
public sealed class MauiMediaService : IMediaService
{
    private const long MaxRawFileSizeBytes = 25 * 1024 * 1024; // 25 MB nyers fotó limit (tömörítés előtt)
    private const int MaxImageDimensionPx = 1280;
    private const int JpegQualityPercent = 78;
    private const string StorageBucket = "fogas-kepek";
    private const string PublicStorageMarker = "/storage/v1/object/public/fogas-kepek/";

    private readonly HttpClient _httpClient;
    private readonly ISupabaseConfigService _configService;
    private readonly IAuthService _authService;
    private readonly ILogger<MauiMediaService> _logger;

    public MauiMediaService(
        HttpClient httpClient,
        ISupabaseConfigService configService,
        IAuthService authService,
        ILogger<MauiMediaService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsCaptureSupported
    {
        get
        {
            try
            {
                return MediaPicker.Default.IsCaptureSupported;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<string?> PickPhotoAsync(CancellationToken ct = default)
    {
        try
        {
            var photos = await MainThread.InvokeOnMainThreadAsync(() =>
                MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
                {
                    Title = "Válassz fogásról készült fotót",
                    SelectionLimit = 1
                }));

            var photo = photos?.FirstOrDefault();
            if (photo is null)
            {
                return null;
            }

            return await ProcessAndUploadPhotoAsync(photo, ct);
        }
        catch (PermissionException ex)
        {
            _logger.LogWarning(ex, "Hiányzó jogosultság a galéria megnyitásához.");
            throw new InvalidOperationException("Nincs engedély a galéria eléréséhez. Engedélyezd a rendszerbeállításokban.", ex);
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "A képválasztás ezen az eszközön nem támogatott.");
            throw new InvalidOperationException("A képválasztás ezen az eszközön nem támogatott.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            _logger.LogError(ex, "Hiba történt a kép kiválasztása közben.");
            throw new InvalidOperationException("Nem sikerült betölteni a kiválasztott képet.", ex);
        }
    }

    public async Task<string?> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (!IsCaptureSupported)
        {
            throw new InvalidOperationException("A kamera használata ezen az eszközön nem támogatott.");
        }

        try
        {
            var photo = await MainThread.InvokeOnMainThreadAsync(() =>
                MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
                {
                    Title = "Fogás fotózása"
                }));

            if (photo is null)
            {
                return null;
            }

            return await ProcessAndUploadPhotoAsync(photo, ct);
        }
        catch (PermissionException ex)
        {
            _logger.LogWarning(ex, "Hiányzó jogosultság a kamera használatához.");
            throw new InvalidOperationException("Nincs engedély a kamera használatához. Engedélyezd a rendszerbeállításokban.", ex);
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "A kamera ezen az eszközön nem támogatott.");
            throw new InvalidOperationException("A kamera használata ezen az eszközön nem támogatott.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            _logger.LogError(ex, "Hiba történt a fotózás közben.");
            throw new InvalidOperationException("Nem sikerült elkészíteni a fotót.", ex);
        }
    }

    public async Task<string?> UploadDataUriIfNeededAsync(string? kepUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kepUrl) ||
            !kepUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return kepUrl;
        }

        var commaIndex = kepUrl.IndexOf(',');
        if (commaIndex <= 0)
        {
            return kepUrl;
        }

        var header = kepUrl[5..commaIndex]; // pl. image/jpeg;base64
        var contentType = header.Split(';')[0];
        var base64 = kepUrl[(commaIndex + 1)..];
        var bytes = Convert.FromBase64String(base64);

        var config = await _configService.GetConfigAsync(ct);
        var token = await _authService.GetValidAccessTokenAsync(ct);

        if (!config.IsConfigured || string.IsNullOrWhiteSpace(token))
        {
            return kepUrl;
        }

        return await UploadToSupabaseStorageAsync(
            config.NormalizedUrl,
            config.AnonKey,
            token,
            bytes,
            string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
            ct);
    }

    public async Task DeletePhotoAsync(string? kepUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kepUrl))
        {
            return;
        }

        var markerIndex = kepUrl.IndexOf(PublicStorageMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return;
        }

        var objectPath = kepUrl[(markerIndex + PublicStorageMarker.Length)..].TrimStart('/');
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return;
        }

        try
        {
            var config = await _configService.GetConfigAsync(ct);
            var token = await _authService.GetValidAccessTokenAsync(ct);
            if (!config.IsConfigured || string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var deleteUrl = $"{config.NormalizedUrl}/storage/v1/object/{StorageBucket}/{objectPath}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
            request.Headers.Add("apikey", config.AnonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nem sikerült törölni a régi képet a Storage-ból ({Status}): {Path}", response.StatusCode, objectPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A kép takarítási hiba nem akaszthatja meg a fogás mentését/törlését
            _logger.LogWarning(ex, "Hiba történt a régi fotó törlésekor a Storage-ból: {Path}", objectPath);
        }
    }

    private async Task<string> ProcessAndUploadPhotoAsync(FileResult photo, CancellationToken ct)
    {
        var rawBytes = await ReadBytesAsync(photo, ct);
        var (compressedBytes, contentType) = await CompressImageAsync(rawBytes, photo.ContentType, photo.FileName, ct);

        try
        {
            var config = await _configService.GetConfigAsync(ct);
            var token = await _authService.GetValidAccessTokenAsync(ct);

            if (config.IsConfigured && !string.IsNullOrWhiteSpace(token))
            {
                return await UploadToSupabaseStorageAsync(
                    config.NormalizedUrl,
                    config.AnonKey,
                    token,
                    compressedBytes,
                    contentType,
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Offline esetben a tömörített kép Data URI-ként helyben mentődik, és szinkronizáláskor feltöltődik a felhőbe
            _logger.LogWarning(ex, "A felhős képfeltöltés sikertelen (offline mód), helyi tömörített mentés használata.");
        }

        return $"data:{contentType};base64,{Convert.ToBase64String(compressedBytes)}";
    }

    private async Task<string> UploadToSupabaseStorageAsync(
        string supabaseUrl,
        string anonKey,
        string accessToken,
        byte[] bytes,
        string contentType,
        CancellationToken ct)
    {
        var ext = contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? ".png"
            : contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
            : ".jpg";

        var userFolder = _authService.CurrentUser?.UserId.ToString("N") ?? "kozos";
        var objectPath = $"{userFolder}/{Guid.NewGuid():N}{ext}";

        var uploadUrl = $"{supabaseUrl}/storage/v1/object/{StorageBucket}/{objectPath}";
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("x-upsert", "true");

        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Supabase Storage feltöltési hiba ({Status}): {Error}", response.StatusCode, err);
            throw new HttpRequestException($"Supabase Storage feltöltési hiba ({(int)response.StatusCode}).");
        }

        return $"{supabaseUrl}/storage/v1/object/public/{StorageBucket}/{objectPath}";
    }

    /// <summary>
    /// Natív platform-kódekkel átméretezi (max. 1280 px) és JPEG 78%-kal tömöríti a képet (~150-250 KB),
    /// így 90-95% felhőtárhely takarítható meg.
    /// </summary>
    private async Task<(byte[] Bytes, string ContentType)> CompressImageAsync(
        byte[] rawBytes,
        string? originalContentType,
        string originalFileName,
        CancellationToken ct)
    {
        try
        {
#if ANDROID
            return await Task.Run(() => CompressOnAndroid(rawBytes), ct);
#elif WINDOWS
            return await CompressOnWindowsAsync(rawBytes);
#else
            await Task.CompletedTask;
            return (rawBytes, GetContentType(originalContentType, originalFileName));
#endif
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "A kép tömörítése nem sikerült, eredeti kép használata.");
            return (rawBytes, GetContentType(originalContentType, originalFileName));
        }
    }

#if ANDROID
    private static (byte[] Bytes, string ContentType) CompressOnAndroid(byte[] rawBytes)
    {
        // EXIF tájolás kiolvasása (álló mobilfotók helyes elforgatásához)
        var rotationDegrees = 0;
        try
        {
            using var exifStream = new MemoryStream(rawBytes);
            using var exif = new Android.Media.ExifInterface(exifStream);
            var orientation = (Android.Media.Orientation)exif.GetAttributeInt(
                Android.Media.ExifInterface.TagOrientation,
                (int)Android.Media.Orientation.Normal);

            rotationDegrees = orientation switch
            {
                Android.Media.Orientation.Rotate90 => 90,
                Android.Media.Orientation.Rotate180 => 180,
                Android.Media.Orientation.Rotate270 => 270,
                _ => 0
            };
        }
        catch
        {
            rotationDegrees = 0;
        }

        // Méret előzetes lekérdezése memóriakímélő inSampleSize számításhoz
        using var boundsOpts = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeByteArray(rawBytes, 0, rawBytes.Length, boundsOpts);

        var sampleSize = 1;
        var longest = Math.Max(boundsOpts.OutWidth, boundsOpts.OutHeight);
        while (longest / (sampleSize * 2) >= MaxImageDimensionPx)
        {
            sampleSize *= 2;
        }

        using var decodeOpts = new Android.Graphics.BitmapFactory.Options { InSampleSize = sampleSize };
        using var decoded = Android.Graphics.BitmapFactory.DecodeByteArray(rawBytes, 0, rawBytes.Length, decodeOpts);
        if (decoded is null)
        {
            return (rawBytes, "image/jpeg");
        }

        Android.Graphics.Bitmap working = decoded;
        Android.Graphics.Bitmap? scaled = null;
        Android.Graphics.Bitmap? rotated = null;

        try
        {
            var maxSide = Math.Max(working.Width, working.Height);
            if (maxSide > MaxImageDimensionPx)
            {
                var scale = (double)MaxImageDimensionPx / maxSide;
                var targetW = Math.Max(1, (int)Math.Round(working.Width * scale));
                var targetH = Math.Max(1, (int)Math.Round(working.Height * scale));
                scaled = Android.Graphics.Bitmap.CreateScaledBitmap(working, targetW, targetH, true);
                if (scaled is not null)
                {
                    working = scaled;
                }
            }

            if (rotationDegrees != 0)
            {
                using var matrix = new Android.Graphics.Matrix();
                matrix.PostRotate(rotationDegrees);
                rotated = Android.Graphics.Bitmap.CreateBitmap(working, 0, 0, working.Width, working.Height, matrix, true);
                if (rotated is not null)
                {
                    working = rotated;
                }
            }

            using var outStream = new MemoryStream();
            working.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, JpegQualityPercent, outStream);
            return (outStream.ToArray(), "image/jpeg");
        }
        finally
        {
            rotated?.Dispose();
            scaled?.Dispose();
        }
    }
#endif

#if WINDOWS
    private static async Task<(byte[] Bytes, string ContentType)> CompressOnWindowsAsync(byte[] rawBytes)
    {
        using var inputStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(inputStream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(rawBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        inputStream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(inputStream);

        var origW = decoder.OrientedPixelWidth;
        var origH = decoder.OrientedPixelHeight;
        var maxSide = Math.Max(origW, origH);

        uint targetW = origW;
        uint targetH = origH;
        if (maxSide > MaxImageDimensionPx)
        {
            var scale = (double)MaxImageDimensionPx / maxSide;
            targetW = Math.Max(1u, (uint)Math.Round(origW * scale));
            targetH = Math.Max(1u, (uint)Math.Round(origH * scale));
        }

        var transform = new Windows.Graphics.Imaging.BitmapTransform
        {
            ScaledWidth = targetW,
            ScaledHeight = targetH,
            InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant
        };

        var pixelData = await decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            transform,
            Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);

        using var outputStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var propertySet = new Windows.Graphics.Imaging.BitmapPropertySet
        {
            {
                "ImageQuality",
                new Windows.Graphics.Imaging.BitmapTypedValue(
                    JpegQualityPercent / 100.0f,
                    Windows.Foundation.PropertyType.Single)
            }
        };

        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
            Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId,
            outputStream,
            propertySet);

        encoder.SetPixelData(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            targetW,
            targetH,
            96,
            96,
            pixelData.DetachPixelData());

        await encoder.FlushAsync();

        var resultBytes = new byte[outputStream.Size];
        using (var reader = new Windows.Storage.Streams.DataReader(outputStream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)outputStream.Size);
            reader.ReadBytes(resultBytes);
        }

        return (resultBytes, "image/jpeg");
    }
#endif

    private static async Task<byte[]> ReadBytesAsync(FileResult photo, CancellationToken ct)
    {
        await using var sourceStream = await photo.OpenReadAsync();
        using var memoryStream = new MemoryStream();

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > MaxRawFileSizeBytes)
            {
                throw new InvalidOperationException("A kiválasztott kép túl nagy (maximum 25 MB engedélyezett).");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        }

        return memoryStream.ToArray();
    }

    private static string GetContentType(string? originalContentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(originalContentType))
        {
            return originalContentType;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }
}
