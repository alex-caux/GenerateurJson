using System.Text.RegularExpressions;

namespace GenerateurJson;

/// <summary>
/// Indices tires du nom du membre (Email, PrixHt, CodePostal...), ajoutes avec la priorite la plus basse : ils ne
/// s'appliquent que si aucune contrainte de la meme famille n'a ete trouvee ailleurs.
/// </summary>
public static class IndicesNom
{
    private static readonly Regex Coupures = new(@"[_\-\s]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Mots(string nomMembre) =>
        Coupures.Split(NormaliseurTexte.NormaliserIsometrique(nomMembre))
            .Where(m => m.Length > 0)
            .Select(m => m.ToLowerInvariant())
            .ToList();

    public static void Appliquer(string nomMembre, ReferenceType type, JeuContraintes jeu)
    {
        var mots = Mots(nomMembre);
        if (mots.Count == 0)
        {
            return;
        }

        var cible = type.TypeCible;
        var jeuCible = type.EstConteneur ? jeu.Element : jeu;
        var dernier = mots[^1];
        var concatene = string.Concat(mots);
        var source = "nom " + nomMembre;

        bool Contient(params string[] cles) => mots.Any(m => cles.Contains(m, StringComparer.Ordinal));

        void Ajouter(Contrainte contrainte) => jeuCible.Ajouter(contrainte with { Origine = OrigineContrainte.Nom, Source = source });

        var estIdentifiant = dernier is "id" or "identifiant" or "identifier" || (mots.Count == 1 && mots[0] == "id");

        switch (cible.Genre)
        {
            case GenreValeur.Entier:
                if (estIdentifiant)
                {
                    Ajouter(new Unicite(true));
                }
                else if (Contient("pourcentage", "pct", "percent", "taux", "rate"))
                {
                    Ajouter(new Plage(0, 100));
                }
                else if (Contient("quantite", "qty", "nombre", "count", "nb"))
                {
                    Ajouter(new Plage(1, 100));
                }
                else if (Contient("age"))
                {
                    Ajouter(new Plage(18, 65));
                }
                else if (Contient("annee", "year"))
                {
                    Ajouter(new Plage(2000, 2030));
                }
                else if (Contient("mois", "month"))
                {
                    Ajouter(new Plage(1, 12));
                }
                else if (Contient("jour", "day"))
                {
                    Ajouter(new Plage(1, 28));
                }
                else if (Contient("heure", "hour"))
                {
                    Ajouter(new Plage(0, 23));
                }
                else if (Contient("minute", "seconde", "second"))
                {
                    Ajouter(new Plage(0, 59));
                }
                else if (concatene is "codepostal" or "zipcode" or "zip")
                {
                    Ajouter(new Plage(1000, 99999));
                }
                else if (Contient("epaisseur", "largeur", "longueur", "hauteur", "diametre", "thickness", "width", "height", "diameter"))
                {
                    Ajouter(new Plage(1, 3000));
                }
                else if (Contient("temperature"))
                {
                    Ajouter(new Plage(20, 900));
                }

                break;

            case GenreValeur.Reel:
                if (Contient("pourcentage", "pct", "percent", "taux", "rate"))
                {
                    Ajouter(new Plage(0, 100));
                }
                else if (Contient("prix", "price", "montant", "amount", "cout", "cost", "tarif"))
                {
                    Ajouter(new Plage(1, 10000));
                    Ajouter(new Decimales(2));
                }
                else if (Contient("poids", "weight", "masse", "mass"))
                {
                    Ajouter(new Plage(0.1m, 1000));
                }
                else if (Contient("epaisseur", "largeur", "longueur", "hauteur", "diametre", "thickness", "width", "height", "diameter"))
                {
                    Ajouter(new Plage(0.1m, 3000));
                }
                else if (Contient("temperature"))
                {
                    Ajouter(new Plage(20, 900));
                }
                else if (Contient("latitude"))
                {
                    Ajouter(new Plage(-90, 90));
                }
                else if (Contient("longitude"))
                {
                    Ajouter(new Plage(-180, 180));
                }

                break;

            case GenreValeur.Texte:
                if (estIdentifiant)
                {
                    Ajouter(new Unicite(true));
                }
                else if (Contient("email", "mail", "courriel"))
                {
                    Ajouter(new Format(GenreFormat.Email));
                }
                else if (Contient("url", "uri", "lien", "link", "site"))
                {
                    Ajouter(new Format(GenreFormat.Url));
                }
                else if (Contient("telephone", "tel", "phone", "mobile", "fax"))
                {
                    Ajouter(new Format(GenreFormat.Telephone));
                }
                else if (concatene is "codepostal" or "zipcode" or "postalcode")
                {
                    Ajouter(new Format(GenreFormat.CodePostal));
                }
                else if (Contient("code", "ref", "reference", "matricule", "sku", "numero", "number"))
                {
                    Ajouter(new Format(GenreFormat.Majuscules));
                    Ajouter(new Format(GenreFormat.Alphanumerique));
                    Ajouter(new LongueurTexte(6, 10));
                }
                else if (Contient("pays", "country"))
                {
                    Ajouter(new ValeurFixe("France", ParDefaut: true));
                }
                else if (Contient("devise", "currency", "monnaie"))
                {
                    Ajouter(new ValeurFixe("EUR", ParDefaut: true));
                }
                else if (Contient("langue", "culture", "locale", "language"))
                {
                    Ajouter(new ValeurFixe("fr-FR", ParDefaut: true));
                }
                else if (Contient("version"))
                {
                    Ajouter(new ValeurFixe("1.0.0", ParDefaut: true));
                }

                break;
        }
    }
}
