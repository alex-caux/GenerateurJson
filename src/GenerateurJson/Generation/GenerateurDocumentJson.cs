using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenerateurJson;

/// <summary>Assemble les documents JSON : objets (recursion bornee), collections, dictionnaires, nommage des proprietes.</summary>
public sealed class GenerateurDocumentJson
{
    private static readonly HashSet<string> Ensembles = new(StringComparer.Ordinal) { "HashSet", "ISet", "IReadOnlySet", "SortedSet", "ImmutableHashSet" };

    private readonly CatalogueTypes _catalogue;
    private readonly CalculateurContraintes _contraintes;
    private readonly ContexteGeneration _contexte;
    private readonly GenerateurValeurs _valeurs;
    private readonly GenerateurTexte _texte;

    public GenerateurDocumentJson(CatalogueTypes catalogue, OptionsGeneration options, CalculateurContraintes contraintes)
    {
        _catalogue = catalogue;
        _contraintes = contraintes;
        _contexte = new ContexteGeneration(options);
        _valeurs = new GenerateurValeurs(_contexte);
        _texte = new GenerateurTexte(_contexte.Aleatoire);
    }

    public IReadOnlyList<string> Avertissements => _contexte.Avertissements;

    public JsonNode? Generer(DescripteurType racine) => GenererObjet(racine);

    public JsonArray GenererPlusieurs(DescripteurType racine, int nombre)
    {
        var tableau = new JsonArray();
        for (var i = 0; i < nombre; i++)
        {
            tableau.Add(GenererObjet(racine));
        }

        return tableau;
    }

    private JsonNode? GenererObjet(DescripteurType type)
    {
        if (!type.EstGenerable)
        {
            var implementations = _catalogue.ImplementationsConcretes(type);
            if (implementations.Count == 0)
            {
                _contexte.Avertir($"{type.NomComplet} : aucune implementation concrete dans les sources, genere a null");
                return null;
            }

            if (implementations.Count > 1)
            {
                _contexte.Avertir($"{type.NomComplet} : plusieurs implementations concretes, {implementations[0].NomSimple} utilisee");
            }

            type = implementations[0];
        }

        if (_contexte.Profondeur >= _contexte.Options.ProfondeurMax)
        {
            _contexte.Avertir($"{type.NomComplet} : profondeur maximale ({_contexte.Options.ProfondeurMax}) atteinte, genere a null");
            return null;
        }

        _contexte.Entrer();
        try
        {
            var objet = new JsonObject();
            foreach (var membre in _catalogue.MembresEffectifs(type))
            {
                objet[NomJson(membre)] = GenererMembre(membre);
            }

            return objet;
        }
        finally
        {
            _contexte.Sortir();
        }
    }

    private JsonNode? GenererMembre(DescripteurMembre membre)
    {
        var contraintes = _contraintes.Pour(membre);
        var optionnel = contraintes.Presence is { Obligatoire: false } || (membre.Type.EstNullable && contraintes.Presence is null);
        if (optionnel && _contexte.Options.TauxNull > 0 && _contexte.Aleatoire.NextDouble() < _contexte.Options.TauxNull)
        {
            return null;
        }

        return GenererValeur(membre.Type, contraintes, membre.Nom, membre.NomQualifie);
    }

    private JsonNode? GenererValeur(ReferenceType type, JeuContraintes contraintes, string nomMembre, string cle)
    {
        switch (type.Genre)
        {
            case GenreValeur.Objet:
                return type.Declaration is null ? null : GenererObjet(type.Declaration);
            case GenreValeur.Collection:
                return GenererCollection(type, contraintes, nomMembre, cle);
            case GenreValeur.Dictionnaire:
                return GenererDictionnaire(type, contraintes, nomMembre, cle);
            case GenreValeur.Inconnu:
                _contexte.Avertir($"{cle} : type {type.TexteOriginal} inconnu, genere a null");
                return null;
            default:
                return _valeurs.Generer(type, contraintes, nomMembre, cle);
        }
    }

    private JsonArray GenererCollection(ReferenceType type, JeuContraintes contraintes, string nomMembre, string cle)
    {
        var tableau = new JsonArray();
        if (type.Element is null)
        {
            return tableau;
        }

        if (type.Element.Genre == GenreValeur.Objet && _contexte.Profondeur >= _contexte.Options.ProfondeurMax)
        {
            _contexte.Avertir($"{cle} : profondeur maximale atteinte, collection vide");
            return tableau;
        }

        var taille = Taille(contraintes.Taille, 1, 3);
        var distincts = contraintes.ElementsDistincts || Ensembles.Contains(type.Nom);
        var vus = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < taille; i++)
        {
            JsonNode? element = null;
            for (var essai = 0; essai < 10; essai++)
            {
                element = GenererValeur(type.Element, contraintes.Element, nomMembre, cle + "[]");
                if (!distincts || vus.Add(element?.ToJsonString() ?? "null"))
                {
                    break;
                }
            }

            tableau.Add(element);
        }

        return tableau;
    }

    private JsonObject GenererDictionnaire(ReferenceType type, JeuContraintes contraintes, string nomMembre, string cle)
    {
        var objet = new JsonObject();
        if (type.Cle is null || type.Element is null)
        {
            return objet;
        }

        var taille = Taille(contraintes.Taille, 2, 2);
        var cles = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < taille; i++)
        {
            var nomCle = GenererCle(type.Cle, i, cles, cle);
            if (nomCle is null)
            {
                break;
            }

            objet[nomCle] = GenererValeur(type.Element, contraintes.Element, nomMembre, cle + "{}");
        }

        return objet;
    }

    private string? GenererCle(ReferenceType typeCle, int indice, HashSet<string> deja, string cle)
    {
        switch (typeCle.Genre)
        {
            case GenreValeur.Texte:
                for (var essai = 0; essai < 20; essai++)
                {
                    var mot = _texte.Mot();
                    if (deja.Add(mot))
                    {
                        return mot;
                    }
                }

                return null;
            case GenreValeur.Entier:
                return (indice + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case GenreValeur.Enum:
            {
                var membres = typeCle.Declaration?.MembresEnum ?? [];
                return indice < membres.Count ? membres[indice].Nom : null;
            }

            case GenreValeur.Guid:
            {
                var octets = new byte[16];
                _contexte.Aleatoire.NextBytes(octets);
                return new Guid(octets).ToString("D");
            }

            default:
                _contexte.Avertir($"{cle} : cle de dictionnaire de type {typeCle.TexteOriginal} non supportee, dictionnaire vide");
                return null;
        }
    }

    private int Taille(TailleCollection? taille, int minParDefaut, int maxParDefaut)
    {
        var min = Math.Max(0, taille?.Min ?? minParDefaut);
        var max = taille?.Max ?? Math.Max(min, maxParDefaut);
        if (max < min)
        {
            max = min;
        }

        return _contexte.Aleatoire.Next(min, max + 1);
    }

    private string NomJson(DescripteurMembre membre)
    {
        if (membre.NomJsonExplicite is not null)
        {
            return membre.NomJsonExplicite;
        }

        return _contexte.Options.Nommage == ModeNommage.Camel ? JsonNamingPolicy.CamelCase.ConvertName(membre.Nom) : membre.Nom;
    }
}
