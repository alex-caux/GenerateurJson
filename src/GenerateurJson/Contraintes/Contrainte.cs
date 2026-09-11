using System.Globalization;

namespace GenerateurJson;

/// <summary>Provenance d'une contrainte, dans l'ordre de priorite (la plus petite valeur gagne).</summary>
public enum OrigineContrainte
{
    Attribut = 0,
    Modificateur = 1,
    Commentaire = 2,
    TypeCSharp = 3,
    Nom = 4,
}

/// <summary>Une contrainte de valeur interpretee, quelle que soit sa provenance.</summary>
public abstract record Contrainte
{
    public OrigineContrainte Origine { get; init; } = OrigineContrainte.Commentaire;

    /// <summary>Detail de la provenance : nom de l'attribut, source du commentaire (summary, fin de ligne, llm...).</summary>
    public string Source { get; init; } = "commentaire";

    public abstract string Decrire();

    public string Etiquette => Origine switch
    {
        OrigineContrainte.Attribut => $"[{Source}]",
        OrigineContrainte.Modificateur => "required",
        OrigineContrainte.Commentaire => Source,
        OrigineContrainte.TypeCSharp => "type C#",
        OrigineContrainte.Nom => "nom",
        _ => Source,
    };

    protected static string FormaterNombre(decimal valeur) => valeur.ToString("0.############", CultureInfo.InvariantCulture);
}

public sealed record Plage(decimal? Min, decimal? Max, bool MinExclusif = false, bool MaxExclusif = false) : Contrainte
{
    public override string Decrire()
    {
        var parties = new List<string>(2);
        if (Min is not null)
        {
            parties.Add((MinExclusif ? "> " : ">= ") + FormaterNombre(Min.Value));
        }

        if (Max is not null)
        {
            parties.Add((MaxExclusif ? "< " : "<= ") + FormaterNombre(Max.Value));
        }

        return parties.Count == 0 ? "plage vide" : "valeur " + string.Join(" et ", parties);
    }
}

public sealed record LongueurTexte(int? Min, int? Max) : Contrainte
{
    public override string Decrire() => Borne("longueur", Min, Max);

    internal static string Borne(string nom, int? min, int? max)
    {
        if (min is not null && max is not null && min == max)
        {
            return $"{nom} = {min}";
        }

        var parties = new List<string>(2);
        if (min is not null)
        {
            parties.Add(">= " + min);
        }

        if (max is not null)
        {
            parties.Add("<= " + max);
        }

        return $"{nom} {string.Join(" et ", parties)}";
    }
}

public sealed record TailleCollection(int? Min, int? Max) : Contrainte
{
    public override string Decrire() => LongueurTexte.Borne("taille", Min, Max);
}

public sealed record ValeursAutorisees(IReadOnlyList<string> Valeurs) : Contrainte
{
    public override string Decrire() => "valeurs autorisees : " + string.Join(", ", Valeurs);
}

public sealed record ValeursExclues(IReadOnlyList<string> Valeurs) : Contrainte
{
    public override string Decrire() => "valeurs exclues : " + string.Join(", ", Valeurs);
}

public enum GenreFormat
{
    Email,
    Url,
    Guid,
    Telephone,
    CodePostal,
    Majuscules,
    Minuscules,
    ChiffresUniquement,
    Lettres,
    Alphanumerique,
    SansEspaces,
    Iso8601,
    Utc,
    DateSeule,
}

public sealed record Format(GenreFormat Genre) : Contrainte
{
    public override string Decrire() => "format " + Genre.ToString().ToLowerInvariant();
}

public sealed record FormatDate(string Modele) : Contrainte
{
    public override string Decrire() => "format de date " + Modele;
}

public sealed record Motif(string Regex) : Contrainte
{
    public override string Decrire() => "motif regex " + Regex;
}

public sealed record Presence(bool Obligatoire) : Contrainte
{
    public override string Decrire() => Obligatoire ? "obligatoire" : "optionnel";
}

public sealed record Decimales(int Nombre) : Contrainte
{
    public override string Decrire() => Nombre == 0 ? "sans decimale" : $"{Nombre} decimale(s)";
}

public sealed record Multiple(decimal Pas) : Contrainte
{
    public override string Decrire() => "multiple de " + FormaterNombre(Pas);
}

