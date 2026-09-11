using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GenerateurJson;

public enum GenreLitteral
{
    Guillemets,
    Regex,
    Date,
    FormatDate,
    Email,
    Url,
}

/// <summary>Un fragment litteral masque dans le texte de travail (position relative au texte, contenu original sans delimiteurs).</summary>
public sealed record Litteral(int Debut, int Longueur, GenreLitteral Genre, string Texte);

/// <summary>
/// Preparation du texte des commentaires : normalisation isometrique (accents retires sans changer la longueur,
/// pour que les positions des regex pointent dans le texte original), masquage des litteraux, decoupage en phrases,
/// lecture des nombres et des listes.
/// </summary>
public static class NormaliseurTexte
{
    private const char Masque = (char)1;

    private static readonly Regex Guillemets = new("\"(?<t>[^\"\n]*)\"|`(?<t>[^`\n]*)`", RegexOptions.CultureInvariant);
    private static readonly Regex RegexNue = new(@"\^\S+\$", RegexOptions.CultureInvariant);
    private static readonly Regex DateIso = new(@"(?<!\d)\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2})?)?(?!\d)|(?<!\d)\d{2}/\d{2}/\d{4}(?: \d{2}:\d{2}(?::\d{2})?)?(?!\d)", RegexOptions.CultureInvariant);
    private static readonly Regex JetonFormatDate = new(@"(?<![\w])[yMdHhmsfFzKtT:\-/.']+(?:\s[yMdHhmsfFzKtT:\-/.']+)*(?![\w])", RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.CultureInvariant);
    private static readonly Regex Url = new(@"https?://\S+", RegexOptions.CultureInvariant);
    private static readonly Regex SeparateursListe = new(@"\s*(?:,(?!\d)|;|/|\|)\s*|\s+(?:ou|or|et|and)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] FormatsDate =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss",
    ];

    /// <summary>Retire les accents et uniformise la ponctuation typographique, caractere par caractere (meme longueur).</summary>
    public static string NormaliserIsometrique(string texte)
    {
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte)
        {
            sb.Append(Simplifier(c));
        }

