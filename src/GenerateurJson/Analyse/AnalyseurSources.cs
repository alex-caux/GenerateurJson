using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GenerateurJson;

/// <summary>
/// Analyse syntaxique (Roslyn, sans compilation) de fichiers .cs : types, membres de donnees, types des membres,
/// attributs et commentaires. Les noms de types utilisateur sont relies au catalogue dans une seconde passe.
/// </summary>
public static class AnalyseurSources
{
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly CSharpParseOptions OptionsAnalyse = new(LanguageVersion.Latest, DocumentationMode.Parse);

    private static readonly HashSet<string> Collections = new(StringComparer.Ordinal)
    {
        "List", "IList", "IReadOnlyList", "IEnumerable", "ICollection", "IReadOnlyCollection", "HashSet", "ISet",
        "IReadOnlySet", "Collection", "ObservableCollection", "ImmutableList", "ImmutableArray", "ImmutableHashSet",
        "Queue", "Stack", "LinkedList", "SortedSet", "IAsyncEnumerable",
    };

    private static readonly HashSet<string> Dictionnaires = new(StringComparer.Ordinal)
    {
        "Dictionary", "IDictionary", "IReadOnlyDictionary", "SortedDictionary", "ConcurrentDictionary",
        "ImmutableDictionary", "SortedList",
    };

    private static readonly Dictionary<string, (GenreValeur Genre, string Nom)> Primitifs = new(StringComparer.Ordinal)
    {
        ["int"] = (GenreValeur.Entier, "int"), ["Int32"] = (GenreValeur.Entier, "int"),
        ["long"] = (GenreValeur.Entier, "long"), ["Int64"] = (GenreValeur.Entier, "long"),
        ["short"] = (GenreValeur.Entier, "short"), ["Int16"] = (GenreValeur.Entier, "short"),
        ["byte"] = (GenreValeur.Entier, "byte"), ["Byte"] = (GenreValeur.Entier, "byte"),
        ["sbyte"] = (GenreValeur.Entier, "sbyte"), ["SByte"] = (GenreValeur.Entier, "sbyte"),
        ["uint"] = (GenreValeur.Entier, "uint"), ["UInt32"] = (GenreValeur.Entier, "uint"),
        ["ulong"] = (GenreValeur.Entier, "ulong"), ["UInt64"] = (GenreValeur.Entier, "ulong"),
        ["ushort"] = (GenreValeur.Entier, "ushort"), ["UInt16"] = (GenreValeur.Entier, "ushort"),
        ["float"] = (GenreValeur.Reel, "float"), ["Single"] = (GenreValeur.Reel, "float"),
        ["double"] = (GenreValeur.Reel, "double"), ["Double"] = (GenreValeur.Reel, "double"),
        ["decimal"] = (GenreValeur.Reel, "decimal"), ["Decimal"] = (GenreValeur.Reel, "decimal"),
        ["bool"] = (GenreValeur.Booleen, "bool"), ["Boolean"] = (GenreValeur.Booleen, "bool"),
        ["string"] = (GenreValeur.Texte, "string"), ["String"] = (GenreValeur.Texte, "string"),
        ["char"] = (GenreValeur.Caractere, "char"), ["Char"] = (GenreValeur.Caractere, "char"),
        ["Guid"] = (GenreValeur.Guid, "Guid"),
        ["DateTime"] = (GenreValeur.DateHeure, "DateTime"),
        ["DateTimeOffset"] = (GenreValeur.DateHeure, "DateTimeOffset"),
        ["DateOnly"] = (GenreValeur.DateSeule, "DateOnly"),
        ["TimeOnly"] = (GenreValeur.HeureSeule, "TimeOnly"),
        ["TimeSpan"] = (GenreValeur.Duree, "TimeSpan"),
        ["Uri"] = (GenreValeur.Uri, "Uri"),
        ["object"] = (GenreValeur.Inconnu, "object"), ["Object"] = (GenreValeur.Inconnu, "object"),
        ["dynamic"] = (GenreValeur.Inconnu, "dynamic"),
        ["JsonElement"] = (GenreValeur.Inconnu, "JsonElement"), ["JsonNode"] = (GenreValeur.Inconnu, "JsonNode"),
        ["JsonObject"] = (GenreValeur.Inconnu, "JsonObject"), ["JsonArray"] = (GenreValeur.Inconnu, "JsonArray"),
        ["JsonDocument"] = (GenreValeur.Inconnu, "JsonDocument"),
        ["Type"] = (GenreValeur.Inconnu, "Type"), ["BigInteger"] = (GenreValeur.Inconnu, "BigInteger"),
        ["Half"] = (GenreValeur.Inconnu, "Half"), ["nint"] = (GenreValeur.Inconnu, "nint"), ["nuint"] = (GenreValeur.Inconnu, "nuint"),
    };