public sealed record Parite(bool Pair) : Contrainte
{
    public override string Decrire() => Pair ? "pair" : "impair";
}

public sealed record ValeurFixe(string Texte, bool ParDefaut) : Contrainte
{
    public override string Decrire() => (ParDefaut ? "valeur par defaut " : "valeur fixe ") + Texte;
}

public sealed record Unicite(bool Sequentiel) : Contrainte
{
    public override string Decrire() => Sequentiel ? "unique, sequentiel" : "unique";
}

public enum GenreTemporalite
{
    Passe,
    Futur,
    Aujourdhui,
}

public sealed record Temporalite(GenreTemporalite Genre) : Contrainte
{
    public override string Decrire() => Genre switch
    {
        GenreTemporalite.Passe => "date dans le passe",
        GenreTemporalite.Futur => "date dans le futur",
        _ => "date du jour",
    };
}

public sealed record PlageDates(DateTime? Min, DateTime? Max) : Contrainte
{
    public override string Decrire()
    {
        var parties = new List<string>(2);
        if (Min is not null)
        {
            parties.Add(">= " + Min.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (Max is not null)
        {
            parties.Add("<= " + Max.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return "date " + string.Join(" et ", parties);
    }
}

public sealed record ElementsDistincts : Contrainte
{
    public override string Decrire() => "elements distincts";
}

public sealed record Unite(string Symbole) : Contrainte
{
    public override string Decrire() => "unite : " + Symbole;
}

public sealed record Note(string Texte) : Contrainte
{
    public override string Decrire() => "note : " + Texte;
}

/// <summary>
/// Vue fusionnee des contraintes d'un membre, une par famille, avec les regles de priorite entre origines et de
/// fusion au sein d'une meme origine. <see cref="Element"/> porte les contraintes des elements d'une collection.
/// </summary>
public sealed class JeuContraintes
{
    private JeuContraintes? _element;

    public Plage? Plage { get; private set; }

    public LongueurTexte? Longueur { get; private set; }

    public TailleCollection? Taille { get; private set; }

    public ValeursAutorisees? Valeurs { get; private set; }

    public ValeursExclues? Exclusions { get; private set; }

    public HashSet<GenreFormat> Formats { get; } = [];

    public FormatDate? FormatDate { get; private set; }

    public Motif? Motif { get; private set; }

    public Presence? Presence { get; private set; }

    public Decimales? Decimales { get; private set; }

    public Multiple? Multiple { get; private set; }

    public Parite? Parite { get; private set; }

    public ValeurFixe? ValeurFixe { get; private set; }

    public Unicite? Unicite { get; private set; }

    public Temporalite? Temporalite { get; private set; }

    public PlageDates? PlageDates { get; private set; }

    public bool ElementsDistincts { get; private set; }

    public Unite? Unite { get; private set; }

    /// <summary>Toutes les contraintes recues, dans l'ordre, pour le rapport.</summary>
    public List<Contrainte> Toutes { get; } = [];

    public List<string> Notes { get; } = [];

    public List<string> Avertissements { get; } = [];

    /// <summary>Contraintes des elements (collection) ou des valeurs (dictionnaire).</summary>
    public JeuContraintes Element => _element ??= new JeuContraintes();

    public bool AElement => _element is not null;

    /// <summary>Une contrainte autre que la presence porte sur la valeur : la valeur par defaut n'est alors pas utilisee.</summary>
    public bool AContrainteDeValeur =>
        Plage is not null || Longueur is not null || Valeurs is not null || Formats.Count > 0 || FormatDate is not null ||
        Motif is not null || Decimales is not null || Multiple is not null || Parite is not null || Unicite is not null ||
        Temporalite is not null || PlageDates is not null;

    /// <summary>Les contraintes retenues apres fusion, une par famille, dans un ordre stable (ce que le generateur applique).</summary>
    public IEnumerable<Contrainte> Effectives()
    {
        if (Presence is not null)
        {
            yield return Presence;
        }

        if (Plage is not null)
        {
            yield return Plage;
        }

        if (Longueur is not null)
        {
            yield return Longueur;
        }

        if (Taille is not null)
        {
            yield return Taille;
        }

        if (Valeurs is not null)
        {
            yield return Valeurs;
        }

        if (Exclusions is not null)
        {
            yield return Exclusions;
        }

        foreach (var format in Toutes.OfType<Format>().DistinctBy(f => f.Genre))
        {
            yield return format;
        }

        if (FormatDate is not null)
        {
            yield return FormatDate;
        }

        if (Motif is not null)
        {
            yield return Motif;
        }

        if (Decimales is not null)
        {
            yield return Decimales;
        }

        if (Multiple is not null)
        {
            yield return Multiple;
        }

        if (Parite is not null)
        {
            yield return Parite;
        }

        if (ValeurFixe is not null)
        {
            yield return ValeurFixe;
        }

        if (Unicite is not null)
        {
            yield return Unicite;
        }

        if (Temporalite is not null)
        {
            yield return Temporalite;
        }

        if (PlageDates is not null)
        {
            yield return PlageDates;
        }

        if (ElementsDistincts && Toutes.OfType<ElementsDistincts>().FirstOrDefault() is { } distincts)
        {
            yield return distincts;
        }

        if (Unite is not null)
        {
            yield return Unite;
        }
    }

    public void Ajouter(Contrainte contrainte)
    {
        Toutes.Add(contrainte);
        switch (contrainte)
        {
            case Plage plage:
                Plage = Fusionner(Plage, plage, Intersecter);
                break;
            case LongueurTexte longueur:
                Longueur = Fusionner(Longueur, longueur, (a, b) => Intersecter(a.Min, a.Max, b.Min, b.Max) is var (min, max) && Valide(min, max) ? new LongueurTexte(min, max) { Origine = a.Origine, Source = a.Source } : null);
                break;
            case TailleCollection taille:
                Taille = Fusionner(Taille, taille, (a, b) => Intersecter(a.Min, a.Max, b.Min, b.Max) is var (min, max) && Valide(min, max) ? new TailleCollection(min, max) { Origine = a.Origine, Source = a.Source } : null);
                break;
            case ValeursAutorisees valeurs:
                Valeurs = Fusionner(Valeurs, valeurs, (a, b) =>
                {
                    var communes = a.Valeurs.Where(v => b.Valeurs.Contains(v, StringComparer.OrdinalIgnoreCase)).ToList();
                    return communes.Count == 0 ? null : new ValeursAutorisees(communes) { Origine = a.Origine, Source = a.Source };
                });
                break;
            case ValeursExclues exclues:
                Exclusions = Exclusions is null
                    ? exclues
                    : new ValeursExclues(Exclusions.Valeurs.Union(exclues.Valeurs, StringComparer.OrdinalIgnoreCase).ToList()) { Origine = Exclusions.Origine, Source = Exclusions.Source };
                break;
            case Format format:
                Formats.Add(format.Genre);
                break;
            case FormatDate formatDate:
                FormatDate = Fusionner(FormatDate, formatDate, DerniereGagne);
                break;
            case Motif motif:
                Motif = Fusionner(Motif, motif, DerniereGagne);
                break;
            case Presence presence:
                AjouterPresence(presence);
                break;
            case Decimales decimales:
                Decimales = Fusionner(Decimales, decimales, DerniereGagne);
                break;
            case Multiple multiple:
                Multiple = Fusionner(Multiple, multiple, DerniereGagne);
                break;
            case Parite parite:
                Parite = Fusionner(Parite, parite, DerniereGagne);
                break;
            case ValeurFixe fixe:
                AjouterValeurFixe(fixe);
                break;
            case Unicite unicite:
                Unicite = Unicite is null || (unicite.Sequentiel && !Unicite.Sequentiel) ? unicite : Unicite;
                break;
            case Temporalite temporalite:
                Temporalite = Fusionner(Temporalite, temporalite, DerniereGagne);
                break;
            case PlageDates plageDates:
                PlageDates = Fusionner(PlageDates, plageDates, (a, b) =>
                {
                    var min = a.Min is null ? b.Min : b.Min is null ? a.Min : (a.Min > b.Min ? a.Min : b.Min);
                    var max = a.Max is null ? b.Max : b.Max is null ? a.Max : (a.Max < b.Max ? a.Max : b.Max);
                    return min is not null && max is not null && min > max ? null : new PlageDates(min, max) { Origine = a.Origine, Source = a.Source };
                });
                break;
            case ElementsDistincts _:
                ElementsDistincts = true;
                break;
            case Unite unite:
                Unite = unite;
                break;
            case Note note:
                Notes.Add(note.Texte);
                break;
        }
    }

    private T? Fusionner<T>(T? actuelle, T nouvelle, Func<T, T, T?> memeOrigine) where T : Contrainte
    {
        if (actuelle is null)
        {
            return nouvelle;
        }

        if (nouvelle.Origine < actuelle.Origine)
        {
            if (actuelle.Origine <= OrigineContrainte.Commentaire)
            {
                Notes.Add($"{actuelle.Decrire()} ({actuelle.Etiquette}) remplace par {nouvelle.Decrire()} ({nouvelle.Etiquette})");
            }

            return nouvelle;
        }

        if (nouvelle.Origine > actuelle.Origine)
        {
            if (nouvelle.Origine <= OrigineContrainte.Commentaire)
            {
                Notes.Add($"{nouvelle.Decrire()} ({nouvelle.Etiquette}) ignore : {actuelle.Etiquette} prioritaire");
            }

            return actuelle;
        }

        var fusion = memeOrigine(actuelle, nouvelle);
        if (fusion is null)
        {
            Avertissements.Add($"contraintes incompatibles : « {actuelle.Decrire()} » et « {nouvelle.Decrire()} » ; la premiere est conservee");
            return actuelle;
        }

        return fusion;
    }

    private T DerniereGagne<T>(T actuelle, T nouvelle) where T : Contrainte
    {
        if (actuelle.Decrire() != nouvelle.Decrire())
        {
            Avertissements.Add($"« {actuelle.Decrire()} » remplace par « {nouvelle.Decrire()} »");
        }

        return nouvelle;
    }

    private void AjouterPresence(Presence presence)
    {
        if (Presence is null || presence.Origine < Presence.Origine)
        {
            Presence = presence;
            return;
        }

        if (presence.Origine > Presence.Origine || presence.Obligatoire == Presence.Obligatoire)
        {
            return;
        }

        Avertissements.Add("obligatoire et optionnel a la fois : obligatoire retenu");
        if (presence.Obligatoire)
        {
            Presence = presence;
        }
    }

    private void AjouterValeurFixe(ValeurFixe fixe)
    {
        if (ValeurFixe is null || (ValeurFixe.ParDefaut && !fixe.ParDefaut))
        {
            ValeurFixe = fixe;
            return;
        }

        if (fixe.ParDefaut && !ValeurFixe.ParDefaut)
        {
            return;
        }

        if (!string.Equals(ValeurFixe.Texte, fixe.Texte, StringComparison.Ordinal))
        {
            Avertissements.Add($"« {ValeurFixe.Decrire()} » remplace par « {fixe.Decrire()} »");
        }

        ValeurFixe = fixe;
    }

    private static Plage? Intersecter(Plage a, Plage b)
    {
        decimal? min;
        bool minExclusif;
        if (a.Min is null || (b.Min is not null && b.Min > a.Min))
        {
            (min, minExclusif) = (b.Min, b.MinExclusif);
        }
        else if (b.Min is null || a.Min > b.Min)
        {
            (min, minExclusif) = (a.Min, a.MinExclusif);
        }
        else
        {
            (min, minExclusif) = (a.Min, a.MinExclusif || b.MinExclusif);
        }

        decimal? max;
        bool maxExclusif;
        if (a.Max is null || (b.Max is not null && b.Max < a.Max))
        {
            (max, maxExclusif) = (b.Max, b.MaxExclusif);
        }
        else if (b.Max is null || a.Max < b.Max)
        {
            (max, maxExclusif) = (a.Max, a.MaxExclusif);
        }
        else
        {
            (max, maxExclusif) = (a.Max, a.MaxExclusif || b.MaxExclusif);
        }

        if (min is not null && max is not null && (min > max || (min == max && (minExclusif || maxExclusif))))
        {
            return null;
        }

        return new Plage(min, max, minExclusif, maxExclusif) { Origine = a.Origine, Source = a.Source };
    }

    private static (int? Min, int? Max) Intersecter(int? aMin, int? aMax, int? bMin, int? bMax)
    {
        var min = aMin is null ? bMin : bMin is null ? aMin : Math.Max(aMin.Value, bMin.Value);
        var max = aMax is null ? bMax : bMax is null ? aMax : Math.Min(aMax.Value, bMax.Value);
        return (min, max);
    }

    private static bool Valide(int? min, int? max) => min is null || max is null || min <= max;
}
