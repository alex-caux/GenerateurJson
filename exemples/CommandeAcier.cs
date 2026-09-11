using System.ComponentModel.DataAnnotations;

namespace ModelesCommande;

// Commande client transmise par l'ERP au niveau 3 : textes et formats (listes de valeurs, codes, regex, e-mail,
// telephone, date ecrite en texte), valeur fixe, collections, dictionnaire, record positionnel documente par <param>.
//   dotnet run --project src/GenerateurJson -- --source exemples/CommandeAcier.cs --explain --seed 42 > NUL

/// <summary>Commande d'acier plat saisie dans l'ERP.</summary>
public sealed class CommandeAcier
{
    [Key]
    public int Id { get; set; }

    public required string NoCommande { get; init; } // motif "^CMD-202[4-7]-[0-9]{5}$"

    /// <summary>Date de saisie dans l'ERP : depuis 2024-01-01, pas dans le futur.</summary>
    public DateOnly DateCommande { get; set; }

    // a venir, facultative
    public DateTime? LivraisonSouhaitee { get; set; }

    public string DateExportErp { get; set; } = string.Empty; // format dd/MM/yyyy HH:mm

    public string CodeClient { get; set; } = string.Empty; // exactement 8 caracteres, lettres et chiffres, en majuscules

    public string? ReferenceClient { get; set; } // au plus 35 caracteres, optionnel

    // Statut : BROUILLON, VALIDEE, EN_PRODUCTION ou EXPEDIEE
    public string Statut { get; set; } = "BROUILLON";

    public char Priorite { get; set; } = 'N'; // N=normale U=urgente

    public string Incoterm { get; set; } = string.Empty; // EXW|FCA|CPT|DAP|DDP

    /// <summary>Toujours "EUR".</summary>
    public string Devise { get; set; } = "EUR";

    /// <summary>Montant HT entre 500 et 250000, 2 decimales.</summary>
    public decimal MontantHt { get; set; }

    public decimal RemisePct { get; set; } // [%] au plus 15, par pas de 0,5

    public int DelaiPaiement { get; set; } // [jours] 30, 45 ou 60

    public AdresseLivraison Livraison { get; set; } = new();

    /// <summary>Au moins une ligne, 5 lignes max.</summary>
    public List<LigneCommande> Lignes { get; set; } = [];

    // sans doublon, 1 a 3 elements; chaque element : parmi 2.1, 2.2, 3.1 ou 3.2
    public List<string> Certificats { get; set; } = [];

    /// <summary>Caracteristiques libres : 2 a 4 entrees, chaque valeur au plus 30 caracteres.</summary>
    public Dictionary<string, string> Caracteristiques { get; set; } = [];
}

/// <summary>Adresse de livraison : formats de texte usuels et indices tires du nom des membres.</summary>
public sealed class AdresseLivraison
{
    public string Societe { get; set; } = string.Empty; // non vide, max 60 caracteres

    public string Rue { get; set; } = string.Empty; // max 80 caracteres

    public string? Complement { get; set; } // facultatif, max 80 caracteres

    public string CodePostal { get; set; } = string.Empty;

    public string Ville { get; set; } = string.Empty; // en majuscules, 2 a 40 caracteres

    public string Pays { get; set; } = "FR"; // valeurs possibles : FR, BE, DE, ES, IT ou LU

    public string? Telephone { get; set; }

    public string? EmailMagasin { get; set; } // courriel du magasinier, facultatif

    public double Latitude { get; set; } // entre 41 et 51,5, 5 decimales

    public double Longitude { get; set; } // entre -5,2 et 9,6, 5 decimales
}

/// <summary>Ligne de commande.</summary>
/// <param name="NumeroLigne">Numero d'ordre de la ligne dans la commande.</param>
/// <param name="Nuance">Parmi S235JR, S355J2, DC01, DC04 et DD11.</param>
/// <param name="EpaisseurMm">]0;25], au centieme.</param>
/// <param name="LargeurMm">De 1000 a 2000 mm, par pas de 50.</param>
/// <param name="QuantiteBobines">Au moins 1, au plus 40.</param>
/// <param name="PoidsCibleT">Poids cible d'une bobine, de 5 a 30 tonnes, 1 decimale.</param>
/// <param name="DateLivraison">A venir.</param>
/// <param name="Revetement">Z=zingue AZ=aluminium-zinc N=nu</param>
public sealed record LigneCommande(
    int NumeroLigne,
    string Nuance,
    decimal EpaisseurMm,
    decimal LargeurMm,
    int QuantiteBobines,
    decimal PoidsCibleT,
    DateOnly DateLivraison,
    string Revetement);
