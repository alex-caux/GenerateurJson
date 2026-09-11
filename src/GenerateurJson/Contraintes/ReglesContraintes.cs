using System.Globalization;
using System.Text.RegularExpressions;

namespace GenerateurJson;

public enum PhaseRegle
{
    /// <summary>Litteraux d'abord : regex, formats de date, listes de valeurs, correspondances 0=x 1=y, unites entre crochets.</summary>
    Litteraux,

    /// <summary>Puis les mots-cles : plages, longueurs, presence, formats, dates, multiples...</summary>
    MotsCles,
}

public enum CibleContrainte
{
    Auto,
    Membre,
    Element,
}

public sealed record ResultatRegle(Contrainte Contrainte, CibleContrainte Cible = CibleContrainte.Auto);

/// <summary>Une phrase de commentaire en cours d'interpretation, avec son texte original et son texte de travail (normalise, masque, consomme).</summary>
public sealed class ContexteRegle
{
    public required string Original { get; init; }

    public required string Travail { get; set; }

    public required string Source { get; init; }

    public required ReferenceType Type { get; init; }

    public required IReadOnlyList<Litteral> Litteraux { get; init; }

    public required List<string> Avertissements { get; init; }

    public GenreValeur GenreCible => Type.GenreCible;

    public ReferenceType TypeCible => Type.TypeCible;

    public CibleContrainte CibleValeur => Type.EstConteneur ? CibleContrainte.Element : CibleContrainte.Membre;

    public bool EstDate => GenreCible is GenreValeur.DateHeure or GenreValeur.DateSeule;

    public bool EstNumerique => GenreCible is GenreValeur.Entier or GenreValeur.Reel;

    public bool EstTexte => GenreCible is GenreValeur.Texte or GenreValeur.Caractere;

    public string Texte(Group groupe) => Original.Substring(groupe.Index, groupe.Length);

    public Litteral? LitteralA(Group groupe) =>
        groupe.Success ? Litteraux.FirstOrDefault(l => l.Debut <= groupe.Index && groupe.Index < l.Debut + l.Longueur) : null;

    public string TexteInterne(Group groupe) => LitteralA(groupe)?.Texte ?? Texte(groupe);

    public decimal? Nombre(Group groupe) => groupe.Success ? NormaliseurTexte.Nombre(Texte(groupe)) : null;

    public int? Entier(Group groupe) => NormaliseurTexte.Entier(Nombre(groupe));

    public void Avertir(string message) => Avertissements.Add($"{message} ({Source}) : « {Original.Trim()} »");
}

public sealed record Regle(string Nom, PhaseRegle Phase, Regex Motif, Func<Match, ContexteRegle, IEnumerable<ResultatRegle>> Construire);

