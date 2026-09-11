using System.Text;

namespace GenerateurJson;

/// <summary>Textes factices lisibles (syllabes prononcables), formats structures (email, url, telephone...) et ajustement aux longueurs demandees.</summary>
public sealed class GenerateurTexte(Random aleatoire)
{
    private static readonly string[] Consonnes = ["b", "c", "d", "f", "g", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v", "z", "ch", "br", "tr", "pl", "cr", "gr"];
    private static readonly string[] Voyelles = ["a", "e", "i", "o", "u", "ou", "an", "on", "in", "ai"];
    private const string Chiffres = "0123456789";
    private const string LettresMinuscules = "abcdefghijklmnopqrstuvwxyz";
    private const string LettresMajuscules = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public string Syllabe() => Consonnes[aleatoire.Next(Consonnes.Length)] + Voyelles[aleatoire.Next(Voyelles.Length)];

    public string Mot(int minSyllabes = 2, int maxSyllabes = 3)
    {
        var nombre = aleatoire.Next(minSyllabes, maxSyllabes + 1);
        var sb = new StringBuilder();
        for (var i = 0; i < nombre; i++)
        {
            sb.Append(Syllabe());
        }

        return sb.ToString();
    }

    public string Mots(int nombre) => string.Join(" ", Enumerable.Range(0, nombre).Select(_ => Mot()));

    public string Phrase(int minMots = 6, int maxMots = 10) => Capitaliser(Mots(aleatoire.Next(minMots, maxMots + 1))) + ".";

    public string NomPropre() => Capitaliser(Mot()) + " " + Capitaliser(Mot(2, 4));

    public string Email() => $"{Mot()}.{Mot()}@exemple.fr";

    public string Url() => $"https://exemple.fr/{Mot()}/{Mot()}";

    public string Telephone() => "06 " + string.Join(" ", Enumerable.Range(0, 4).Select(_ => SuiteDeChiffres(2)));

    public string Code(int longueur) => Suite(LettresMajuscules + Chiffres, longueur);

    public string SuiteDeChiffres(int longueur) => Suite(Chiffres, longueur);

    public string SuiteDeLettres(int longueur, bool majuscules) => Suite(majuscules ? LettresMajuscules : LettresMinuscules, longueur);

    public static string Capitaliser(string texte) =>
        texte.Length == 0 ? texte : char.ToUpperInvariant(texte[0]) + texte[1..];

    /// <summary>Applique les contraintes de jeu de caracteres et de casse.</summary>
    public string AppliquerFormats(string texte, IReadOnlySet<GenreFormat> formats)
    {
        if (formats.Contains(GenreFormat.ChiffresUniquement))
        {
            texte = new string(texte.Where(c => !char.IsWhiteSpace(c)).Select(c => char.IsDigit(c) ? c : Chiffres[aleatoire.Next(Chiffres.Length)]).ToArray());
        }

        if (formats.Contains(GenreFormat.Lettres))
        {
            texte = new string(texte.Where(c => !char.IsWhiteSpace(c)).Select(c => char.IsLetter(c) ? c : LettresMinuscules[aleatoire.Next(LettresMinuscules.Length)]).ToArray());
        }

        if (formats.Contains(GenreFormat.Alphanumerique))
        {
            texte = new string(texte.Where(char.IsLetterOrDigit).ToArray());
        }

        if (formats.Contains(GenreFormat.SansEspaces))
        {
            texte = new string(texte.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }

        if (formats.Contains(GenreFormat.Majuscules))
        {
            texte = texte.ToUpperInvariant();
        }
        else if (formats.Contains(GenreFormat.Minuscules))
        {
            texte = texte.ToLowerInvariant();
        }

        return texte;
    }

    /// <summary>Coupe ou complete le texte pour respecter la longueur (exacte, minimale ou maximale).</summary>
    public string AjusterLongueur(string texte, LongueurTexte? longueur, IReadOnlySet<GenreFormat> formats)
    {
        if (longueur is null)
        {
            return texte;
        }

        var min = longueur.Min;
        var max = longueur.Max;
        if (min is not null && texte.Length < min)
        {
            // Complement en syllabes (ou en chiffres) pour rester lisible, puis coupe eventuelle a la longueur exacte.
            var sb = new StringBuilder(texte);
            while (sb.Length < min)
            {
                var morceau = formats.Contains(GenreFormat.ChiffresUniquement) ? SuiteDeChiffres(3) : Syllabe();
                sb.Append(formats.Contains(GenreFormat.Majuscules) ? morceau.ToUpperInvariant() : morceau);
            }

            texte = sb.ToString();
        }

        if (max is not null && texte.Length > max)
        {
            texte = texte[..Math.Max(0, max.Value)];
        }

        return texte;
    }

    private string Suite(string alphabet, int longueur)
    {
        var sb = new StringBuilder(longueur);
        for (var i = 0; i < longueur; i++)
        {
            sb.Append(alphabet[aleatoire.Next(alphabet.Length)]);
        }

        return sb.ToString();
    }
}
