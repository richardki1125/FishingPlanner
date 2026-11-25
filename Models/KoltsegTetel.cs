using System.ComponentModel.DataAnnotations;

namespace FishingPlanner.Models;

/// <summary>
/// A horgásztúrához tartozó költségmezőt (pl. Ételek, Italok, Etetőanyag) reprezentáló entitás.
/// </summary>
public class KoltsegTetel : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "A túra azonosítója kötelező.")]
    public Guid TuraId { get; set; }

    [Required(ErrorMessage = "A költségmező neve kötelező.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "A megnevezés 2 és 100 karakter között legyen.")]
    public string Nev { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "100000000", ErrorMessage = "Az összeg 0 és 100 000 000 között legyen.")]
    public decimal Osszeg { get; set; }

    public bool KozosE { get; set; } = true;

    /// <summary>
    /// Egyéni költség esetén az érintett résztvevő azonosítója; közös költségnél null.
    /// </summary>
    public Guid? ResztvevoId { get; set; }

    /// <summary>
    /// Opcionálisan megadható, hogy ki fizette (előlegezte meg) a tételt.
    /// </summary>
    public Guid? FizetoResztvevoId { get; set; }

    [StringLength(300, ErrorMessage = "A megjegyzés legfeljebb 300 karakter lehet.")]
    public string? Megjegyzes { get; set; }

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
                "Egyéni költség esetén kötelező résztvevőt választani.",
                [nameof(ResztvevoId)]);
        }

        if (KozosE && ResztvevoId is not null)
        {
            yield return new ValidationResult(
                "Közös költség nem rendelhető egyetlen résztvevőhöz.",
                [nameof(ResztvevoId)]);
        }
    }
}

/// <summary>
/// Egy résztvevőre jutó teljes költség, közös/egyéni bontás és elszámolási egyenleg összesítője.
/// </summary>
public sealed record SzemelyKoltsegOsszesites(
    Guid ResztvevoId,
    string Nev,
    string Szerepkor,
    string? ProfilkepUrl,
    bool AktivE,
    decimal KozosResz,
    decimal EgyeniKoltseg,
    decimal OsszesKoltseg,
    decimal BefizetettOsszeg,
    decimal ElszamolhatoKoltseg,
    decimal Egyenleg,
    double AranySzazalek);

/// <summary>
/// Minimális számú elszámolási javaslat a fizetett tételek kiegyenlítéséhez.
/// </summary>
public sealed record ElszamolasiUtalas(
    string KiFizetNev,
    string KinekFizetNev,
    decimal Osszeg);
