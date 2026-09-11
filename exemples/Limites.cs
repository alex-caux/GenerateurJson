using System.Text.Json;

namespace ModelesLimites;

// Chaque membre illustre une limite documentee dans le README : lancer avec --explain pour voir les avertissements
// (lignes « ! » du rapport et « avertissement : » sur stderr) et les valeurs generees a null.
// Les lignes « // --- » sont des bannieres : le lecteur de commentaires les saute, elles ne deviennent pas des contraintes.
//   dotnet run --project src/GenerateurJson -- --source exemples/Limites.cs --type CasLimites --explain > NUL

/// <summary>Ce que le moteur de regles ne comprend pas, ou ne sait pas generer.</summary>
public sealed class CasLimites
{
    // --- separateur de milliers : « 1 000 » est lu comme deux nombres, la phrase est signalee non reconnue
    public int TonnageAnnuel { get; set; } // entre 1 000 et 50 000 tonnes

    // --- constante referencee : pas de semantique Roslyn, TailleLot n'est pas resolue
    public int QuantiteMaxi { get; set; } // max TailleLot

    // --- contraintes incompatibles : la premiere plage est conservee
    public int NombreEssais { get; set; } // entre 10 et 20. Max 5.

    // --- valeur inconnue de l'enum : ignoree, avec un avertissement
    public EtatLigne Etat { get; set; } // valeurs possibles : Marche, Arret ou Inconnu

    // --- plage numerique sur une date : ni annee ni date, rien n'est retenu
    public DateOnly Livraison { get; set; } // entre 5 et 10

    // --- obligatoire et optionnel a la fois : obligatoire retenu
    public string? Observation { get; set; } // obligatoire. Facultatif.

    // --- regex hors du sous-ensemble supporte (lookahead) : texte libre a la generation
    public string CodeAcces { get; set; } = string.Empty; // regex ^(?=.*[0-9])[A-Z0-9]{8}$

    // --- types non generes : null, avec un avertissement a l'analyse
    public object? Charge { get; set; }

    public (int Min, int Max) Bornes { get; set; }

    public int[,] Matrice { get; set; } = new int[0, 0];

    public JsonElement Brut { get; set; }

    public Adresse? Siege { get; set; }

    public Enveloppe<string>? Paquet { get; set; }

    // --- cle de dictionnaire non supportee (DateTime) : dictionnaire vide
    public Dictionary<DateTime, int> ParJour { get; set; } = [];

    // --- plusieurs implementations concretes : la premiere declaree est utilisee
    public Mesure Derniere { get; set; } = null!;

    // --- type recursif : coupe a --max-depth (3 par defaut), puis null ou []
    public Noeud Arborescence { get; set; } = new();
}

public enum EtatLigne
{
    Marche,
    Arret,
    Maintenance,
}

/// <summary>Classe generique : jamais generee (avertissement a l'analyse).</summary>
public sealed class Enveloppe<T>
{
    public T? Contenu { get; set; }
}

/// <summary>Deux implementations concretes : le generateur prend la premiere declaree et le signale.</summary>
public abstract class Mesure
{
    public DateTime Horodatage { get; set; } // dans le passe
}

public sealed class MesureTemperature : Mesure
{
    public decimal Valeur { get; set; } // en degres, entre 20 et 1300, 1 decimale
}

public sealed class MesurePression : Mesure
{
    public decimal Valeur { get; set; } // en bar, de 0 a 250, 2 decimales
}

/// <summary>Arborescence des lignes et equipements : type recursif.</summary>
public sealed class Noeud
{
    public string Nom { get; set; } = string.Empty; // max 30 caracteres

    public List<Noeud> Enfants { get; set; } = []; // max 2 elements
}
