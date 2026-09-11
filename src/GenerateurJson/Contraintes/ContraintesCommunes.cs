using System.Globalization;

namespace GenerateurJson;

/// <summary>
/// Ce que les deux interpreteurs (regles et LLM) partagent : contraintes issues des attributs, du modificateur
/// required et du type nullable, routage vers le membre ou vers les elements d'une collection, filtrage des
/// valeurs autorisees selon le type.
/// </summary>
public static class ContraintesCommunes
{
    public static void AjouterAttributsEtType(DescripteurMembre membre, JeuContraintes jeu)
    {
        foreach (var attribut in membre.Attributs)
        {
            foreach (var contrainte in DepuisAttribut(attribut, membre.Type, jeu.Avertissements))
            {
                AjouterAvecCible(jeu, membre.Type, new ResultatRegle(contrainte));
            }
        }

        if (membre.EstRequis)
        {
            jeu.Ajouter(new Presence(true) { Origine = OrigineContrainte.Modificateur, Source = "required" });
        }

        if (membre.Type.EstNullable)
        {
            jeu.Ajouter(new Presence(false) { Origine = OrigineContrainte.TypeCSharp, Source = membre.Type.TexteOriginal });
        }
    }

    /// <summary>
    /// Sur une collection ou un dictionnaire, les contraintes de valeur portent sur les elements ; la presence, la
    /// taille, la distinction des elements et les notes portent sur la collection elle-meme.
    /// </summary>
    public static void AjouterAvecCible(JeuContraintes jeu, ReferenceType type, ResultatRegle resultat)
    {
        var cible = resultat.Cible;
        if (cible == CibleContrainte.Auto)
        {
            cible = type.EstConteneur && resultat.Contrainte is not (Presence or TailleCollection or ElementsDistincts or Note)
                ? CibleContrainte.Element
                : CibleContrainte.Membre;
        }

        if (cible == CibleContrainte.Element && resultat.Contrainte is Unicite unicite)
        {
            jeu.Ajouter(new ElementsDistincts { Origine = unicite.Origine, Source = unicite.Source });
            return;
        }

        (cible == CibleContrainte.Element ? jeu.Element : jeu).Ajouter(resultat.Contrainte);
    }

    /// <summary>Attributs DataAnnotations et System.Text.Json reconnus. Les autres sont ignores silencieusement.</summary>
    public static IEnumerable<Contrainte> DepuisAttribut(AttributDeclare attribut, ReferenceType type, List<string> avertissements)
    {
        var p = attribut.Positionnels;
        var origine = OrigineContrainte.Attribut;
        var source = attribut.Nom;
        var cible = type.TypeCible;
        var estTexte = cible.Genre is GenreValeur.Texte or GenreValeur.Caractere;

        switch (attribut.Nom)
        {
            case "Range":
                if (p.Count >= 3 && p[1] is string debut && p[2] is string fin)
                {
                    var dateMin = NormaliseurTexte.Date(debut);
                    var dateMax = NormaliseurTexte.Date(fin);
                    if (dateMin is not null || dateMax is not null)
                    {
                        yield return new PlageDates(dateMin, dateMax) { Origine = origine, Source = source };
                    }
                    else
                    {
                        yield return new Plage(NormaliseurTexte.Nombre(debut), NormaliseurTexte.Nombre(fin)) { Origine = origine, Source = source };
                    }
                }
                else if (p.Count >= 2)
                {
                    yield return new Plage(EnDecimal(p[0]), EnDecimal(p[1])) { Origine = origine, Source = source };
                }

                break;

            case "MinLength":
                if (EnEntier(p.ElementAtOrDefault(0)) is { } minLongueur)
                {
                    yield return Longueur(type, minLongueur, null, origine, source);
                }

                break;

            case "MaxLength":
                if (EnEntier(p.ElementAtOrDefault(0)) is { } maxLongueur)
                {
                    yield return Longueur(type, null, maxLongueur, origine, source);
                }

                break;

            case "Length":
                if (p.Count >= 2)
                {
                    yield return Longueur(type, EnEntier(p[0]), EnEntier(p[1]), origine, source);
                }

                break;

            case "StringLength":
            {
                attribut.Nommes.TryGetValue("MinimumLength", out var minimum);
                yield return new LongueurTexte(EnEntier(minimum), EnEntier(p.ElementAtOrDefault(0))) { Origine = origine, Source = source };
                break;
            }

            case "Required":
            case "JsonRequired":
                yield return new Presence(true) { Origine = origine, Source = source };
                break;

            case "RegularExpression":
                if (p.ElementAtOrDefault(0) is string motif && motif.Length > 0)
                {
                    yield return new Motif(motif) { Origine = origine, Source = source };
                }

                break;

            case "EmailAddress":
                yield return new Format(GenreFormat.Email) { Origine = origine, Source = source };
                break;

            case "Url":
                yield return new Format(GenreFormat.Url) { Origine = origine, Source = source };
                break;

            case "Phone":
                yield return new Format(GenreFormat.Telephone) { Origine = origine, Source = source };
                break;

            case "AllowedValues":
            case "DeniedValues":
            {
                var valeurs = FiltrerValeurs(p.Select(EnTexte).ToList(), cible, avertissements, "[" + attribut.Nom + "]");
                if (valeurs.Count > 0)
                {
                    yield return attribut.Nom == "AllowedValues"
                        ? new ValeursAutorisees(valeurs) { Origine = origine, Source = source }
                        : new ValeursExclues(valeurs) { Origine = origine, Source = source };
                }

                break;
            }

            case "DefaultValue":
                if (p.Count >= 1)
                {
                    yield return new ValeurFixe(EnTexte(p[^1]), ParDefaut: true) { Origine = origine, Source = source };
                }

                break;

            case "Key":
                yield return new Unicite(Sequentiel: true) { Origine = origine, Source = source };
                break;

            case "DataType":
            {
                var genre = (p.ElementAtOrDefault(0) as string) switch
                {
                    "EmailAddress" => GenreFormat.Email,
                    "Url" or "ImageUrl" => GenreFormat.Url,
                    "PhoneNumber" => GenreFormat.Telephone,
                    "PostalCode" => GenreFormat.CodePostal,
                    "Date" => GenreFormat.DateSeule,
                    _ => (GenreFormat?)null,
                };
                if (genre is not null)
                {
                    yield return new Format(genre.Value) { Origine = origine, Source = source };
                }

                break;
            }

            case "Obsolete":
                yield return new Note("membre obsolete") { Origine = origine, Source = source };
                break;
        }

        static Contrainte Longueur(ReferenceType type, int? min, int? max, OrigineContrainte origine, string source) =>
            type.EstConteneur
                ? new TailleCollection(min, max) { Origine = origine, Source = source }
                : new LongueurTexte(min, max) { Origine = origine, Source = source };
    }