/// <summary>
/// La grammaire des commentaires, sous forme de table de regles. Les regex s'appliquent au texte normalise (sans
/// accents, litteraux masques par des caracteres U+0001) ; les valeurs sont relues dans le texte original.
/// Chaque regle decide selon le type du membre : « entre 3 et 10 » est une plage sur un nombre, une longueur sur un texte.
/// </summary>
public static class ReglesContraintes
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Caractere de masque des litteraux (U+0001), construit sans echappement dans la source.
    private static readonly string M = ((char)1).ToString();

    private const string N = @"[-+]?\d+(?:[.,]\d+)?";
    private const string Unites = @"(?<u>caracteres?|chars?|characters?|lettres?|letters?|symboles?|signes?|car\.|elements?|items?|entrees?|entries|valeurs?|values?|lignes?|lines?|occurrences?|objets?|objects?|enregistrements?|records?|membres?|members?|chiffres?|digits?)";
    private const string SuffixeUnite = @"(?:\s*" + Unites + @"\b)?";
    private const string MotsMax = @"(?:max(?:imum|imal|imale|i)?\.?|au plus|au maximum|pas plus de|plafond(?:ne)?(?: a| de)?|borne (?:haute|sup(?:erieure)?)|limite (?:haute|sup(?:erieure)?)|jusqu'a|at most|no more than|not more than|up to|upper bound|ceiling|capped? at|limitee? a|limited to|n'excede pas|n'excedant pas|ne depasse pas|ne peut (?:pas )?depasser|sans depasser|must not exceed|does not exceed|not exceeding|tronquee? a|truncated to)";
    private const string MotsMin = @"(?:min(?:imum|imal|imale|i)?\.?|au moins|au minimum|pas moins de|plancher(?: a| de)?|borne (?:basse|inf(?:erieure)?)|limite (?:basse|inf(?:erieure)?)|a partir de|at least|not less than|no less than|lower bound|floor)";
    private const string Separateur = @"(?:\s*(?::|=|de|of|a|to))?\s*";
    private const string Negation = @"(?<!\b(?:non|pas|not|never|jamais|no)[ -])";
    private static readonly string Valeur = @"(?<val>" + M + @"+|[-+]?\d+(?:[.,]\d+)?|[A-Za-z][\w.\-]*)";
    private static readonly string DateOuAnnee = @"(?:" + M + @"+|(?:19|20)\d{2})";
    private const string UnitesPhysiques = @"(?:mm|cm|m|km|kg|g|tonnes?|ms|s|sec|secondes?|seconds?|min|minutes?|h|heures?|hours?|jours?|days?|°c|degres?|pct|pourcents?|bar|mpa|kn|m/s|m/min|mm/s|tr/min|rpm|kw|kwh|hz|litres?|liters?|l/min|l/h|m3|m3/h|µm|um|ppm)";
    private static readonly string MotifCorrespondance = @"(?<![\w.\-])(?<cle>[-+]?\d+(?:[.,]\d+)?|[A-Za-z]{1,4})\s*(?<sep>=>|->|=|:)\s*(?<lib>[A-Za-z" + M + @"][^,;\n]*?)\s*(?=[,;]|\s/\s|$|\s(?:[-+]?\d+(?:[.,]\d+)?|[A-Za-z]{1,4})\s*(?:=>|->|=|:))";

    private static readonly ResultatRegle[] Rien = [];
    private static readonly Regex RegexCorrespondance = new(MotifCorrespondance, Options);

    private static readonly HashSet<string> MotsExclusDesListes = new(StringComparer.OrdinalIgnoreCase)
    {
        "null", "vide", "empty", "absent", "absente", "omis", "omise", "omitted", "obligatoire", "facultatif", "facultative",
        "optionnel", "optionnelle", "requis", "requise", "required", "optional", "nullable", "renseigne", "renseignee",
        "present", "presente", "fourni", "fournie", "valorise", "valorisee", "positif", "positive", "negatif", "negative",
        "unique", "pair", "impair", "even", "odd", "etc", "...",
    };

    private static readonly HashSet<string> MotsVides = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "le", "la", "les", "de", "des", "du", "un", "une", "dans", "sur", "a", "au", "aux", "et", "ou", "pour", "par",
        "avec", "sans", "the", "an", "in", "on", "of", "to", "and", "or", "for", "with", "at", "by", "si", "if", "que", "qui",
        "ce", "cette", "ces", "est", "is", "are", "sont", "etre", "be", "l", "d", "vers", "entre", "puis", "then",
    };

    public static IReadOnlyList<Regle> Toutes { get; } = Construire();

    private static Regle R(string nom, PhaseRegle phase, string motif, Func<Match, ContexteRegle, IEnumerable<ResultatRegle>> construire, RegexOptions options = Options) =>
        new(nom, phase, new Regex(motif, options), construire);

    private static IReadOnlyList<Regle> Construire() =>
    [
        // ----- Phase 1 : litteraux -----
        R("unite-crochets", PhaseRegle.Litteraux, @"\[\s*(?<u>[^\]\d;," + M + @"]{1,12}?)\s*\]",
            (m, ctx) => PoserUnite(ctx, ctx.Texte(m.Groups["u"]))),
        R("correspondances", PhaseRegle.Litteraux, MotifCorrespondance, Correspondance),
        R("format-ou-motif", PhaseRegle.Litteraux,
            @"\b(?:au format|formats?|formatees?|formatted|patterns?|motifs?|regexp?|expressions? regulieres?|regular expressions?|correspond a|matches|doit respecter|must match|conforme a|forme)\s*(?::|=|en|as|in|de|du)?\s*(?<lit>" + M + @"+|[yMdHhmsfFzKtT:\-/.]{2,})(?![\w])",
            FormatOuMotif),
        R("litteral-regex-format", PhaseRegle.Litteraux, M + "+", LitteralNu),
        R("valeurs-introduites", PhaseRegle.Litteraux,
            @"\b(?<intro>valeurs?\s+(?:possibles?|autorisees?|admises?|permises?|acceptees?|attendues?|valides?)|possible values?|allowed values?|accepted values?|valid values?|permitted values?|one of|l'une? des valeurs|parmi|among|codes? possibles?|peut (?:etre|valoir|prendre)|can be|may be|must be one of|doit (?:etre|valoir)|est (?:l'un de|parmi)|is one of|either|soit)\b\s*(?:suivantes?|following|les valeurs|the following|les suivantes)?\s*[:=]?\s*(?<liste>.+?)\s*\.?\s*$",
            ValeursIntroduites),
        R("liste-majuscules", PhaseRegle.Litteraux,
            @":\s*(?<liste>[A-Z0-9_\-]+(?:\s*[,/|]\s*[A-Z0-9_\-]+)+(?:\s*(?:,|ou|or|et|and)\s+[A-Z0-9_\-]+)?)\s*\.?\s*$",
            (m, ctx) => ListeValeurs(ctx, ctx.Texte(m.Groups["liste"]), exclusion: false, minimum: 2), RegexOptions.CultureInvariant),
        R("liste-pipe", PhaseRegle.Litteraux, @"^\s*(?<liste>[\w.\-]+(?:\s*\|\s*[\w.\-]+)+)\s*\.?\s*$",
            (m, ctx) => ListeValeurs(ctx, ctx.Texte(m.Groups["liste"]), exclusion: false, minimum: 2)),

        // ----- Phase 2 : mots-cles (l'ordre compte : chaque correspondance consomme son texte) -----
        R("iso-8601", PhaseRegle.MotsCles, @"\b(?:iso[ -]?8601|iso)\b", (m, ctx) => Un(ctx, new Format(GenreFormat.Iso8601))),
        R("utc", PhaseRegle.MotsCles, @"\b(?:utc|zulu)\b", (m, ctx) => Un(ctx, new Format(GenreFormat.Utc))),
        R("date-seule", PhaseRegle.MotsCles, @"\b(?:date seule|sans heure|sans l'heure|date only|jour seul)\b", (m, ctx) => Un(ctx, new Format(GenreFormat.DateSeule))),
        R("heure-locale", PhaseRegle.MotsCles, @"\b(?:heure locale|local time)\b", (m, ctx) => Un(ctx, new Note("heure locale demandee : generee sans decalage"))),
        R("date-apres", PhaseRegle.MotsCles,
            @"\b(?:apres|posterieures? a|a partir d[eu]|depuis|after|since|later than|not before|pas avant|au plus tot(?: le)?)\s+(?:le\s+)?(?<d>" + DateOuAnnee + @")(?![\w])",
            (m, ctx) => BorneDate(m, ctx, "d", null)),
        R("date-avant", PhaseRegle.MotsCles,
            @"\b(?:avant|anterieures? a|jusqu'au?|before|until|till|earlier than|not after|pas apres|au plus tard(?: le)?)\s+(?:le\s+)?(?<d>" + DateOuAnnee + @")(?![\w])",
            (m, ctx) => BorneDate(m, ctx, null, "d")),
        R("date-entre", PhaseRegle.MotsCles,
            @"\b(?:entre|between|de|from)\s+(?<a>" + DateOuAnnee + @")\s+(?:et|and|a|to)\s+(?<b>" + DateOuAnnee + @")(?![\w])",
            (m, ctx) => BorneDate(m, ctx, "a", "b")),
        R("entre-et", PhaseRegle.MotsCles, @"\b(?:entre|between)\s+(?<a>" + N + @")\s+(?:et|and)\s+(?<b>" + N + @")" + SuffixeUnite,
            (m, ctx) => PlageDepuis(m, ctx, "a", "b", false, false)),
        R("de-a", PhaseRegle.MotsCles, @"\b(?:de|from)\s+(?<a>" + N + @")\s+(?:a|jusqu'a|to|up to|through)\s+(?<b>" + N + @")" + SuffixeUnite,
            (m, ctx) => PlageDepuis(m, ctx, "a", "b", false, false)),
        R("a-avec-unite", PhaseRegle.MotsCles, @"(?<![\w.,\-])(?<a>" + N + @")\s+(?:a|to)\s+(?<b>" + N + @")\s*" + Unites + @"\b",
            (m, ctx) => PlageDepuis(m, ctx, "a", "b", false, false)),
        R("intervalle", PhaseRegle.MotsCles, @"(?<lo>[\[\]])\s*(?<a>" + N + @")\s*(?:;|,)\s*(?<b>" + N + @")\s*(?<hi>[\[\]])",
            (m, ctx) => PlageDepuis(m, ctx, "a", "b", m.Groups["lo"].Value == "]", m.Groups["hi"].Value == "[")),
        R("plage-points", PhaseRegle.MotsCles, @"(?<![\w.,\-])(?<a>\d+(?:[.,]\d+)?)\s*\.\.\s*(?<b>\d+(?:[.,]\d+)?)(?![\w.,\-])" + SuffixeUnite,
            (m, ctx) => PlageDepuis(m, ctx, "a", "b", false, false)),
        // « 10-20 » n'est une plage que suivi d'une unite ou d'une ponctuation : « une dependance a 2-3 s » est de la prose.
        R("plage-tiret", PhaseRegle.MotsCles, @"(?<![\w.,\-])(?<a>\d+(?:[.,]\d+)?)\s*-\s*(?<b>\d+(?:[.,]\d+)?)(?![\w.,\-])(?:\s*" + Unites + @"\b|(?=\s*(?:$|[,;.)!?])))",
            (m, ctx) => PlageDepuis(m, ctx, "a", "b", false, false)),
        R("comparateur-min-inclusif", PhaseRegle.MotsCles, @"(?:>=|≥|=>|superieures? ou egales? a|greater than or equal to|not below|au moins egale? a)\s*(?<n>" + N + @")",
            (m, ctx) => PlageDepuis(m, ctx, "n", "", false, false)),
        R("comparateur-max-inclusif", PhaseRegle.MotsCles, @"(?:<=|≤|=<|inferieures? ou egales? a|less than or equal to|not above|au plus egale? a)\s*(?<n>" + N + @")",
            (m, ctx) => PlageDepuis(m, ctx, "", "n", false, false)),
        R("comparateur-min-exclusif", PhaseRegle.MotsCles, @"(?:>(?!=)|strictement superieures? a|superieures? a|plus grandes? que|greater than|above|more than|exceeds|au-dela de|beyond)\s*(?<n>" + N + @")",
            (m, ctx) => PlageDepuis(m, ctx, "n", "", true, false)),
        R("comparateur-max-exclusif", PhaseRegle.MotsCles, @"(?:<(?!=)|strictement inferieures? a|inferieures? a|plus petites? que|less than|below|under|en dessous de|en deca de)\s*(?<n>" + N + @")",
            (m, ctx) => PlageDepuis(m, ctx, "", "n", false, true)),
        R("longueur-mot-cle", PhaseRegle.MotsCles,
            @"\b(?<mot>longueur|taille|length|size|len|count|nombre)\s*(?<q>max(?:imum|imale)?|maxi|min(?:imum|imale)?|mini|fixe|fixed|exacte?)?\s*(?::|=|de|of)?\s*(?<n>" + N + @")(?![\w.,])",
            LongueurMotCle),
        R("maxlength", PhaseRegle.MotsCles, @"\b(?:max\s?length|maximum length|longueur maximale?)\s*(?::|=|de|of)?\s*(?<n>" + N + @")(?![\w.,])",
            (m, ctx) => PoserLongueur(ctx, null, ctx.Entier(m.Groups["n"]))),
        R("minlength", PhaseRegle.MotsCles, @"\b(?:min\s?length|minimum length|longueur minimale?)\s*(?::|=|de|of)?\s*(?<n>" + N + @")(?![\w.,])",
            (m, ctx) => PoserLongueur(ctx, ctx.Entier(m.Groups["n"]), null)),
        R("fixedlength", PhaseRegle.MotsCles, @"\b(?:fixed[ -]length|longueur fixe)\s*(?:of|de|:|=)?\s*(?<n>" + N + @")(?![\w.,])",
            (m, ctx) => PoserLongueur(ctx, ctx.Entier(m.Groups["n"]), ctx.Entier(m.Groups["n"]))),
        R("min-prefixe", PhaseRegle.MotsCles, @"\b" + MotsMin + Separateur + @"(?<n>" + N + @")" + SuffixeUnite,
            (m, ctx) => PlageDepuis(m, ctx, "n", "", false, false)),
        R("max-prefixe", PhaseRegle.MotsCles, @"\b" + MotsMax + Separateur + @"(?<n>" + N + @")" + SuffixeUnite,
            (m, ctx) => PlageDepuis(m, ctx, "", "n", false, false)),
        R("max-suffixe", PhaseRegle.MotsCles, @"(?<![\w.,\-])(?<n>" + N + @")" + SuffixeUnite + @"\s*(?:max(?:imum)?|maxi|au maximum|maximales?|au plus|at most|or less|ou moins|tops)\b",
            (m, ctx) => PlageDepuis(m, ctx, "", "n", false, false)),
        R("min-suffixe", PhaseRegle.MotsCles, @"(?<![\w.,\-])(?<n>" + N + @")" + SuffixeUnite + @"\s*(?:minimum|mini|au minimum|minimales?|at least|or more|ou plus)\b",
            (m, ctx) => PlageDepuis(m, ctx, "n", "", false, false)),
        R("decimales", PhaseRegle.MotsCles, @"(?<![\w.,])(?<n>\d+)\s*(?:decimales?|decimals?|decimal places?|chiffres? apres la virgule|digits? after the (?:decimal )?point)\b",
            (m, ctx) => PoserDecimales(ctx, ctx.Entier(m.Groups["n"]))),
        R("arrondi", PhaseRegle.MotsCles, @"\b(?<mot>arrondies? (?:a|au)|rounded to|round to|precision(?: de| of)?)\s*:?\s*(?<n>" + N + @")\s*(?<u>decimales?|decimals?|decimal places?|chiffres?|digits?)?\b", Arrondi),
        R("sans-decimale", PhaseRegle.MotsCles, @"\b(?:pas de decimales?|sans decimales?|no decimals?|nombre entier|whole number|integer)\b", (m, ctx) => PoserDecimales(ctx, 0)),
        R("fraction-nommee", PhaseRegle.MotsCles, @"\b(?:au )?(?<f>dixieme|tenth|centieme|hundredth|millieme|thousandth)\b",
            (m, ctx) => PoserDecimales(ctx, m.Groups["f"].Value.ToLowerInvariant() switch { "dixieme" or "tenth" => 1, "centieme" or "hundredth" => 2, _ => 3 })),
        R("exact-unite", PhaseRegle.MotsCles,
            @"(?:\b(?:exactement|exactly|precisement|precisely|toujours|always|obligatoirement|doit (?:faire|contenir|comporter)|must (?:be|contain|have)|contient|contains|sur|on|de|of)\s+)?(?<![\w.,\-])(?<n>\d+(?:[.,]\d+)?)\s*" + Unites + @"\b",
            (m, ctx) => PlageDepuis(m, ctx, "n", "", false, false, exact: true)),
        R("strictement-positif", PhaseRegle.MotsCles, @"\b(?:strictement positi(?:f|ve)s?|strictly positive|positi(?:f|ve)s? stricte?s?)\b", (m, ctx) => PoserSigne(ctx, true, false)),
        R("non-negatif", PhaseRegle.MotsCles, @"\b(?:non[ -]?negati(?:f|ve)s?|nonnegative|positi(?:f|ve)s? ou nul(?:le)?s?|zero or positive|positive or zero)\b", (m, ctx) => PoserSigne(ctx, false, false)),
        R("non-positif", PhaseRegle.MotsCles, @"\b(?:negati(?:f|ve)s? ou nul(?:le)?s?|non[ -]?positi(?:f|ve)s?|zero or negative|negative or zero)\b", (m, ctx) => PoserSigne(ctx, false, true)),
        R("positif", PhaseRegle.MotsCles, Negation + @"\bpositi(?:f|ve)s?\b", (m, ctx) => PoserSigne(ctx, false, false)),
        R("negatif", PhaseRegle.MotsCles, Negation + @"\bnegati(?:f|ve)s?\b", (m, ctx) => PoserSigne(ctx, true, true)),
        R("non-nul", PhaseRegle.MotsCles, @"\b(?:non nul(?:le)?s?|non[ -]?zero|nonzero|differentes? de (?:0|zero)|jamais (?:0|zero)|never (?:0|zero)|!= ?0|≠ ?0)\b", NonNul),
        R("optionnel", PhaseRegle.MotsCles,
            @"\b(?:optionnel(?:le)?s?|facultati(?:f|ve)s?|optional|peut etre (?:null|vide|absente?|omise?|nul(?:le)?)|may be (?:null|empty|omitted|absent)|can be (?:null|empty|omitted)|nullable|not required|non obligatoire|non requise?|pas obligatoire|si (?:connue?|disponible|renseignee?)|if (?:known|available|provided))\b",
            (m, ctx) => Un(ctx, new Presence(false), CibleContrainte.Membre)),
        R("obligatoire", PhaseRegle.MotsCles,
            Negation + @"\b(?:obligatoire|requise?s?|required|mandatory|non[ -]?null(?:able)?|not null(?:able)?|jamais null|never null|toujours (?:renseignee?|presente?|fournie?|valorisee?)|always (?:present|set|provided|filled)|doit etre (?:renseignee?|fournie?|presente?)|must be (?:provided|set|present)|ne peut (?:pas )?etre (?:vide|null)|cannot be (?:null|empty)|ne doit pas etre (?:vide|null))\b",
            (m, ctx) => Un(ctx, new Presence(true), CibleContrainte.Membre)),
        R("non-vide", PhaseRegle.MotsCles, @"\b(?:non vide|not empty|non-empty|jamais vide|never empty)\b", NonVide),
        R("au-moins-un", PhaseRegle.MotsCles, @"\b(?:au moins une?|at least one|doit contenir au moins une?)\b", (m, ctx) => PoserTaille(ctx, 1, null)),
        R("peut-etre-vide", PhaseRegle.MotsCles, @"\b(?:liste vide (?:autorisee|possible|acceptee|permise)|empty (?:list|array) (?:allowed|ok|permitted)|vide autorisee?|eventuellement vide|possibly empty|tableau vide autorise)\b",
            (m, ctx) => PoserTaille(ctx, 0, null)),
        R("sans-doublon", PhaseRegle.MotsCles, @"\b(?:sans doublons?|pas de doublons?|no duplicates?|distinct(?:e|s|es)?|elements? uniques?|unique (?:elements|items|values)|valeurs uniques)\b",
            (m, ctx) => ctx.Type.EstConteneur ? Un(ctx, new ElementsDistincts(), CibleContrainte.Membre) : Un(ctx, new Unicite(false), CibleContrainte.Membre)),
        R("passe", PhaseRegle.MotsCles,
            Negation + @"\b(?:dans le passe|passees?|anterieures? a aujourd'hui|avant aujourd'hui|in the past|past|before (?:today|now)|deja (?:ecoulee?|survenue?)|historique|revolue?)\b|\b(?:pas|not|jamais|never) (?:dans le futur|in the future)\b",
            (m, ctx) => PoserTemporalite(ctx, GenreTemporalite.Passe)),
        R("futur", PhaseRegle.MotsCles,
            Negation + @"\b(?:dans le futur|futures?|a venir|posterieures? a aujourd'hui|apres aujourd'hui|in the future|after (?:today|now)|upcoming|ulterieures?)\b|\b(?:pas|not|jamais|never) (?:dans le passe|in the past)\b",
            (m, ctx) => PoserTemporalite(ctx, GenreTemporalite.Futur)),
        R("aujourdhui", PhaseRegle.MotsCles, @"\b(?:aujourd'hui|today|date du jour|current date|maintenant|date courante)\b", (m, ctx) => PoserTemporalite(ctx, GenreTemporalite.Aujourdhui)),
        R("email", PhaseRegle.MotsCles, @"\b(?:e-?mails?|courriels?|adresses? (?:e-?mail|electroniques?|mail)|mail)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Email)),
        R("url", PhaseRegle.MotsCles, @"\b(?:urls?|uris?|liens?|links?|adresse web|site web|website|https?)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Url)),
        R("guid", PhaseRegle.MotsCles, @"\b(?:guid|uuid)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Guid)),
        R("telephone", PhaseRegle.MotsCles, @"\b(?:telephone|tel\.?|phone|mobile|portable|gsm|fax)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Telephone)),
        R("code-postal", PhaseRegle.MotsCles, @"\b(?:code postal|zip ?code|postal code)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.CodePostal)),
        R("majuscules", PhaseRegle.MotsCles, @"\b(?:majuscules?|en capitales?|upper[ -]?case|uppercase|caps|en maj)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Majuscules)),
        R("minuscules", PhaseRegle.MotsCles, @"\b(?:minuscules?|lower[ -]?case|lowercase|en min)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Minuscules)),
        R("chiffres-uniquement", PhaseRegle.MotsCles, @"\b(?:chiffres? (?:uniquement|seulement)|uniquement (?:des )?chiffres|que des chiffres|numerique(?: uniquement)?|digits? only|only digits|numbers only|numeric(?:al)? only)\b",
            (m, ctx) => PoserFormatTexte(ctx, GenreFormat.ChiffresUniquement)),
        R("alphanumerique", PhaseRegle.MotsCles, @"\b(?:alphanum(?:erique|eric)s?|lettres et chiffres|letters and digits)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Alphanumerique)),
        R("lettres", PhaseRegle.MotsCles, @"\b(?:lettres? (?:uniquement|seulement)|uniquement (?:des )?lettres|alphabetique|alphabetic|letters only|only letters)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.Lettres)),
        R("sans-espace", PhaseRegle.MotsCles, @"\b(?:sans espaces?|no spaces?|without spaces?)\b", (m, ctx) => PoserFormatTexte(ctx, GenreFormat.SansEspaces)),
        R("multiple", PhaseRegle.MotsCles, @"\b(?:multiples? de|multiples? of|divisible par|divisible by|par pas de|par paliers? de|pas de|step of|increments? of|granularite(?: de)?|rounded to (?:the )?nearest)\s*(?<n>" + N + @")(?!\s*(?:decimal|chiffre|digit))",
            (m, ctx) => PoserMultiple(ctx, ctx.Nombre(m.Groups["n"]))),
        R("pair", PhaseRegle.MotsCles, @"(?<!\b(?:une?|a) )\b(?:paire?s?|even)\b(?!\s+(?:of|de))", (m, ctx) => PoserParite(ctx, true)),
        R("impair", PhaseRegle.MotsCles, @"\b(?:impaire?s?|odd)\b", (m, ctx) => PoserParite(ctx, false)),
        R("fixe", PhaseRegle.MotsCles, @"\b(?:toujours|always|vaut toujours|est toujours|constante?|constant|fixe(?: a)?|fixed(?: to| at)?|valeur fixe|invariable|systematiquement)\s*(?::|=|egale? a|equal to|a|to)?\s*" + Valeur,
            (m, ctx) => PoserFixe(m, ctx, false)),
        R("vaut", PhaseRegle.MotsCles, @"\b(?:vaut|egale? a|equals?|is set to|set to|valeur\s*:|value\s*:)\s*" + Valeur, (m, ctx) => PoserFixe(m, ctx, false)),
        R("egal", PhaseRegle.MotsCles, @"(?<![<>!=])=(?!=|>)\s*" + Valeur, (m, ctx) => PoserFixe(m, ctx, false)),
        R("defaut", PhaseRegle.MotsCles, @"\b(?:par defaut|valeur par defaut|default(?: value)?|defaults? to|by default|si absente?|if omitted|initialisee? a)\s*(?::|=|a|to|is|est)?\s*" + Valeur,
            (m, ctx) => PoserFixe(m, ctx, true)),
        R("unique", PhaseRegle.MotsCles, @"\b(?:unique|non dupliquee?|jamais deux fois|cle primaire|primary key|identifiant unique|unique (?:identifier|id|key)|cle unique)\b", (m, ctx) => Un(ctx, new Unicite(false))),
        R("sequentiel", PhaseRegle.MotsCles, @"\b(?:incrementale?s?|auto-?incrementee?s?|auto-?increment(?:ed|al)?|sequentiel(?:le)?s?|sequential|numero de sequence|sequence number|croissante?s?|increasing|compteur|counter|numero d'ordre|identity|identifiants?|identifier)\b",
            (m, ctx) => Un(ctx, new Unicite(true))),
        R("unite-prose", PhaseRegle.MotsCles, @"\b(?:en|in|exprimees? en|unite\s*:?|unit\s*:?)\s+(?<u>" + UnitesPhysiques + @")(?![\w/])|(?<u>%)(?!\w)",
            (m, ctx) => PoserUnite(ctx, ctx.Texte(m.Groups["u"]))),
        R("unite-suffixe", PhaseRegle.MotsCles, @"(?<![\w.,\-])(?<n>" + N + @")\s*(?<u>" + UnitesPhysiques + @"|%)(?![\w/])",
            (m, ctx) => ctx.EstNumerique || ctx.GenreCible == GenreValeur.Duree ? PoserUnite(ctx, ctx.Texte(m.Groups["u"])) : Rien),
        R("unite-nue", PhaseRegle.MotsCles, @"\b(?<u>millisecondes?|milliseconds?|ms|secondes?|seconds?|sec|minutes?|heures?|hours?|jours?|days?|mm|cm|km|kg|tonnes?|°c|bar|mpa|kn|rpm|tr/min|m/min|l/min|kw|kwh|hz|ppm)\b",
            (m, ctx) => ctx.EstNumerique || ctx.GenreCible == GenreValeur.Duree ? PoserUnite(ctx, ctx.Texte(m.Groups["u"])) : Rien),
        R("pourcentage", PhaseRegle.MotsCles, @"\b(?:pourcentages?|percent(?:age)?s?|taux)\b", (m, ctx) => ctx.EstNumerique ? PoserUnite(ctx, "%") : Rien),
        R("exclusions", PhaseRegle.MotsCles, @"\b(?:sauf|except|excepte|hors|excluding|jamais|never|differentes? de|autre que|other than)\s+(?<liste>.+?)\s*\.?\s*$",
            (m, ctx) => ListeValeurs(ctx, ctx.Texte(m.Groups["liste"]), exclusion: true, minimum: 1)),
        R("liste-nue", PhaseRegle.MotsCles, @"^\s*(?<liste>[\w.\-]+(?:\s*,\s*[\w.\-]+)*\s*(?:ou|or)\s+[\w.\-]+)\s*\.?\s*$",
            (m, ctx) => ListeValeurs(ctx, ctx.Texte(m.Groups["liste"]), exclusion: false, minimum: 2)),
    ];

    // ----- Constructeurs de contraintes -----

    private static IEnumerable<ResultatRegle> Un(ContexteRegle ctx, Contrainte contrainte, CibleContrainte cible = CibleContrainte.Auto) =>
        [new ResultatRegle(contrainte with { Source = ctx.Source }, cible)];

    private static IEnumerable<ResultatRegle> Deux(ContexteRegle ctx, CibleContrainte cible, Contrainte premiere, Contrainte seconde) =>
        [new ResultatRegle(premiere with { Source = ctx.Source }, cible), new ResultatRegle(seconde with { Source = ctx.Source }, cible)];

    private enum Categorie
    {
        Aucune,
        Caracteres,
        Elements,
        Chiffres,
    }

    private static Categorie CategorieUnite(Group unite)
    {
        if (!unite.Success)
        {
            return Categorie.Aucune;
        }

        var texte = unite.Value.ToLowerInvariant();
        if (texte.StartsWith("chiffre", StringComparison.Ordinal) || texte.StartsWith("digit", StringComparison.Ordinal))
        {
            return Categorie.Chiffres;
        }

        if (texte.StartsWith("car", StringComparison.Ordinal) || texte.StartsWith("char", StringComparison.Ordinal) ||
            texte.StartsWith("lettre", StringComparison.Ordinal) || texte.StartsWith("letter", StringComparison.Ordinal) ||
            texte.StartsWith("symbole", StringComparison.Ordinal) || texte.StartsWith("signe", StringComparison.Ordinal))
        {
            return Categorie.Caracteres;
        }

        return Categorie.Elements;
    }

    private static IEnumerable<ResultatRegle> PlageDepuis(Match m, ContexteRegle ctx, string groupeMin, string groupeMax, bool minExclusif, bool maxExclusif, bool exact = false)
    {
        var min = groupeMin.Length > 0 ? ctx.Nombre(m.Groups[groupeMin]) : null;
        var max = groupeMax.Length > 0 ? ctx.Nombre(m.Groups[groupeMax]) : null;
        if (min is null && max is null)
        {
            return Rien;
        }

        return Intervalle(ctx, min, max, minExclusif, maxExclusif, CategorieUnite(m.Groups["u"]), exact);
    }

    /// <summary>Le coeur du dispatch : un intervalle numerique devient plage, longueur, taille, nombre de chiffres ou plage de dates selon l'unite et le type.</summary>
    private static IEnumerable<ResultatRegle> Intervalle(ContexteRegle ctx, decimal? min, decimal? max, bool minExclusif, bool maxExclusif, Categorie categorie, bool exact)
    {
        if (exact)
        {
            max = min;
        }

        switch (categorie)
        {
            case Categorie.Caracteres:
                return PoserLongueur(ctx, NormaliseurTexte.Entier(min), NormaliseurTexte.Entier(max));
            case Categorie.Elements:
                return PoserTaille(ctx, NormaliseurTexte.Entier(min), NormaliseurTexte.Entier(max));
            case Categorie.Chiffres:
                return PoserChiffres(ctx, NormaliseurTexte.Entier(min), NormaliseurTexte.Entier(max));
        }

        switch (ctx.GenreCible)
        {
            case GenreValeur.Entier:
            case GenreValeur.Reel:
            case GenreValeur.Duree:
                return Un(ctx, new Plage(min, max, minExclusif, maxExclusif), ctx.CibleValeur);

            case GenreValeur.Texte:
            case GenreValeur.Caractere:
                return ctx.Type.EstConteneur
                    ? PoserTaille(ctx, NormaliseurTexte.Entier(min), NormaliseurTexte.Entier(max))
                    : PoserLongueur(ctx, NormaliseurTexte.Entier(min), NormaliseurTexte.Entier(max));

            case GenreValeur.DateHeure:
            case GenreValeur.DateSeule:
            {
                var dateMin = Annee(min, fin: false);
                var dateMax = Annee(max, fin: true);
                if ((min is null || dateMin is not null) && (max is null || dateMax is not null))
                {
                    return Un(ctx, new PlageDates(dateMin, dateMax), ctx.CibleValeur);
                }

                ctx.Avertir("plage numerique sans objet pour une date");
                return Rien;
            }

            default:
                // Objet, booleen, Guid... : pas de plage possible ; le nombre reste dans la phrase, qui sera signalee non reconnue.
                return ctx.Type.EstConteneur
                    ? PoserTaille(ctx, NormaliseurTexte.Entier(min), NormaliseurTexte.Entier(max))
                    : Rien;
        }
    }

    private static DateTime? Annee(decimal? valeur, bool fin)
    {
        var annee = NormaliseurTexte.Entier(valeur);
        if (annee is null || annee < 1900 || annee > 2100)
        {
            return null;
        }

        return fin ? new DateTime(annee.Value, 12, 31) : new DateTime(annee.Value, 1, 1);
    }

    private static IEnumerable<ResultatRegle> PoserLongueur(ContexteRegle ctx, int? min, int? max)
    {
        if (min is null && max is null)
        {
            return Rien;
        }

        if (ctx.EstTexte)
        {
            return Un(ctx, new LongueurTexte(min, max), ctx.CibleValeur);
        }

        return ctx.EstNumerique ? PoserChiffres(ctx, min, max) : Rien;
    }

    private static IEnumerable<ResultatRegle> PoserTaille(ContexteRegle ctx, int? min, int? max)
    {
        if (ctx.Type.EstConteneur)
        {
            return Un(ctx, new TailleCollection(min, max), CibleContrainte.Membre);
        }

        return ctx.EstTexte ? Un(ctx, new LongueurTexte(min, max), CibleContrainte.Membre) : Rien;
    }

    private static IEnumerable<ResultatRegle> PoserChiffres(ContexteRegle ctx, int? min, int? max)
    {
        if (min is null && max is null)
        {
            return Rien;
        }

        if (ctx.EstNumerique)
        {
            decimal? borneMin = min is null ? null : min <= 1 ? 0 : Puissance10(min.Value - 1);
            decimal? borneMax = max is null ? null : Puissance10(max.Value) - 1;
            if (min is not null && max is null)
            {
                // « 5 chiffres » sans max explicite : exactement 5 chiffres.
                borneMax = Puissance10(min.Value) - 1;
            }

            return Un(ctx, new Plage(borneMin, borneMax), ctx.CibleValeur);
        }

        return ctx.EstTexte
            ? Deux(ctx, ctx.CibleValeur, new LongueurTexte(min, max ?? min), new Format(GenreFormat.ChiffresUniquement))
            : Rien;
    }

    private static decimal Puissance10(int exposant)
    {
        decimal resultat = 1;
        for (var i = 0; i < Math.Min(exposant, 27); i++)
        {
            resultat *= 10;
        }

        return resultat;
    }

    private static IEnumerable<ResultatRegle> BorneDate(Match m, ContexteRegle ctx, string? groupeMin, string? groupeMax)
    {
        if (!ctx.EstDate)
        {
            return Rien;
        }

        var min = groupeMin is null ? null : DateDepuis(m.Groups[groupeMin], ctx, fin: false);
        var max = groupeMax is null ? null : DateDepuis(m.Groups[groupeMax], ctx, fin: true);
        if ((groupeMin is not null && min is null) || (groupeMax is not null && max is null))
        {
            return Rien;
        }

        return Un(ctx, new PlageDates(min, max), ctx.CibleValeur);
    }

    private static DateTime? DateDepuis(Group groupe, ContexteRegle ctx, bool fin)
    {
        if (!groupe.Success)
        {
            return null;
        }

        var litteral = ctx.LitteralA(groupe);
        if (litteral is not null)
        {
            return litteral.Genre == GenreLitteral.Date ? NormaliseurTexte.Date(litteral.Texte) : null;
        }

        return Annee(NormaliseurTexte.Nombre(ctx.Texte(groupe)), fin);
    }

    private static IEnumerable<ResultatRegle> LongueurMotCle(Match m, ContexteRegle ctx)
    {
        var n = ctx.Entier(m.Groups["n"]);
        if (n is null)
        {
            return Rien;
        }

        var qualificatif = m.Groups["q"].Value.ToLowerInvariant();
        int? min = null;
        int? max = null;
        if (qualificatif.StartsWith("max", StringComparison.Ordinal))
        {
            max = n;
        }
        else if (qualificatif.StartsWith("min", StringComparison.Ordinal))
        {
            min = n;
        }
        else
        {
            min = n;
            max = n;
        }

        if (ctx.EstNumerique)
        {
            return Un(ctx, new Plage(min, max), ctx.CibleValeur);
        }

        var mot = m.Groups["mot"].Value.ToLowerInvariant();
        if (ctx.Type.EstConteneur && (mot is "taille" or "size" or "count" or "nombre" || !ctx.EstTexte))
        {
            return PoserTaille(ctx, min, max);
        }

        return ctx.EstTexte ? PoserLongueur(ctx, min, max) : PoserTaille(ctx, min, max);
    }

    private static IEnumerable<ResultatRegle> PoserDecimales(ContexteRegle ctx, int? nombre)
    {
        if (nombre is null || nombre < 0 || !(ctx.EstNumerique || ctx.GenreCible == GenreValeur.Duree))
        {
            return Rien;
        }

        return Un(ctx, new Decimales(nombre.Value), ctx.CibleValeur);
    }

    private static IEnumerable<ResultatRegle> Arrondi(Match m, ContexteRegle ctx)
    {
        var n = ctx.Nombre(m.Groups["n"]);
        if (n is null || !ctx.EstNumerique)
        {
            return Rien;
        }

        if (m.Groups["u"].Success)
        {
            return PoserDecimales(ctx, NormaliseurTexte.Entier(n));
        }

        if (n > 0 && n < 1)
        {
            return Deux(ctx, ctx.CibleValeur, new Multiple(n.Value), new Decimales(NombreDecimales(n.Value)));
        }

        var mot = m.Groups["mot"].Value.ToLowerInvariant();
        return mot.StartsWith("precision", StringComparison.Ordinal)
            ? PoserDecimales(ctx, NormaliseurTexte.Entier(n))
            : PoserMultiple(ctx, n);
    }

    private static int NombreDecimales(decimal valeur)
    {
        var texte = valeur.ToString(CultureInfo.InvariantCulture).TrimEnd('0');
        var point = texte.IndexOf('.');
        return point < 0 ? 0 : texte.Length - point - 1;
    }

    private static IEnumerable<ResultatRegle> PoserSigne(ContexteRegle ctx, bool exclusif, bool estMax)
    {
        if (!ctx.EstNumerique)
        {
            return Rien;
        }

        return Un(ctx, estMax ? new Plage(null, 0, false, exclusif) : new Plage(0, null, exclusif, false), ctx.CibleValeur);
    }

    private static IEnumerable<ResultatRegle> NonNul(Match m, ContexteRegle ctx)
    {
        if (ctx.EstNumerique)
        {
            return Un(ctx, new ValeursExclues(["0"]), ctx.CibleValeur);
        }

        return Un(ctx, new Presence(true), CibleContrainte.Membre);
    }

    private static IEnumerable<ResultatRegle> NonVide(Match m, ContexteRegle ctx)
    {
        if (ctx.Type.EstConteneur)
        {
            return Deux(ctx, CibleContrainte.Membre, new TailleCollection(1, null), new Presence(true));
        }

        if (ctx.EstTexte)
        {
            return Deux(ctx, CibleContrainte.Membre, new LongueurTexte(1, null), new Presence(true));
        }

        return Un(ctx, new Presence(true), CibleContrainte.Membre);
    }

    private static IEnumerable<ResultatRegle> PoserTemporalite(ContexteRegle ctx, GenreTemporalite genre) =>
        ctx.EstDate ? Un(ctx, new Temporalite(genre), ctx.CibleValeur) : Rien;

    private static IEnumerable<ResultatRegle> PoserFormatTexte(ContexteRegle ctx, GenreFormat genre) =>
        ctx.EstTexte ? Un(ctx, new Format(genre), ctx.CibleValeur) : Rien;

    private static IEnumerable<ResultatRegle> PoserMultiple(ContexteRegle ctx, decimal? pas)
    {
        if (pas is null || pas <= 0 || !ctx.EstNumerique)
        {
            return Rien;
        }

        return Un(ctx, new Multiple(pas.Value), ctx.CibleValeur);
    }

    private static IEnumerable<ResultatRegle> PoserParite(ContexteRegle ctx, bool pair) =>
        ctx.GenreCible == GenreValeur.Entier ? Un(ctx, new Parite(pair), ctx.CibleValeur) : Rien;

    private static IEnumerable<ResultatRegle> PoserUnite(ContexteRegle ctx, string symbole)
    {
        symbole = symbole.Trim();
        if (symbole.Length == 0)
        {
            return Rien;
        }

        var normalise = NormaliseurTexte.NormaliserIsometrique(symbole).ToLowerInvariant();
        var resultats = new List<ResultatRegle> { new(new Unite(symbole) { Source = ctx.Source }) };
        if (ctx.EstNumerique && normalise is "%" or "pct" or "pourcent" or "pourcents" or "percent")
        {
            resultats.Add(new ResultatRegle(new Plage(0, 100) { Origine = OrigineContrainte.Nom, Source = "unite %" }, ctx.CibleValeur));
        }

        return resultats;
    }

    private static IEnumerable<ResultatRegle> PoserFixe(Match m, ContexteRegle ctx, bool parDefaut)
    {
        var groupe = m.Groups["val"];
        var litteral = ctx.LitteralA(groupe);
        var texte = litteral is not null ? litteral.Texte : ctx.Texte(groupe);
        if (litteral is null && MotsVides.Contains(texte))
        {
            return Rien;
        }

        if (ctx.EstNumerique)
        {
            var nombre = NormaliseurTexte.Nombre(texte);
            if (nombre is null)
            {
                return Rien;
            }

            texte = NormaliseurTexte.FormaterNombre(nombre.Value);
        }
        else if (ctx.GenreCible == GenreValeur.Booleen && !EstBooleen(texte))
        {
            return Rien;
        }

        return Un(ctx, new ValeurFixe(texte, parDefaut), ctx.CibleValeur);
    }

    private static bool EstBooleen(string texte) =>
        texte.ToLowerInvariant() is "true" or "false" or "vrai" or "faux" or "oui" or "non" or "yes" or "no" or "0" or "1";

    private static IEnumerable<ResultatRegle> ListeValeurs(ContexteRegle ctx, string texteListe, bool exclusion, int minimum)
    {
        if (!(ctx.EstTexte || ctx.EstNumerique || ctx.GenreCible == GenreValeur.Enum))
        {
            return Rien;
        }

        var elements = NormaliseurTexte.ScinderListe(texteListe).Where(e => !MotsExclusDesListes.Contains(e)).ToList();
        if (elements.Count < minimum)
        {
            return Rien;
        }

        // Introducteur faible (« doit etre », « peut etre », « soit ») : la liste n'est retenue que si chaque element
        // ressemble a une valeur (un seul jeton, nombre si le membre est numerique), sinon c'est de la prose.
        if (minimum > 1 && elements.Any(e => e.Any(char.IsWhiteSpace) || (ctx.EstNumerique && NormaliseurTexte.Nombre(e) is null)))
        {
            return Rien;
        }

        var valeurs = ContraintesCommunes.FiltrerValeurs(elements, ctx.TypeCible, ctx.Avertissements, ctx.Source);
        if (valeurs.Count == 0)
        {
            return Rien;
        }

        return Un(ctx, exclusion ? new ValeursExclues(valeurs) : new ValeursAutorisees(valeurs), ctx.CibleValeur);
    }

    private static IEnumerable<ResultatRegle> ValeursIntroduites(Match m, ContexteRegle ctx)
    {
        var intro = m.Groups["intro"].Value.ToLowerInvariant();
        var faible = intro.StartsWith("peut", StringComparison.Ordinal) || intro.StartsWith("can be", StringComparison.Ordinal) ||
                     intro.StartsWith("may be", StringComparison.Ordinal) || intro.StartsWith("doit", StringComparison.Ordinal) ||
                     intro.StartsWith("est ", StringComparison.Ordinal) || intro.StartsWith("either", StringComparison.Ordinal) ||
                     intro.StartsWith("soit", StringComparison.Ordinal);
        return ListeValeurs(ctx, ctx.Texte(m.Groups["liste"]), exclusion: false, minimum: faible ? 2 : 1);
    }

    private static IEnumerable<ResultatRegle> FormatOuMotif(Match m, ContexteRegle ctx)
    {
        var groupe = m.Groups["lit"];
        var litteral = ctx.LitteralA(groupe);
        if (litteral is null)
        {
            var jeton = ctx.Texte(groupe);
            return NormaliseurTexte.EstFormatDate(jeton, 1) ? Un(ctx, new FormatDate(jeton)) : Rien;
        }

        return litteral.Genre switch
        {
            GenreLitteral.Regex => Un(ctx, new Motif(litteral.Texte)),
            GenreLitteral.FormatDate => Un(ctx, new FormatDate(litteral.Texte)),
            GenreLitteral.Guillemets when NormaliseurTexte.EstFormatDate(litteral.Texte, 1) => Un(ctx, new FormatDate(litteral.Texte)),
            GenreLitteral.Guillemets when RessembleRegex(litteral.Texte) => Un(ctx, new Motif(litteral.Texte)),
            GenreLitteral.Guillemets => Un(ctx, new Note("format : " + litteral.Texte)),
            _ => Un(ctx, new Note("exemple : " + litteral.Texte)),
        };
    }

    private static IEnumerable<ResultatRegle> LitteralNu(Match m, ContexteRegle ctx)
    {
        var litteral = ctx.LitteralA(m.Groups[0]);
        return litteral?.Genre switch
        {
            GenreLitteral.Regex => Un(ctx, new Motif(litteral.Texte)),
            GenreLitteral.FormatDate => Un(ctx, new FormatDate(litteral.Texte)),
            _ => Rien,
        };
    }

    private static bool RessembleRegex(string texte) => texte.IndexOfAny(['^', '$', '[', ']', '{', '}', '\\', '|', '*', '+', '?']) >= 0;

    /// <summary>
    /// « 0=arret 1=marche » : deux correspondances ou plus restreignent les valeurs ; une seule (« 0 = illimite »)
    /// n'est qu'une note. Toutes les paires de la phrase sont lues d'un coup par la premiere correspondance.
    /// </summary>
    private static IEnumerable<ResultatRegle> Correspondance(Match m, ContexteRegle ctx)
    {
        if (!PaireValide(m, ctx))
        {
            return Rien;
        }

        var paires = RegexCorrespondance.Matches(ctx.Travail).Where(p => PaireValide(p, ctx)).ToList();
        var numeriques = paires.All(p => NormaliseurTexte.Nombre(p.Groups["cle"].Value) is not null);
        if (!numeriques && paires.Count < 2)
        {
            return Rien;
        }

        var cle = ClePaire(m, ctx);
        var libelle = ctx.TexteInterne(m.Groups["lib"]).Trim();
        var resultats = new List<ResultatRegle> { new(new Note($"{cle} = {libelle}") { Source = ctx.Source }) };
        if (paires.Count >= 2 && m.Index == paires[0].Index)
        {
            var valeurs = ContraintesCommunes.FiltrerValeurs(paires.Select(p => ClePaire(p, ctx)).ToList(), ctx.TypeCible, ctx.Avertissements, ctx.Source);
            if (valeurs.Count > 0)
            {
                resultats.Add(new ResultatRegle(new ValeursAutorisees(valeurs) { Source = ctx.Source }, ctx.CibleValeur));
            }
        }

        return resultats;
    }

    private static bool PaireValide(Match paire, ContexteRegle ctx)
    {
        var numerique = NormaliseurTexte.Nombre(paire.Groups["cle"].Value) is not null;
        if (!numerique && !(ctx.EstTexte || ctx.GenreCible == GenreValeur.Enum))
        {
            return false;
        }

        if (numerique && !(ctx.EstNumerique || ctx.EstTexte || ctx.GenreCible == GenreValeur.Enum))
        {
            return false;
        }

        if (paire.Groups["sep"].Value == ":")
        {
            var i = paire.Index - 1;
            while (i >= 0 && ctx.Travail[i] == ' ')
            {
                i--;
            }

            if (i >= 0 && ctx.Travail[i] is not (',' or ';' or '/'))
            {
                return false;
            }
        }

        return true;
    }

    private static string ClePaire(Match paire, ContexteRegle ctx)
    {
        var texte = ctx.Texte(paire.Groups["cle"]);
        var nombre = NormaliseurTexte.Nombre(texte);
        return nombre is null ? texte : NormaliseurTexte.FormaterNombre(nombre.Value);
    }
}
