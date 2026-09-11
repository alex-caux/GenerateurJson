using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ModelesMaintenance;

// Ordre de travail de GMAO : attributs DataAnnotations et System.Text.Json (prioritaires sur les commentaires),
// modificateur required, heritage d'une base abstraite, interface a implementation unique, type imbrique, champs
// publics, membres ignores. AffectationEquipe n'etant referencee que via son interface, c'est aussi une racine
// possible : d'ou le --type.
//   dotnet run --project src/GenerateurJson -- --source exemples/Maintenance.cs --type OrdreTravail --explain --seed 42 > NUL

/// <summary>Priorite d'intervention : Urgente vaut 1, les suivantes 2, 3 et 4.</summary>
public enum Priorite
{
    Urgente = 1,
    Haute,
    Normale,
    Opportuniste,
}

public enum Metier
{
    Electricite,
    Mecanique,
    Hydraulique,
    Automatisme,
    Soudure,
}

/// <summary>Base abstraite : jamais generee seule, ses membres sont places en tete de chaque classe derivee.</summary>
public abstract class ObjetTrace
{
    [Key]
    public int Id { get; set; }

    [JsonPropertyName("cree_par")]
    public string CreePar { get; set; } = string.Empty; // trigramme : 3 lettres, en majuscules, lettres uniquement

    public DateTimeOffset ModifieLe { get; set; } // UTC, dans le passe
}

/// <summary>Ordre de travail de maintenance.</summary>
public sealed class OrdreTravail : ObjetTrace
{
    public const int VersionSchema = 3;

    public static int Compteur;

    [RegularExpression(@"^OT-[0-9]{6}$")]
    public required string Numero { get; init; }

    [StringLength(60, MinimumLength = 5)]
    public required string Titre { get; init; } // entre 10 et 20 caracteres

    [AllowedValues("LAM", "DEC", "REC", "GAL")]
    public string Atelier { get; set; } = "LAM";

    [DeniedValues(Priorite.Opportuniste)]
    public Priorite Priorite { get; set; } // 1=urgente 2=haute 3=normale 4=opportuniste

    [JsonRequired]
    public string Demandeur { get; set; } = string.Empty;

    public Equipement Equipement { get; set; } = new();

    public DateTime DateDemande { get; set; } // dans le passe, depuis 2025

    // apres le 01/01/2026, avant le 31/12/2026 18:00
    public DateTime DebutPrevu { get; set; }

    public DateTime Echeance { get; } // a venir, date seule

    [Range(0, 100)]
    public int UsurePct { get; set; } // entre 0 et 50

    public TimeSpan DureeEstimee { get; set; } // de 15 a 480 minutes

    [DefaultValue(true)]
    public bool ArretLigne { get; set; }

    public List<Metier> Metiers { get; set; } = []; // sans doublon, 1 a 3 elements

    [Length(1, 5)]
    public List<string> Consignations { get; set; } = []; // chaque element : 4 a 12 caracteres, en majuscules, sans espace

    public IAffectation Affectation { get; set; } = null!;

    /// <summary>Etapes de la gamme, dans l'ordre : 2 a 6 elements.</summary>
    public List<Etape> Etapes { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Cloture { get; set; }

    [Obsolete("remplace par Atelier")]
    public string? AncienSecteur { get; set; }

    public Uri? Documentation { get; set; }

    // --- ignores : champ prive, [JsonIgnore], proprietes calculees (et plus haut const et static)

    private string _brouillon = string.Empty;

    [JsonIgnore]
    public string? CacheInterne { get; set; }

    public string Resume => $"{Numero} {Titre}";

    public bool EnRetard
    {
        get { return Echeance < DateTime.Today; }
    }

    /// <summary>Type imbrique : nom complet ModelesMaintenance.OrdreTravail.Etape (--type OrdreTravail.Etape).</summary>
    public sealed class Etape
    {
        public int Rang { get; set; } // numero d'ordre

        public string Libelle { get; set; } = string.Empty; // max 80 caracteres

        public Metier Metier { get; set; }

        public bool Consignation { get; set; } // par defaut false

        public decimal? DureeHeures { get; set; } // de 0,25 a 8, par pas de 0,25, facultatif
    }
}

/// <summary>Equipement concerne : donnees portees par des champs publics, herite lui aussi d'ObjetTrace.</summary>
public sealed class Equipement : ObjetTrace
{
    [RegularExpression(@"^EQ-[A-Z]{3}-[0-9]{4}$")]
    public string Repere = string.Empty;

    public string Designation = string.Empty; // entre 5 et 60 caracteres

    public long HeuresMarche; // en heures, de 0 a 120000

    [Range(typeof(DateTime), "2000-01-01", "2026-12-31")]
    public DateTime MiseEnService;

    [Url]
    public string? LienGed;
}

/// <summary>Interface : un membre de ce type est genere avec son implementation concrete, unique dans les sources.</summary>
public interface IAffectation
{
    string Equipe { get; }
}

public sealed class AffectationEquipe : IAffectation
{
    public string Equipe { get; set; } = "A"; // A, B, C, D ou E

    public HashSet<string> Matricules { get; set; } = []; // 1 a 4 elements; chaque element : motif "^M[0-9]{5}$"
}
