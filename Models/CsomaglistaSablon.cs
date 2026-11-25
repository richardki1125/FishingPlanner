using System.ComponentModel.DataAnnotations;

namespace FishingPlanner.Models;

/// <summary>
/// Újrahasználható csomaglista (sablon), amely tetszőleges túrákhoz hozzáadható létrehozáskor vagy később.
/// </summary>
public class CsomaglistaSablon
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "A csomaglista neve kötelező.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "A csomaglista neve 2 és 100 karakter között legyen.")]
    public string Nev { get; set; } = string.Empty;

    [StringLength(300, ErrorMessage = "A leírás legfeljebb 300 karakter lehet.")]
    public string? Leiras { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Egy újrahasználható csomaglistához tartozó felszerelési tétel.
/// </summary>
public class CsomaglistaSablonTetel : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "A csomaglista azonosítója kötelező.")]
    public Guid SablonId { get; set; }

    [Required(ErrorMessage = "A felszerelés neve kötelező.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "A felszerelés neve 2 és 100 karakter között legyen.")]
    public string Nev { get; set; } = string.Empty;

    [Required(ErrorMessage = "A kategória megadása kötelező.")]
    [StringLength(50, ErrorMessage = "A kategória legfeljebb 50 karakter lehet.")]
    public string Kategoria { get; set; } = "Egyéb";

    [Range(1, 500, ErrorMessage = "A mennyiség 1 és 500 között legyen.")]
    public int Mennyiseg { get; set; } = 1;

    public bool KozosE { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SablonId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Érvényes csomaglista kiválasztása kötelező.",
                [nameof(SablonId)]);
        }
    }
}
