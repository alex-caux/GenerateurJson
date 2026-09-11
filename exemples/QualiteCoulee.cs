namespace ModelesQualite;

// Coulee d'acierie et certificat qualite : plages numeriques, intervalles, decimales, pas, unites, pourcentages,
// enums (valeurs explicites, [Flags]), dictionnaire a cles enum, DateOnly / TimeOnly / TimeSpan, Guid, byte[],
// record struct positionnel, commentaires /* */ et <remarks>.
//   dotnet run --project src/GenerateurJson -- --source exemples/QualiteCoulee.cs --explain --seed 42 > NUL

/// <summary>Elements doses par le spectrometre : cles du dictionnaire des teneurs.</summary>
public enum ElementChimique
{
    C,
    Mn,
    Si,
    P,
    S,
    Al,
    Nb,
    Ti,
}

/// <summary>Valeurs explicites : avec --enum-as-int, le JSON porte 10, 20, 30 ou 99.</summary>
public enum StatutLot
{
    Conforme = 10,
    Deroge = 20,
    Bloque = 30,
    Rebute = 99,
}

/// <summary>Enum [Flags] : un membre non nul est tire.</summary>
[Flags]
public enum Anomalie
{
    Aucune = 0,
    Inclusion = 1,
    Fissure = 1 << 1,
    Segregation = 1 << 2,
    HorsTolerance = 1 << 3,
}

/// <summary>Coulee d'acierie et son certificat qualite.</summary>
public sealed class Coulee
{
    public string NumeroCoulee { get; set; } = string.Empty; // 6 chiffres

    // DK=Dunkerque, FOS=Fos-sur-Mer
    public string Acierie { get; set; } = "DK";

    /// <summary>Entre 2020 et 2026.</summary>
    public DateOnly DateCoulee { get; set; }

    public TimeOnly HeureDebut { get; set; }

    public TimeSpan DureeCoulee { get; set; } // de 40 a 70 minutes

    public decimal TemperatureLiquide { get; set; } // en degres, entre 1520 et 1650, sans decimale

    public decimal PoidsNetT { get; set; } // strictement positif, max 330, precision 0,5

    public int NombreBrames { get; set; } // pair, de 2 a 12

    /// <summary>Taux de chute de la coulee, en pourcentage, au plus 12, au dixieme.</summary>
    public decimal TauxChute { get; set; }

    /*
     * Teneurs en % massique, de 4 a 8 entrees ;
     * chaque valeur : entre 0 et 1,8, 3 decimales.
     */
    public Dictionary<ElementChimique, decimal> Analyse { get; set; } = [];

    /// <summary>Entre 1 et 3 essais par coulee.</summary>
    public List<EssaiTraction> Essais { get; set; } = [];

    public StatutLot Statut { get; set; } // valeurs possibles : Conforme, Deroge ou Bloque

    public Anomalie Anomalies { get; set; }

    /// <summary>Trigramme du chef de poste.</summary>
    /// <remarks>En majuscules, lettres uniquement, exactement 3 caracteres.</remarks>
    public string Operateur { get; set; } = string.Empty;

    public Guid IdentifiantMes { get; set; }

    public byte[]? CertificatPdf { get; set; } // facultatif
}

/// <summary>Essai de traction sur eprouvette.</summary>
public readonly record struct EssaiTraction(
    string Sens, // L=long T=travers
    int ReMpa, // elasticite en MPa, de 235 a 460
    int RmMpa, // resistance en MPa, de 360 a 630
    decimal AllongementPct); // entre 18 et 45 %, 1 decimale