    /// <summary>Analyse des fichiers .cs ou des dossiers (parcours recursif, bin/ et obj/ ignores).</summary>
    public static CatalogueTypes Analyser(IEnumerable<string> chemins)
    {
        var fichiers = new List<string>();
        foreach (var chemin in chemins)
        {
            if (Directory.Exists(chemin))
            {
                fichiers.AddRange(Directory
                    .EnumerateFiles(chemin, "*.cs", SearchOption.AllDirectories)
                    .Where(EstFichierSource)
                    .OrderBy(f => f, StringComparer.Ordinal));
            }
            else if (File.Exists(chemin))
            {
                fichiers.Add(chemin);
            }
            else
            {
                throw new ErreurAnalyse($"source introuvable : {chemin}");
            }
        }

        if (fichiers.Count == 0)
        {
            throw new ErreurAnalyse("aucun fichier .cs trouve dans les sources indiquees");
        }

        return AnalyserTextes(fichiers.Select(f => (f, LireTexte(f))));
    }

    public static CatalogueTypes AnalyserTexte(string code, string nom = "memoire.cs") => AnalyserTextes([(nom, code)]);

    public static CatalogueTypes AnalyserTextes(IEnumerable<(string Nom, string Code)> sources)
    {
        var avertissements = new List<string>();
        var types = new List<DescripteurType>();
        var parNom = new Dictionary<string, DescripteurType>(StringComparer.Ordinal);

        foreach (var (nom, code) in sources)
        {
            var arbre = CSharpSyntaxTree.ParseText(code, OptionsAnalyse, path: nom);
            foreach (var diagnostic in arbre.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(5))
            {
                var ligne = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                avertissements.Add($"{nom}:{ligne} : erreur de syntaxe ignoree : {diagnostic.GetMessage()}");
            }

            foreach (var declaration in arbre.GetCompilationUnitRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (declaration.Identifier.IsMissing)
                {
                    continue;
                }

                var descripteur = AnalyserDeclaration(declaration, nom, avertissements);
                if (parNom.TryGetValue(descripteur.NomComplet, out var existant))
                {
                    if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || existant.Genre == descripteur.Genre)
                    {
                        Fusionner(existant, descripteur);
                    }
                    else
                    {
                        avertissements.Add($"type {descripteur.NomComplet} declare plusieurs fois avec des natures differentes : seule la premiere declaration est conservee");
                    }
                }
                else
                {
                    parNom[descripteur.NomComplet] = descripteur;
                    types.Add(descripteur);
                }
            }
        }

