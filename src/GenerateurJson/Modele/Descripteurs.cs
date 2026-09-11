namespace GenerateurJson;

/// <summary>Nature JSON d'un type C# une fois resolu.</summary>
public enum GenreValeur
{
    Entier,
    Reel,
    Booleen,
    Texte,
    Caractere,
    Guid,
    DateHeure,
    DateSeule,
    HeureSeule,
    Duree,
    Uri,
    Octets,
    Enum,
    Objet,
    Collection,
    Dictionnaire,
    Inconnu,
}

/// <summary>
/// Reference a un type telle qu'ecrite sur un membre (int, string?, List&lt;Produit&gt;...), resolue vers un genre JSON.
/// Classe (et non record) : le graphe des types est cyclique, une egalite structurelle bouclerait.
/// </summary>
public sealed class ReferenceType
{
    /// <summary>Genre JSON. Vaut Inconnu pour un nom a resoudre dans le catalogue jusqu'a la seconde passe.</summary>
    public GenreValeur Genre { get; set; }

    /// <summary>Nom C# normalise : int, decimal, Produit, Outer.Inner, List...</summary>
    public required string Nom { get; init; }

    /// <summary>Texte tel qu'ecrit dans la source, pour les messages.</summary>
    public required string TexteOriginal { get; init; }

    public bool EstNullable { get; init; }

    /// <summary>Vrai pour un identifiant utilisateur a chercher dans le catalogue lors de la seconde passe.</summary>
    public bool AResoudre { get; init; }

    /// <summary>Type des elements (collection) ou des valeurs (dictionnaire).</summary>
    public ReferenceType? Element { get; init; }

    /// <summary>Type des cles (dictionnaire).</summary>
    public ReferenceType? Cle { get; init; }

    /// <summary>Declaration correspondante dans le catalogue (enum ou objet), renseignee par la seconde passe.</summary>
    public DescripteurType? Declaration { get; set; }

    public bool EstNumerique => Genre is GenreValeur.Entier or GenreValeur.Reel;

    public bool EstConteneur => Genre is GenreValeur.Collection or GenreValeur.Dictionnaire;

    /// <summary>Genre porteur des contraintes de valeur : l'element pour une collection ou un dictionnaire, le type lui-meme sinon.</summary>
    public GenreValeur GenreCible => EstConteneur ? Element?.Genre ?? GenreValeur.Inconnu : Genre;

    /// <summary>Type porteur des contraintes de valeur (voir <see cref="GenreCible"/>).</summary>
    public ReferenceType TypeCible => EstConteneur && Element is not null ? Element : this;

    public ReferenceType AvecNullable(string texteOriginal) => new()
    {
        Genre = Genre,
        Nom = Nom,
        TexteOriginal = texteOriginal,
        EstNullable = true,
        AResoudre = AResoudre,
        Element = Element,
        Cle = Cle,
        Declaration = Declaration,
    };

    public override string ToString() => TexteOriginal;
}

public enum GenreDeclaration
{
    Classe,
    Record,
    Struct,
    RecordStruct,
    Interface,
    Enum,
}

/// <summary>Un type declare dans les sources fournies (classe, record, struct, interface ou enum).</summary>
public sealed class DescripteurType
{
    /// <summary>Nom qualifie : EspaceDeNoms.Englobant.Nom.</summary>
    public required string NomComplet { get; init; }

    public required string NomSimple { get; init; }

    public required string EspaceNoms { get; init; }

    public required GenreDeclaration Genre { get; init; }

    public required string Fichier { get; init; }

    public int Ligne { get; init; }

    public bool EstAbstrait { get; init; }

    public bool EstStatique { get; init; }

    public bool EstGenerique { get; init; }

    public bool EstFlags { get; init; }

    /// <summary>Texte du &lt;summary&gt; du type, pour le rapport.</summary>
    public string? Resume { get; set; }

    /// <summary>Types de base tels qu'ecrits (EntiteBase, IEquatable&lt;Bobine&gt;...).</summary>
    public List<string> TypesDeBase { get; } = [];

    /// <summary>Membres declares par ce type (sans ceux herites).</summary>
    public List<DescripteurMembre> Membres { get; } = [];

    /// <summary>Membres d'une enumeration, avec leur valeur effective.</summary>
    public List<MembreEnum> MembresEnum { get; } = [];

    /// <summary>Un objet peut etre genere pour ce type (ni interface, ni enum, ni abstrait, ni statique, ni generique).</summary>
    public bool EstGenerable =>
        Genre is not (GenreDeclaration.Interface or GenreDeclaration.Enum) && !EstAbstrait && !EstStatique && !EstGenerique;

    public string Emplacement => $"{Fichier}:{Ligne}";

    public override string ToString() => NomComplet;
}

/// <summary>Un membre de donnees : propriete publique, champ public ou parametre positionnel de record.</summary>
public sealed class DescripteurMembre
{
    public required string Nom { get; init; }

    public required ReferenceType Type { get; init; }

    public required CommentairesMembre Commentaires { get; init; }

    public required IReadOnlyList<AttributDeclare> Attributs { get; init; }

    /// <summary>Nom complet du type qui declare le membre.</summary>
    public required string TypeDeclarant { get; init; }

    /// <summary>Nom impose par [JsonPropertyName], le cas echeant.</summary>
    public string? NomJsonExplicite { get; init; }

    /// <summary>Modificateur C# required.</summary>
    public bool EstRequis { get; init; }

    public bool EstPositionnel { get; init; }

    public bool EstLectureSeule { get; init; }

    /// <summary>[JsonIgnore] : jamais present dans le JSON.</summary>
    public bool EstIgnore { get; init; }

    public int Ligne { get; init; }

    public string NomQualifie => $"{TypeDeclarant}.{Nom}";
}

/// <summary>Membre d'une enumeration et sa valeur effective (explicite ou sequentielle).</summary>
public sealed record MembreEnum(string Nom, long Valeur);

/// <summary>Attribut lu syntaxiquement : nom sans le suffixe Attribute, arguments positionnels et nommes evalues quand ils sont litteraux.</summary>
public sealed record AttributDeclare(string Nom, IReadOnlyList<object?> Positionnels, IReadOnlyDictionary<string, object?> Nommes);

/// <summary>Les differentes sources de commentaire rattachees a un membre.</summary>
public sealed record CommentairesMembre(string? Resume, string? Remarques, string? ParamDoc, string? FinDeLigne, string? LignesAuDessus)
{
    public static readonly CommentairesMembre Vide = new(null, null, null, null, null);

    public bool EstVide => Resume is null && Remarques is null && ParamDoc is null && FinDeLigne is null && LignesAuDessus is null;

    /// <summary>Sources non vides, avec leur etiquette pour le rapport.</summary>
    public IEnumerable<(string Source, string Texte)> Sources()
    {
        if (Resume is not null) yield return ("summary", Resume);
        if (Remarques is not null) yield return ("remarks", Remarques);
        if (ParamDoc is not null) yield return ("param", ParamDoc);
        if (LignesAuDessus is not null) yield return ("lignes au-dessus", LignesAuDessus);
        if (FinDeLigne is not null) yield return ("fin de ligne", FinDeLigne);
    }
}