    /// <summary>
    /// Adapte une liste de valeurs au type cible : nombres reconnus pour un numerique, noms de membres pour un enum
    /// (avertissement si inconnu), rien pour un booleen, texte tel quel sinon.
    /// </summary>
    public static IReadOnlyList<string> FiltrerValeurs(IReadOnlyList<string> valeurs, ReferenceType cible, List<string> avertissements, string contexte)
    {
        var resultat = new List<string>();
        switch (cible.Genre)
        {
            case GenreValeur.Entier:
            case GenreValeur.Reel:
                foreach (var valeur in valeurs)
                {
                    var nombre = NormaliseurTexte.Nombre(valeur);
                    if (nombre is null)
                    {
                        avertissements.Add($"valeur « {valeur} » ignoree : ce n'est pas un nombre ({contexte})");
                    }
                    else
                    {
                        resultat.Add(NormaliseurTexte.FormaterNombre(nombre.Value));
                    }
                }

                break;

            case GenreValeur.Enum:
            {
                var membres = cible.Declaration?.MembresEnum ?? [];
                foreach (var valeur in valeurs)
                {
                    var membre = membres.FirstOrDefault(m => string.Equals(m.Nom, valeur, StringComparison.OrdinalIgnoreCase))
                                 ?? membres.FirstOrDefault(m => m.Valeur.ToString(CultureInfo.InvariantCulture) == valeur);
                    if (membre is null)
                    {
                        avertissements.Add($"valeur « {valeur} » inconnue pour l'enum {cible.Declaration?.NomSimple ?? cible.Nom} ({contexte})");
                    }
                    else
                    {
                        resultat.Add(membre.Nom);
                    }
                }

                break;
            }

            case GenreValeur.Booleen:
                break;

            default:
                resultat.AddRange(valeurs);
                break;
        }

        return resultat.Distinct(StringComparer.Ordinal).ToList();
    }

    private static decimal? EnDecimal(object? valeur)
    {
        switch (valeur)
        {
            case null:
                return null;
            case string texte:
                return NormaliseurTexte.Nombre(texte);
            default:
                try
                {
                    return Convert.ToDecimal(valeur, CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
                {
                    return null;
                }
        }
    }

    private static int? EnEntier(object? valeur) => NormaliseurTexte.Entier(EnDecimal(valeur));

    private static string EnTexte(object? valeur) => valeur switch
    {
        null => "null",
        string texte => texte,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => valeur.ToString() ?? string.Empty,
    };
}