        var catalogue = new CatalogueTypes(types, avertissements);
        Relier(catalogue, avertissements);
        return catalogue;
    }

    // ----- Fichiers -----

    private static bool EstFichierSource(string chemin)
    {
        var nom = Path.GetFileName(chemin);
        var segments = chemin.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(s => s is "bin" or "obj") &&
               !nom.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) &&
               !nom.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) &&
               !nom.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string LireTexte(string fichier)
    {
        var octets = File.ReadAllBytes(fichier);
        string texte;
        try
        {
            texte = Utf8Strict.GetString(octets);
        }
        catch (DecoderFallbackException)
        {
            // Fichier Windows-1252 (accents non UTF-8) : Latin1 couvre e accentue, a grave, c cedille...
            texte = Encoding.Latin1.GetString(octets);
        }

        // Marque d'ordre d'octets (U+FEFF) eventuelle en tete de fichier.
        return texte.Length > 0 && texte[0] == (char)0xFEFF ? texte[1..] : texte;
    }

    // ----- Declarations -----

    private static DescripteurType AnalyserDeclaration(BaseTypeDeclarationSyntax declaration, string fichier, List<string> avertissements)
    {
        var espaceNoms = string.Join(".", declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
        var englobants = declaration.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.ValueText);
        var nomSimple = declaration.Identifier.ValueText;
        var segments = new List<string>();
        if (espaceNoms.Length > 0)
        {
            segments.Add(espaceNoms);
        }

        segments.AddRange(englobants);
        segments.Add(nomSimple);

        var attributs = LecteurAttributs.Lire(declaration.AttributeLists);
        var genre = declaration switch
        {
            EnumDeclarationSyntax => GenreDeclaration.Enum,
            InterfaceDeclarationSyntax => GenreDeclaration.Interface,
            StructDeclarationSyntax => GenreDeclaration.Struct,
            RecordDeclarationSyntax record => record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? GenreDeclaration.RecordStruct : GenreDeclaration.Record,
            _ => GenreDeclaration.Classe,
        };

        var type = new DescripteurType
        {
            NomComplet = string.Join(".", segments),
            NomSimple = nomSimple,
            EspaceNoms = espaceNoms,
            Genre = genre,
            Fichier = fichier,
            Ligne = LigneDe(declaration),
            EstAbstrait = declaration.Modifiers.Any(SyntaxKind.AbstractKeyword),
            EstStatique = declaration.Modifiers.Any(SyntaxKind.StaticKeyword),
            EstGenerique = declaration is TypeDeclarationSyntax { TypeParameterList: not null },
            EstFlags = attributs.Any(a => a.Nom == "Flags"),
            Resume = ExtracteurCommentaires.Resume(declaration),
        };

        if (declaration.BaseList is not null)
        {
            foreach (var baseType in declaration.BaseList.Types)
            {
                type.TypesDeBase.Add(baseType.Type.ToString());
            }
        }

        if (type.EstGenerique)
        {
            avertissements.Add($"type generique {type.NomComplet} non supporte : il ne sera jamais genere");
        }

        switch (declaration)
        {
            case EnumDeclarationSyntax enumeration:
                AnalyserEnum(enumeration, type, avertissements);
                break;
            case TypeDeclarationSyntax typeDeclare when genre != GenreDeclaration.Interface:
                AnalyserMembres(typeDeclare, type);
                break;
        }

        return type;
    }

    private static void Fusionner(DescripteurType existant, DescripteurType complement)
    {
        existant.Membres.AddRange(complement.Membres);
        existant.MembresEnum.AddRange(complement.MembresEnum);
        foreach (var baseType in complement.TypesDeBase)
        {
            if (!existant.TypesDeBase.Contains(baseType))
            {
                existant.TypesDeBase.Add(baseType);
            }
        }

        existant.Resume ??= complement.Resume;
    }

    private static void AnalyserEnum(EnumDeclarationSyntax enumeration, DescripteurType type, List<string> avertissements)
    {
        long suivant = 0;
        foreach (var membre in enumeration.Members)
        {
            long? explicite = membre.EqualsValue is null ? null : EvaluerEntier(membre.EqualsValue.Value);
            if (membre.EqualsValue is not null && explicite is null)
            {
                avertissements.Add($"{type.NomComplet}.{membre.Identifier.ValueText} : valeur non evaluable, numerotation sequentielle utilisee");
            }

            var valeur = explicite ?? suivant;
            type.MembresEnum.Add(new MembreEnum(membre.Identifier.ValueText, valeur));
            suivant = valeur + 1;
        }
    }

    private static long? EvaluerEntier(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax litteral:
                try
                {
                    return litteral.Token.Value is null ? null : Convert.ToInt64(litteral.Token.Value, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (e is OverflowException or InvalidCastException or FormatException)
                {
                    return null;
                }

            case PrefixUnaryExpressionSyntax moins when moins.IsKind(SyntaxKind.UnaryMinusExpression):
                return -EvaluerEntier(moins.Operand);
            case ParenthesizedExpressionSyntax parenthese:
                return EvaluerEntier(parenthese.Expression);
            case CastExpressionSyntax cast:
                return EvaluerEntier(cast.Expression);
            case BinaryExpressionSyntax binaire:
            {
                var gauche = EvaluerEntier(binaire.Left);
                var droite = EvaluerEntier(binaire.Right);
                if (gauche is null || droite is null)
                {
                    return null;
                }

                if (binaire.IsKind(SyntaxKind.LeftShiftExpression))
                {
                    return gauche.Value << (int)droite.Value;
                }

                if (binaire.IsKind(SyntaxKind.BitwiseOrExpression))
                {
                    return gauche.Value | droite.Value;
                }

                if (binaire.IsKind(SyntaxKind.AddExpression))
                {
                    return gauche.Value + droite.Value;
                }

                if (binaire.IsKind(SyntaxKind.MultiplyExpression))
                {
                    return gauche.Value * droite.Value;
                }

                return null;
            }

            default:
                return null;
        }
    }

    private static void AnalyserMembres(TypeDeclarationSyntax declaration, DescripteurType type)
    {
        // Parametres positionnels d'un record (ceux d'un constructeur primaire de classe ne sont pas des proprietes).
        if (declaration is RecordDeclarationSyntax { ParameterList: not null } record)
        {
            var docParams = ExtracteurCommentaires.DocParams(record);
            foreach (var parametre in record.ParameterList.Parameters)
            {
                if (parametre.Type is null || parametre.Identifier.IsMissing)
                {
                    continue;
                }

                var attributs = LecteurAttributs.Lire(parametre.AttributeLists);
                type.Membres.Add(new DescripteurMembre
                {
                    Nom = parametre.Identifier.ValueText,
                    Type = ResoudreType(parametre.Type),
                    Commentaires = ExtracteurCommentaires.ExtrairePourParametre(parametre, docParams),
                    Attributs = attributs,
                    TypeDeclarant = type.NomComplet,
                    NomJsonExplicite = LecteurAttributs.NomJson(attributs),
                    EstPositionnel = true,
                    EstIgnore = LecteurAttributs.EstIgnore(attributs),
                    Ligne = LigneDe(parametre),
                });
            }
        }

        foreach (var membre in declaration.Members)
        {
            switch (membre)
            {
                case PropertyDeclarationSyntax propriete when EstPublicInstance(propriete.Modifiers) && EstAutoPropriete(propriete):
                {
                    var attributs = LecteurAttributs.Lire(propriete.AttributeLists);
                    type.Membres.Add(new DescripteurMembre
                    {
                        Nom = propriete.Identifier.ValueText,
                        Type = ResoudreType(propriete.Type),
                        Commentaires = ExtracteurCommentaires.Extraire(propriete),
                        Attributs = attributs,
                        TypeDeclarant = type.NomComplet,
                        NomJsonExplicite = LecteurAttributs.NomJson(attributs),
                        EstRequis = propriete.Modifiers.Any(SyntaxKind.RequiredKeyword),
                        EstLectureSeule = !AUnAccesseurEcriture(propriete),
                        EstIgnore = LecteurAttributs.EstIgnore(attributs),
                        Ligne = LigneDe(propriete),
                    });
                    break;
                }

                case FieldDeclarationSyntax champ when EstPublicInstance(champ.Modifiers) && !champ.Modifiers.Any(SyntaxKind.ConstKeyword):
                {
                    var attributs = LecteurAttributs.Lire(champ.AttributeLists);
                    var commentaires = ExtracteurCommentaires.Extraire(champ);
                    foreach (var variable in champ.Declaration.Variables)
                    {
                        type.Membres.Add(new DescripteurMembre
                        {
                            Nom = variable.Identifier.ValueText,
                            Type = ResoudreType(champ.Declaration.Type),
                            Commentaires = commentaires,
                            Attributs = attributs,
                            TypeDeclarant = type.NomComplet,
                            NomJsonExplicite = LecteurAttributs.NomJson(attributs),
                            EstRequis = champ.Modifiers.Any(SyntaxKind.RequiredKeyword),
                            EstLectureSeule = champ.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
                            EstIgnore = LecteurAttributs.EstIgnore(attributs),
                            Ligne = LigneDe(variable),
                        });
                    }

                    break;
                }
            }
        }
    }

    private static bool EstPublicInstance(SyntaxTokenList modificateurs) =>
        modificateurs.Any(SyntaxKind.PublicKeyword) && !modificateurs.Any(SyntaxKind.StaticKeyword);

    /// <summary>Propriete automatique (accesseurs sans corps) : une propriete calculee n'est pas une donnee.</summary>
    private static bool EstAutoPropriete(PropertyDeclarationSyntax propriete) =>
        propriete.ExpressionBody is null &&
        propriete.AccessorList is not null &&
        propriete.AccessorList.Accessors.All(a => a.Body is null && a.ExpressionBody is null);

    private static bool AUnAccesseurEcriture(PropertyDeclarationSyntax propriete) =>
        propriete.AccessorList is not null &&
        propriete.AccessorList.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration));

    private static int LigneDe(SyntaxNode noeud) => noeud.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    // ----- Types -----

    public static ReferenceType ResoudreType(TypeSyntax syntaxe)
    {
        var texte = syntaxe.ToString();
        switch (syntaxe)
        {
            case NullableTypeSyntax nullable:
                return ResoudreType(nullable.ElementType).AvecNullable(texte);

            case PredefinedTypeSyntax predefini:
                return ParNom(predefini.Keyword.ValueText, texte);

            case ArrayTypeSyntax tableau:
            {
                if (tableau.RankSpecifiers.Any(r => r.Rank > 1))
                {
                    return Inconnu("tableau multidimensionnel", texte);
                }

                var element = ResoudreType(tableau.ElementType);
                for (var i = tableau.RankSpecifiers.Count - 1; i >= 0; i--)
                {
                    element = element is { Genre: GenreValeur.Entier, Nom: "byte", EstNullable: false }
                        ? new ReferenceType { Genre = GenreValeur.Octets, Nom = "byte[]", TexteOriginal = texte }
                        : new ReferenceType { Genre = GenreValeur.Collection, Nom = element.Nom + "[]", Element = element, TexteOriginal = texte };
                }

                return element;
            }

            case GenericNameSyntax generique:
            {
                var nom = generique.Identifier.ValueText;
                var arguments = generique.TypeArgumentList.Arguments;
                if (nom == "Nullable" && arguments.Count == 1)
                {
                    return ResoudreType(arguments[0]).AvecNullable(texte);
                }

                if (Collections.Contains(nom) && arguments.Count == 1)
                {
                    return new ReferenceType { Genre = GenreValeur.Collection, Nom = nom, Element = ResoudreType(arguments[0]), TexteOriginal = texte };
                }

                if (Dictionnaires.Contains(nom) && arguments.Count == 2)
                {
                    return new ReferenceType
                    {
                        Genre = GenreValeur.Dictionnaire,
                        Nom = nom,
                        Cle = ResoudreType(arguments[0]),
                        Element = ResoudreType(arguments[1]),
                        TexteOriginal = texte,
                    };
                }

                return Inconnu($"type generique {texte}", texte);
            }

            case IdentifierNameSyntax identifiant:
                return ParNom(identifiant.Identifier.ValueText, texte);

            case QualifiedNameSyntax qualifie:
                return ResoudreType(qualifie.Right);

            case AliasQualifiedNameSyntax alias:
                return ResoudreType(alias.Name);

            case TupleTypeSyntax:
                return Inconnu("tuple", texte);

            default:
                return Inconnu(texte, texte);
        }
    }

    private static ReferenceType ParNom(string nom, string texte)
    {
        if (Primitifs.TryGetValue(nom, out var primitif))
        {
            return new ReferenceType { Genre = primitif.Genre, Nom = primitif.Nom, TexteOriginal = texte };
        }

        return new ReferenceType { Genre = GenreValeur.Inconnu, Nom = nom, TexteOriginal = texte, AResoudre = true };
    }

    private static ReferenceType Inconnu(string nom, string texte) =>
        new() { Genre = GenreValeur.Inconnu, Nom = nom, TexteOriginal = texte };

    // ----- Seconde passe : relier les noms de types utilisateur au catalogue -----

    private static void Relier(CatalogueTypes catalogue, List<string> avertissements)
    {
        var signales = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in catalogue.Types)
        {
            foreach (var membre in type.Membres)
            {
                Relier(membre.Type, catalogue, avertissements, signales);
            }
        }
    }

    private static void Relier(ReferenceType reference, CatalogueTypes catalogue, List<string> avertissements, HashSet<string> signales)
    {
        if (reference.Genre == GenreValeur.Inconnu)
        {
            if (reference.AResoudre)
            {
                DescripteurType? declaration = null;
                try
                {
                    declaration = catalogue.Resoudre(reference.Nom);
                }
                catch (ErreurAnalyse erreur)
                {
                    if (signales.Add(reference.Nom))
                    {
                        avertissements.Add(erreur.Message);
                    }
                }

                if (declaration is null)
                {
                    if (signales.Add(reference.Nom))
                    {
                        avertissements.Add($"type {reference.Nom} inconnu (absent des sources fournies) : genere a null");
                    }
                }
                else if (declaration.EstGenerique)
                {
                    reference.Declaration = declaration;
                }
                else
                {
                    reference.Declaration = declaration;
                    reference.Genre = declaration.Genre == GenreDeclaration.Enum ? GenreValeur.Enum : GenreValeur.Objet;
                }
            }
            else if (signales.Add(reference.Nom))
            {
                avertissements.Add($"type {reference.Nom} non supporte : genere a null");
            }
        }

        if (reference.Element is not null)
        {
            Relier(reference.Element, catalogue, avertissements, signales);
        }

        if (reference.Cle is not null)
        {
            Relier(reference.Cle, catalogue, avertissements, signales);
        }
    }
}
