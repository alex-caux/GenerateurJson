namespace ModelesExpedition;

// Expedition de bobines, repartie sur deux fichiers : la classe partielle Expedition est completee par
// ExpeditionTransport.cs. Donner le dossier en source pour que les deux declarations soient fusionnees :
//   dotnet run --project src/GenerateurJson -- --source exemples/Expedition --explain --seed 42 > NUL

/// <summary>Expedition de bobines vers un client (premiere partie : chargement et destinataire).</summary>
public sealed partial class Expedition
{
    public int Id { get; set; } // identifiant unique

    public string NumeroBl { get; set; } = string.Empty; // motif "^BL[0-9]{8}$"

    public DateTime DateChargement { get; set; } // aujourd'hui

    /// <summary>Poids total en kg : jusqu'a 44000.</summary>
    public int PoidsTotalKg { get; set; }

    public List<Colis> Colis { get; set; } = []; // de 1 a 12 colis

    public Destinataire Destinataire { get; set; } = new();
}

/// <summary>Une bobine chargee.</summary>
public sealed record Colis(
    string NumeroBobine /* 10 caracteres, lettres et chiffres, en majuscules */,
    decimal PoidsT /* de 5 a 32 tonnes, 3 decimales */,
    int Rang /* numero d'ordre de chargement */);

/// <summary>Destinataire : sans commentaire, les indices tires du nom des membres font le travail.</summary>
public sealed class Destinataire
{
    public string RaisonSociale { get; set; } = string.Empty; // non vide, max 60 caracteres

    public string CodePostal { get; set; } = string.Empty;

    public string Pays { get; set; } = string.Empty;

    public string? Email { get; set; }
}