        return sb.ToString();
    }

    private static char Simplifier(char c)
    {
        if (c < 128)
        {
            return c;
        }

        switch (c)
        {
            case '’':
            case '‘':
                return '\'';
            case '«':
            case '»':
            case '“':
            case '”':
                return '"';
            case (char)0x00A0:
            case (char)0x202F:
                return ' ';
            case '–':
            case '—':
                return '-';
            case 'œ':
                return 'o';
            case 'Œ':
                return 'O';
            case 'æ':
                return 'a';
            case 'Æ':
                return 'A';
        }

        if (char.IsLetter(c))
        {
            var decompose = c.ToString().Normalize(NormalizationForm.FormD);
            if (decompose.Length > 0 && decompose[0] < 128)
            {
                return decompose[0];
            }
        }

        return c;
    }

    /// <summary>
    /// Remplace les litteraux (guillemets, regex ^...$, dates, formats de date, emails, URL) par des caracteres de
    /// masque de meme longueur, pour qu'une date « 2024-01-01 » ne soit pas lue comme une plage 2024..1.
    /// </summary>
    public static (string Travail, IReadOnlyList<Litteral> Litteraux) MasquerLitteraux(string normalise, string original)
    {
        var travail = normalise.ToCharArray();
        var litteraux = new List<Litteral>();

        Masquer(Guillemets, GenreLitteral.Guillemets, m => m.Groups["t"]);
        Masquer(RegexNue, GenreLitteral.Regex, m => m.Groups[0]);
        Masquer(DateIso, GenreLitteral.Date, m => m.Groups[0]);
        Masquer(JetonFormatDate, GenreLitteral.FormatDate, m => m.Groups[0], texte => EstFormatDate(texte, 2));
        Masquer(Email, GenreLitteral.Email, m => m.Groups[0]);
        Masquer(Url, GenreLitteral.Url, m => m.Groups[0]);

        return (new string(travail), litteraux);

        void Masquer(Regex regex, GenreLitteral genre, Func<Match, Group> interne, Func<string, bool>? valide = null)
        {
            foreach (Match m in regex.Matches(new string(travail)))
            {
                if (m.Length == 0 || travail.Skip(m.Index).Take(m.Length).Any(c => c == Masque))
                {
                    continue;
                }

                var groupe = interne(m);
                var texte = original.Substring(groupe.Index, groupe.Length);
                if (valide is not null && !valide(texte))
                {
                    continue;
                }

                litteraux.Add(new Litteral(m.Index, m.Length, genre, texte));
                for (var i = m.Index; i < m.Index + m.Length; i++)
                {
                    travail[i] = Masque;
                }
            }
        }
    }

    /// <summary>Un jeton ne contenant que des caracteres de format de date, avec au moins <paramref name="genresMin"/> composants distincts (annee, mois, jour, heure, minute, seconde).</summary>
    public static bool EstFormatDate(string texte, int genresMin)
    {
        if (texte.Length < 2 || texte.Any(c => !"yMdHhmsfFzKtT:-/.' ".Contains(c)))
        {
            return false;
        }

        var genres = 0;
        if (texte.Contains("yy", StringComparison.Ordinal))
        {
            genres++;
        }

        if (texte.Contains("MM", StringComparison.Ordinal))
        {
            genres++;
        }

        if (texte.Contains("dd", StringComparison.Ordinal))
        {
            genres++;
        }

        if (texte.Contains("HH", StringComparison.Ordinal) || texte.Contains("hh", StringComparison.Ordinal))
        {
            genres++;
        }

        if (texte.Contains("mm", StringComparison.Ordinal))
        {
            genres++;
        }

        if (texte.Contains("ss", StringComparison.Ordinal))
        {
            genres++;
        }

        if (genres < genresMin)
        {
            return false;
        }

        try
        {
            _ = new DateTime(2000, 1, 1).ToString(texte, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Decoupe en phrases : retours a la ligne, « ; » (sauf devant un nombre), « . ! ? » suivis d'un blanc ou en fin de texte.</summary>
    public static IReadOnlyList<(int Debut, int Fin)> Phrases(string travail)
    {
        var phrases = new List<(int, int)>();
        var debut = 0;
        for (var i = 0; i < travail.Length; i++)
        {
            var c = travail[i];
            var coupe = false;
            if (c == '\n')
            {
                coupe = true;
            }
            else if (c is '.' or '!' or '?')
            {
                coupe = i + 1 >= travail.Length || char.IsWhiteSpace(travail[i + 1]);
            }
            else if (c == ';')
            {
                var j = i + 1;
                while (j < travail.Length && travail[j] == ' ')
                {
                    j++;
                }

                coupe = j >= travail.Length || !(char.IsDigit(travail[j]) || travail[j] is '-' or '+');
            }

            if (coupe)
            {
                phrases.Add((debut, i));
                debut = i + 1;
            }
        }

        phrases.Add((debut, travail.Length));
        return phrases.Where(p => p.Item2 > p.Item1 && travail.AsSpan(p.Item1, p.Item2 - p.Item1).Trim().Length > 0).ToList();
    }

    public static decimal? Nombre(string texte)
    {
        var propre = texte.Trim().Replace(',', '.');
        return decimal.TryParse(propre, NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur) ? valeur : null;
    }

    public static int? Entier(decimal? valeur) =>
        valeur is { } v && v == decimal.Floor(v) && v >= int.MinValue && v <= int.MaxValue ? (int)v : null;

    public static string FormaterNombre(decimal valeur) => valeur.ToString("0.############", CultureInfo.InvariantCulture);

    public static DateTime? Date(string texte) =>
        DateTime.TryParseExact(texte.Trim(), FormatsDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Scinde une liste « A, B ou C » / « A ; B » / « A | B » / « A / B », en retirant guillemets, parentheses et point final.</summary>
    public static IReadOnlyList<string> ScinderListe(string texte)
    {
        var resultat = new List<string>();
        foreach (var brut in SeparateursListe.Split(texte))
        {
            var element = brut.Trim().Trim('"', '`', '\'', '(', ')', '[', ']').TrimEnd('.').Trim();
            if (element.Length > 0)
            {
                resultat.Add(element);
            }
        }

        return resultat;
    }
}
