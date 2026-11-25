using System.ComponentModel.DataAnnotations;

namespace FishingPlanner.Models;

/// <summary>
/// A fogási napló egy bejegyzését reprezentáló entitás.
/// Támogatja a súly + hossz alapú (egyedi hal) és a darabszám alapú (pl. kárász, keszeg) rögzítést is.
/// </summary>
public class Fogas : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "A túra azonosítója kötelező.")]
    public Guid TuraId { get; set; }

    [Required(ErrorMessage = "A horgász kiválasztása kötelező.")]
    public Guid ResztvevoId { get; set; }

    [Required(ErrorMessage = "A halfaj megadása kötelező.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "A halfaj 2 és 60 karakter között legyen.")]
    public string Halfaj { get; set; } = string.Empty;

    /// <summary>
    /// Igaz, ha a fogás nem súlyra, hanem darabszámra (mennyiségre) megy (pl. kárász).
    /// </summary>
    public bool DarabAlapuE { get; set; }

    /// <summary>
    /// Darabszám alapú fogásnál a kifogott halak száma; súly alapú fogásnál mindig 1.
    /// </summary>
    [Range(1, 500, ErrorMessage = "A mennyiség 1 és 500 db között legyen.")]
    public int Mennyiseg { get; set; } = 1;

    /// <summary>
    /// Igaz, ha a hal vissza lett engedve (Catch &amp; Release); hamis, ha megtartották.
    /// </summary>
    public bool VisszaengedettE { get; set; } = true;

    public double? SulyKg { get; set; }

    public double? HosszCm { get; set; }

    [Required(ErrorMessage = "A fogás időpontja kötelező.")]
    public DateTime Idopont { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "A csali megadása kötelező.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "A csali neve 2 és 80 karakter között legyen.")]
    public string Csali { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "A megjegyzés legfeljebb 500 karakter lehet.")]
    public string? Megjegyzes { get; set; }

    /// <summary>
    /// Helyi Data URI vagy felhős tárhely (Supabase Storage) kép URL.
    /// </summary>
    public string? KepUrl { get; set; }

    /// <summary>
    /// A bejegyzésben szereplő halak tényleges darabszáma.
    /// </summary>
    public int EffektiveDb => DarabAlapuE ? Math.Max(1, Mennyiseg) : 1;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TuraId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Érvényes túra kiválasztása kötelező.",
                [nameof(TuraId)]);
        }

        if (ResztvevoId == Guid.Empty)
        {
            yield return new ValidationResult(
                "A horgász kiválasztása kötelező.",
                [nameof(ResztvevoId)]);
        }

        if (Idopont > DateTime.Now.AddMinutes(5))
        {
            yield return new ValidationResult(
                "A fogás időpontja nem lehet a jövőben.",
                [nameof(Idopont)]);
        }

        if (DarabAlapuE)
        {
            if (Mennyiseg is < 1 or > 500)
            {
                yield return new ValidationResult(
                    "A mennyiség 1 és 500 db között legyen.",
                    [nameof(Mennyiseg)]);
            }
        }
        else
        {
            if (SulyKg is null or < 0.05 or > 250.0)
            {
                yield return new ValidationResult(
                    "A hal súlya 0,05 és 250 kg között legyen.",
                    [nameof(SulyKg)]);
            }

            if (HosszCm is null or < 5.0 or > 350.0)
            {
                yield return new ValidationResult(
                    "A hal hossza 5 és 350 cm között legyen.",
                    [nameof(HosszCm)]);
            }
        }
    }
}
