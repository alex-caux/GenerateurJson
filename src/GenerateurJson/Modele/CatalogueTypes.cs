namespace GenerateurJson;

/// <summary>Erreur d'analyse des sources (fichier absent, aucun type, nom ambigu...).</summary>
public sealed class ErreurAnalyse(string message) : Exception(message);

/// <summary>Ensemble des types trouves dans les sources, avec la resolution des noms et de l'heritage.</summary>
public sealed class CatalogueTypes
{
    private readonly Dictionary<DescripteurType, IReadOnlyList<DescripteurMembre>> _membresEffectifs = [];
    private readonly Dictionary<string, DescripteurType> _parNomComplet;

    public CatalogueTypes(IReadOnlyList<DescripteurType> types, IReadOnlyList<string> avertissements)
    {
        Types = types;
        Avertissements = avertissements;
        _parNomComplet = new Dictionary<string, DescripteurType>(StringComparer.Ordinal);
        foreach (var t in types)
        {
            _parNomComplet.TryAdd(t.NomComplet, t);
        }
    }

    /// <summary>Types dans l'ordre de declaration, declarations partielles fusionnees.</summary>
    public IReadOnlyList<DescripteurType> Types { get; }

    public IReadOnlyList<string> Avertissements { get; }

    /// <summary>
    /// Resout un nom simple (Bobine), imbrique (Outer.Inner) ou qualifie (Ns.Bobine).
    /// Null si absent ; <see cref="ErreurAnalyse"/> si plusieurs types correspondent.
    /// </summary>
    public DescripteurType? Resoudre(string nom)
    {
        if (_parNomComplet.TryGetValue(nom, out var exact))
        {
            return exact;
        }

        var suffixe = "." + nom;
        var candidats = Types
            .Where(t => t.NomSimple == nom || t.NomComplet.EndsWith(suffixe, StringComparison.Ordinal))
            .ToList();
        return candidats.Count switch
        {
            0 => null,
            1 => candidats[0],
            _ => throw new ErreurAnalyse(
                $"le nom « {nom} » est ambigu : {string.Join(", ", candidats.Select(c => c.NomComplet))}. " +
                "Precisez-le (espace de noms ou type englobant)."),
        };
    }

    /// <summary>Resolution tolerante d'un type de base : nom simple sans arguments generiques, null si absent ou ambigu.</summary>
    public DescripteurType? ResoudreBase(string nomBase)
    {
        var nom = nomBase;
        var chevron = nom.IndexOf('<');
        if (chevron >= 0)
        {
            nom = nom[..chevron];
        }

        nom = nom.Trim();
        try
        {
            return Resoudre(nom);
        }
        catch (ErreurAnalyse)
        {
            return null;
        }
    }

    /// <summary>
    /// Membres a generer pour un type : ceux des classes de base d'abord (recursivement), puis les siens ;
    /// un nom redeclare dans le type derive masque celui de la base ; les membres [JsonIgnore] sont retires.
    /// </summary>
    public IReadOnlyList<DescripteurMembre> MembresEffectifs(DescripteurType type)
    {
        if (_membresEffectifs.TryGetValue(type, out var deja))
        {
            return deja;
        }

        var accumulateur = new List<DescripteurMembre>();
        Collecter(type, accumulateur, [type]);
        var liste = accumulateur.Where(m => !m.EstIgnore).ToList();
        _membresEffectifs[type] = liste;
        return liste;
    }

    private void Collecter(DescripteurType type, List<DescripteurMembre> accumulateur, HashSet<DescripteurType> vus)
    {
        foreach (var nomBase in type.TypesDeBase)
        {
            var baseType = ResoudreBase(nomBase);
            if (baseType is null || baseType.Genre is GenreDeclaration.Interface or GenreDeclaration.Enum || !vus.Add(baseType))
            {
                continue;
            }

            Collecter(baseType, accumulateur, vus);
        }

        foreach (var membre in type.Membres)
        {
            accumulateur.RemoveAll(m => m.Nom == membre.Nom);
            accumulateur.Add(membre);
        }
    }

    /// <summary>
    /// Candidats naturels au type racine : types generables ayant au moins un membre, qu'aucun membre d'un autre
    /// type ne reference (une auto-reference, comme un noeud d'arbre, ne compte pas).
    /// </summary>
    public IReadOnlyList<DescripteurType> Racines()
    {
        var references = new HashSet<DescripteurType>();
        foreach (var type in Types)
        {
            foreach (var membre in type.Membres)
            {
                foreach (var declaration in Declarations(membre.Type))
                {
                    if (declaration != type)
                    {
                        references.Add(declaration);
                    }
                }
            }
        }

        return Types.Where(t => t.EstGenerable && !references.Contains(t) && MembresEffectifs(t).Count > 0).ToList();
    }

    /// <summary>Types generables qui derivent (directement ou non) du type abstrait ou de l'interface donnes.</summary>
    public IReadOnlyList<DescripteurType> ImplementationsConcretes(DescripteurType abstrait) =>
        Types.Where(t => t.EstGenerable && t != abstrait && Derive(t, abstrait, [])).ToList();

    private bool Derive(DescripteurType type, DescripteurType cible, HashSet<DescripteurType> vus)
    {
        foreach (var nomBase in type.TypesDeBase)
        {
            var baseType = ResoudreBase(nomBase);
            if (baseType is null || !vus.Add(baseType))
            {
                continue;
            }

            if (baseType == cible || Derive(baseType, cible, vus))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tous les types (objets, enums, implementations concretes) atteignables depuis une racine, racine comprise.</summary>
    public IReadOnlySet<DescripteurType> TypesAtteignables(DescripteurType racine)
    {
        var vus = new HashSet<DescripteurType>();
        var pile = new Stack<DescripteurType>();
        pile.Push(racine);
        while (pile.Count > 0)
        {
            var type = pile.Pop();
            if (!vus.Add(type))
            {
                continue;
            }

            if (type.Genre is GenreDeclaration.Enum)
            {
                continue;
            }

            foreach (var membre in MembresEffectifs(type))
            {
                foreach (var declaration in Declarations(membre.Type))
                {
                    if (declaration.Genre is GenreDeclaration.Interface || declaration.EstAbstrait)
                    {
                        foreach (var implementation in ImplementationsConcretes(declaration))
                        {
                            pile.Push(implementation);
                        }
                    }

                    pile.Push(declaration);
                }
            }
        }

        return vus;
    }

    private static IEnumerable<DescripteurType> Declarations(ReferenceType reference)
    {
        if (reference.Declaration is not null)
        {
            yield return reference.Declaration;
        }

        if (reference.Element is not null)
        {
            foreach (var d in Declarations(reference.Element))
            {
                yield return d;
            }
        }

        if (reference.Cle is not null)
        {
            foreach (var d in Declarations(reference.Cle))
            {
                yield return d;
            }
        }
    }
}
