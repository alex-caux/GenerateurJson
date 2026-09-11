using System.Text;
using System.Text.RegularExpressions;

namespace GenerateurJson;

/// <summary>
/// Regex inverse pour un sous-ensemble : litteraux, classes [a-z0-9_-] (et negations), \d \w \s \D \W \S, le point,
/// quantificateurs ? * + {n} {n,} {n,m}, groupes avec alternatives, ancres ^ $. Retourne null si le motif sort de ce
/// sous-ensemble ou si le texte produit ne le respecte pas (verification par Regex.IsMatch).
/// </summary>
public static class GenerateurMotif
{
    private const string Chiffres = "0123456789";
    private const string Minuscules = "abcdefghijklmnopqrstuvwxyz";
    private const string Majuscules = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Mot = Minuscules + Majuscules + Chiffres + "_";
    private const string Visibles = Mot + "-.:/ ";

    public static string? Generer(string motif, Random aleatoire)
    {
        try
        {
            var texte = motif;
            if (texte.StartsWith('^'))
            {
                texte = texte[1..];
            }

            if (texte.EndsWith('$') && !texte.EndsWith("\\$", StringComparison.Ordinal))
            {
                texte = texte[..^1];
            }

            var position = 0;
            var racine = Alternatives(texte, ref position);
            if (position != texte.Length)
            {
                return null;
            }

            var sb = new StringBuilder();
            racine.Generer(sb, aleatoire);
            var resultat = sb.ToString();
            return Regex.IsMatch(resultat, motif) ? resultat : null;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private abstract class Noeud
    {
        public abstract void Generer(StringBuilder sb, Random aleatoire);
    }

    private sealed class NoeudAlternatives(List<NoeudSequence> branches) : Noeud
    {
        public override void Generer(StringBuilder sb, Random aleatoire) => branches[aleatoire.Next(branches.Count)].Generer(sb, aleatoire);
    }

    private sealed class NoeudSequence : Noeud
    {
        public List<(Noeud Noeud, int Min, int Max)> Elements { get; } = [];

        public override void Generer(StringBuilder sb, Random aleatoire)
        {
            foreach (var (noeud, min, max) in Elements)
            {
                var repetitions = aleatoire.Next(min, max + 1);
                for (var i = 0; i < repetitions; i++)
                {
                    noeud.Generer(sb, aleatoire);
                }
            }
        }
    }

    private sealed class NoeudClasse(string alphabet) : Noeud
    {
        public override void Generer(StringBuilder sb, Random aleatoire) => sb.Append(alphabet[aleatoire.Next(alphabet.Length)]);
    }

    private sealed class NoeudLitteral(char caractere) : Noeud
    {
        public override void Generer(StringBuilder sb, Random aleatoire) => sb.Append(caractere);
    }

    private static NoeudAlternatives Alternatives(string texte, ref int position)
    {
        var branches = new List<NoeudSequence> { Sequence(texte, ref position) };
        while (position < texte.Length && texte[position] == '|')
        {
            position++;
            branches.Add(Sequence(texte, ref position));
        }

        return new NoeudAlternatives(branches);
    }

    private static NoeudSequence Sequence(string texte, ref int position)
    {
        var sequence = new NoeudSequence();
        while (position < texte.Length && texte[position] is not ('|' or ')'))
        {
            var atome = Atome(texte, ref position);
            var (min, max) = Quantificateur(texte, ref position);
            sequence.Elements.Add((atome, min, max));
        }

        return sequence;
    }

    private static Noeud Atome(string texte, ref int position)
    {
        var c = texte[position];
        switch (c)
        {
            case '(':
            {
                position++;
                if (position < texte.Length && texte[position] == '?')
                {
                    // Groupe non capturant (?:...) accepte ; toute autre construction (?...) est hors sous-ensemble.
                    if (position + 1 < texte.Length && texte[position + 1] == ':')
                    {
                        position += 2;
                    }
                    else
                    {
                        throw new FormatException("groupe special non supporte");
                    }
                }

                var interne = Alternatives(texte, ref position);
                if (position >= texte.Length || texte[position] != ')')
                {
                    throw new FormatException("parenthese non fermee");
                }

                position++;
                return interne;
            }

            case '[':
                position++;
                return Classe(texte, ref position);

            case '\\':
                position++;
                return Echappement(texte[position++]);

            case '.':
                position++;
                return new NoeudClasse(Visibles);

            case '*' or '+' or '?' or '{':
                throw new FormatException("quantificateur sans atome");

            default:
                position++;
                return new NoeudLitteral(c);
        }
    }

    private static Noeud Echappement(char c) => c switch
    {
        'd' => new NoeudClasse(Chiffres),
        'D' => new NoeudClasse(Minuscules + Majuscules + "-_"),
        'w' => new NoeudClasse(Mot),
        'W' => new NoeudClasse("-. "),
        's' => new NoeudClasse(" "),
        'S' => new NoeudClasse(Mot),
        'n' => new NoeudLitteral('\n'),
        't' => new NoeudLitteral('\t'),
        'b' or 'B' or 'A' or 'z' or 'Z' or 'G' => throw new FormatException("ancre non supportee"),
        _ when char.IsDigit(c) => throw new FormatException("reference arriere non supportee"),
        _ => new NoeudLitteral(c),
    };

    private static NoeudClasse Classe(string texte, ref int position)
    {
        var negation = position < texte.Length && texte[position] == '^';
        if (negation)
        {
            position++;
        }

        var alphabet = new StringBuilder();
        var premier = true;
        while (position < texte.Length && (texte[position] != ']' || premier))
        {
            premier = false;
            var c = texte[position++];
            if (c == '\\')
            {
                var echappe = texte[position++];
                alphabet.Append(echappe switch
                {
                    'd' => Chiffres,
                    'w' => Mot,
                    's' => " ",
                    _ => echappe.ToString(),
                });
                continue;
            }

            if (position + 1 < texte.Length && texte[position] == '-' && texte[position + 1] != ']')
            {
                var fin = texte[position + 1];
                position += 2;
                if (fin < c)
                {
                    throw new FormatException("intervalle inverse");
                }

                for (var x = c; x <= fin; x++)
                {
                    alphabet.Append(x);
                }

                continue;
            }

            alphabet.Append(c);
        }

        if (position >= texte.Length)
        {
            throw new FormatException("classe non fermee");
        }

        position++;
        var contenu = alphabet.ToString();
        if (negation)
        {
            contenu = new string(Visibles.Where(x => !contenu.Contains(x)).ToArray());
        }

        if (contenu.Length == 0)
        {
            throw new FormatException("classe vide");
        }

        return new NoeudClasse(contenu);
    }

    private static (int Min, int Max) Quantificateur(string texte, ref int position)
    {
        if (position >= texte.Length)
        {
            return (1, 1);
        }

        (int Min, int Max) resultat;
        switch (texte[position])
        {
            case '*':
                position++;
                resultat = (0, 3);
                break;
            case '+':
                position++;
                resultat = (1, 3);
                break;
            case '?':
                position++;
                resultat = (0, 1);
                break;
            case '{':
            {
                var fin = texte.IndexOf('}', position);
                if (fin < 0)
                {
                    throw new FormatException("accolade non fermee");
                }

                var contenu = texte[(position + 1)..fin];
                position = fin + 1;
                var parties = contenu.Split(',');
                var min = int.Parse(parties[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                var max = parties.Length == 1 ? min
                    : parties[1].Trim().Length == 0 ? min + 2
                    : int.Parse(parties[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                resultat = (min, max);
                break;
            }

            default:
                return (1, 1);
        }

        // Quantificateur paresseux (« +? », « *? ») : sans effet sur la generation.
        if (position < texte.Length && texte[position] == '?')
        {
            position++;
        }

        return resultat;
    }
}
