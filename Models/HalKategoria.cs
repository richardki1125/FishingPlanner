namespace FishingPlanner.Models;

/// <summary>
/// A halak főkategóriája (pl. Ponty, Amur, Kárász, Ragadozók).
/// </summary>
public sealed class HalKategoria
{
    public Guid Id { get; set; }
    public string Nev { get; set; } = string.Empty;
    public int Sorrend { get; set; } = 100;
}

/// <summary>
/// Egy hal főkategóriához tartozó konkrét alkategória / fajta (pl. Ponty -> Tükörponty).
/// </summary>
public sealed class HalAlkategoria
{
    public Guid Id { get; set; }
    public Guid KategoriaId { get; set; }
    public string Nev { get; set; } = string.Empty;
    public int Sorrend { get; set; } = 100;
}

/// <summary>
/// Összekapcsolt főkategória és a hozzá tartozó alkategóriák listája a 2 szintű választóhoz.
/// </summary>
public sealed class HalKategoriaCsoport
{
    public required HalKategoria Kategoria { get; init; }
    public List<HalAlkategoria> Alkategoriak { get; init; } = [];
}

/// <summary>
/// Beépített magyar halfaj-katalógus tartalék (offline vagy frissen indított adatbázis esetére).
/// </summary>
public static class HalKatalogusDefaults
{
    private static readonly (string Kategoria, int Sorrend, string[] Alkategoriak)[] AlapAdatok =
    [
        ("Ponty", 10, ["Tükörponty", "Pikkelyes ponty (Tőponty)", "Nyurgaponty", "Bőrponty", "Koi ponty"]),
        ("Amur", 20, ["Fehér amur", "Fekete amur"]),
        ("Kárász", 30, ["Ezüstkárász", "Aranykárász"]),
        ("Keszegfélék", 40, ["Dévérkeszeg", "Karikakeszeg", "Vörösszárnyú keszeg", "Bodorka", "Bagolykeszeg", "Lapos keszeg"]),
        ("Harcsa", 50, ["Lesőharcsa (Európai harcsa)", "Törpeharcsa", "Afrikai harcsa"]),
        ("Ragadozók", 60, ["Csuka", "Fogassüllő (Süllő)", "Kősüllő", "Balin", "Sügér", "Fekete sügér", "Domolykó"]),
        ("Busa", 70, ["Fehér busa", "Pettyes busa"]),
        ("Egyéb békés halak", 80, ["Compó", "Márna", "Paduc", "Jászkeszeg", "Tokhal (Kecsege / Szibériai tok)", "Pisztráng", "Angolna"])
    ];

    public static List<HalKategoriaCsoport> GetDefaultCsoportok()
    {
        var result = new List<HalKategoriaCsoport>(AlapAdatok.Length);
        var katIndex = 1;

        foreach (var (kategoriaNev, sorrend, alkategoriak) in AlapAdatok)
        {
            var katId = CreateDeterministicGuid(katIndex, 0);
            var csoport = new HalKategoriaCsoport
            {
                Kategoria = new HalKategoria
                {
                    Id = katId,
                    Nev = kategoriaNev,
                    Sorrend = sorrend
                },
                Alkategoriak = []
            };

            var alIndex = 1;
            foreach (var alNev in alkategoriak)
            {
                csoport.Alkategoriak.Add(new HalAlkategoria
                {
                    Id = CreateDeterministicGuid(katIndex, alIndex),
                    KategoriaId = katId,
                    Nev = alNev,
                    Sorrend = alIndex * 10
                });
                alIndex++;
            }

            result.Add(csoport);
            katIndex++;
        }

        return result;
    }

    public static List<HalKategoriaCsoport> BuildCsoportok(
        IReadOnlyList<HalKategoria> kategoriak,
        IReadOnlyList<HalAlkategoria> alkategoriak)
    {
        if (kategoriak.Count == 0)
        {
            return GetDefaultCsoportok();
        }

        var alMap = alkategoriak
            .GroupBy(a => a.KategoriaId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Sorrend).ThenBy(x => x.Nev, StringComparer.CurrentCultureIgnoreCase).ToList());

        return kategoriak
            .OrderBy(k => k.Sorrend)
            .ThenBy(k => k.Nev, StringComparer.CurrentCultureIgnoreCase)
            .Select(k => new HalKategoriaCsoport
            {
                Kategoria = new HalKategoria { Id = k.Id, Nev = k.Nev, Sorrend = k.Sorrend },
                Alkategoriak = alMap.TryGetValue(k.Id, out var lista)
                    ? lista.Select(a => new HalAlkategoria
                    {
                        Id = a.Id,
                        KategoriaId = a.KategoriaId,
                        Nev = a.Nev,
                        Sorrend = a.Sorrend
                    }).ToList()
                    : []
            })
            .ToList();
    }

    private static Guid CreateDeterministicGuid(int kategoriaIndex, int alkategoriaIndex) =>
        Guid.Parse($"70000000-0000-0000-{kategoriaIndex:0000}-{alkategoriaIndex:000000000000}");
}
