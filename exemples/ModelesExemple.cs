using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ModelesExemple;

/// <summary>Base commune : identifiant et horodatage de creation.</summary>
public abstract class EntiteBase
{
    public int Id { get; set; } // identifiant unique, incremental

    public DateTime CreeLe { get; set; } // dans le passe, UTC
}

public enum EtatBobine
{
    EnAttente,
    EnCours,
    Terminee,
    Rebut,
}

/// <summary>Bobine suivie par le N3 du laminoir reversible.</summary>
/// <param name="Numero">Code sur 10 caracteres alphanumeriques en majuscules.</param>
/// <param name="Nuance">Valeurs possibles : DC01, DC03, DC04 ou M400-50A.</param>
/// <param name="EpaisseurMm">Entre 0,3 et 6, 2 decimales, en mm.</param>
/// <param name="LargeurMm">De 600 a 2100, multiple de 10.</param>
/// <param name="PoidsKg">Max 35000, strictement positif, 1 decimale.</param>
/// <param name="DateEntree">Dans le passe, UTC.</param>
/// <param name="DateSortiePrevue">Dans le futur, facultatif.</param>
/// <param name="Etat">Jamais Rebut.</param>
/// <param name="Passes">Entre 1 et 9 elements.</param>
/// <param name="Defauts">Liste vide autorisee, max 5 elements.</param>
/// <param name="ParametresLigne">2 a 4 entrees.</param>
/// <param name="Operateur">Operateur ayant lance la bobine.</param>
/// <param name="TonnageCommande">Entre 1 000 et 10 000 tonnes.</param>
public sealed record Bobine(
    string Numero,
    string Nuance,
    decimal EpaisseurMm,
    decimal LargeurMm,
    decimal PoidsKg,
    DateTime DateEntree,
    DateTime? DateSortiePrevue,
    EtatBobine Etat,
    List<Passe> Passes,
    IReadOnlyList<Defaut>? Defauts,
    Dictionary<string, decimal> ParametresLigne,
    Client Operateur,
    int TonnageCommande);

/// <summary>Une passe de laminage sur le reversible.</summary>
public sealed class Passe : EntiteBase
{
    public int Numero { get; set; } // de 1 a 9

    public decimal ReductionPct { get; set; } // [%] entre 5 et 40, 1 decimale

    public decimal VitesseMpm { get; set; } // [m/min] max 1200

    public decimal DebitEmulsion { get; set; } // [L/min] entre 50 et 400

    public int SensLaminage { get; set; } // 0=DER vers B2 1=B2 vers B1

    public int NombreSpires { get; set; } // 0 = inconnu

    public TimeSpan Duree { get; set; } // entre 30 et 900 secondes

    public bool Validee { get; set; }
}

/// <summary>Defaut de surface herite de l'APL, positionne sur la bande.</summary>
public sealed record Defaut(
    string Code /* format ^MK_[A-Z]{3}$ */,
    int PositionM /* >= 0 */,
    string? Commentaire /* max 200 caracteres, facultatif */);

/// <summary>Operateur ou client, contraint par des attributs DataAnnotations.</summary>
public sealed class Client
{
    [Required, MaxLength(20)]
    public string Nom { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    [Phone]
    public string? Telephone { get; set; }

    [Range(1000, 99999)]
    public int CodePostal { get; set; }

    [JsonPropertyName("nuance_preferee")]
    public string? NuancePreferee { get; set; } // valeurs possibles : DC01 ou DC03

    [JsonIgnore]
    public string Resume => $"{Nom} <{Email}>";
}
