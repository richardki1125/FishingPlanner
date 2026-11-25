using System.ComponentModel.DataAnnotations;

namespace FishingPlanner.Models;

/// <summary>
/// A csomaglistában szereplő közös vagy egyéni felszerelési tételt reprezentáló entitás.
/// </summary>
public class FelszerelesTetel : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "A túra azonosítója kötelező.")]
    public Guid TuraId { get; set; }

    /// <summary>
    /// A felelős résztvevő azonosítója; közös felszerelés esetén null.
    /// </summary>
    public Guid? ResztvevoId { get; set; }

    [Required(ErrorMessage = "A felszerelés neve kötelező.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "A felszerelés neve 2 és 100 karakter között legyen.")]
    public string Nev { get; set; } = string.Empty;

    [Required(ErrorMessage = "A kategória megadása kötelező.")]
    [StringLength(50, ErrorMessage = "A kategória legfeljebb 50 karakter lehet.")]
    public string Kategoria { get; set; } = "Egyéb";

    [Range(1, 500, ErrorMessage = "A mennyiség 1 és 500 között legyen.")]
    public int Mennyiseg { get; set; } = 1;

    public bool Bepakolva { get; set; }

    public bool KozosE { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TuraId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Érvényes túra kiválasztása kötelező.",
                [nameof(TuraId)]);
        }

        if (!KozosE && (ResztvevoId is null || ResztvevoId == Guid.Empty))
        {
            yield return new ValidationResult(
                "Egyéni felszerelés esetén kötelező résztvevőt választani.",
                [nameof(ResztvevoId)]);
        }

        if (KozosE && ResztvevoId is not null)
        {
            yield return new ValidationResult(
                "Közös felszerelés nem rendelhető egyetlen résztvevőhöz.",
                [nameof(ResztvevoId)]);
        }
    }
}
