using System.Text.RegularExpressions;

namespace GenerateurJson;

/// <summary>
/// Interprete les commentaires par la grammaire de <see cref="ReglesContraintes"/> : normalisation, masquage des
/// litteraux, decoupage en phrases, application des regles en deux phases avec consommation du texte reconnu,
/// avertissement pour les phrases qui contiennent un nombre ou un mot declencheur sans qu'aucune regle ne s'applique.
/// </summary>
public sealed class InterpreteurContraintes : IInterpreteurCommentaires
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex Declencheurs = new(
        @"\d|\b(?:max|maxi|maximum|min|mini|minimum|entre|between|format|obligatoire|requis|required|optionnel|optional|facultatif|" +
        @"jamais|never|toujours|always|longueur|length|taille|size|chiffres?|digits?|caracteres?|characters?|positif|positive|" +
        @"negatif|negative|unique|parmi|among|possibles?|autorisees?|allowed|interdit|forbidden|null|vide|empty|regex|pattern|" +
        @"motif|decimales?|arrondi|round|multiple|pair|impair|even|odd|futur|future|passe|past|avant|apres|before|after|" +
        @"superieur|superieure|inferieur|inferieure|greater|less|limite|limit|borne|plafond|plancher|doit|must|uniquement|seulement|only)\b",
        Options);

    private static readonly Regex PrefixeElement = new(
        @"^\s*(?:pour\s+)?(?:chaque|each|les|the|tous les|toutes les|all)?\s*(?:elements?|valeurs?|items?|entrees?|entries)\b\s*[:=]?",
        Options);

    public string Nom => "regles";

    public JeuContraintes Interpreter(DescripteurMembre membre, CatalogueTypes catalogue)
    {
        var jeu = new JeuContraintes();
        ContraintesCommunes.AjouterAttributsEtType(membre, jeu);
        foreach (var (source, texte) in membre.Commentaires.Sources())
        {
            foreach (var resultat in InterpreterTexte(texte, membre.Type, source, jeu.Avertissements))
            {
                ContraintesCommunes.AjouterAvecCible(jeu, membre.Type, resultat);
            }
        }

        return jeu;
    }

    public static IReadOnlyList<ResultatRegle> InterpreterTexte(string texte, ReferenceType type, string source, List<string> avertissements)
    {
        var normalise = NormaliseurTexte.NormaliserIsometrique(texte);
        var (travail, litteraux) = NormaliseurTexte.MasquerLitteraux(normalise, texte);
        var resultats = new List<ResultatRegle>();

        foreach (var (debut, fin) in NormaliseurTexte.Phrases(travail))
        {
            var contexte = new ContexteRegle
            {
                Original = texte[debut..fin],
                Travail = travail[debut..fin],
                Source = source,
                Type = type,
                Litteraux = litteraux
                    .Where(l => l.Debut >= debut && l.Debut < fin)
                    .Select(l => l with { Debut = l.Debut - debut })
                    .ToList(),
                Avertissements = avertissements,
            };

            var cibleElement = type.EstConteneur && PrefixeElement.IsMatch(contexte.Travail);
            foreach (var phase in new[] { PhaseRegle.Litteraux, PhaseRegle.MotsCles })
            {
                foreach (var regle in ReglesContraintes.Toutes)
                {
                    if (regle.Phase != phase)
                    {
                        continue;
                    }

                    AppliquerRegle(regle, contexte, cibleElement, resultats);
                }
            }

            if (Declencheurs.IsMatch(contexte.Travail))
            {
                avertissements.Add($"phrase non reconnue ({source}) : « {contexte.Original.Trim()} »");
            }
        }

        return resultats;
    }

    // Toutes les correspondances d'une regle sont interpretees sur le meme texte de travail, puis seules celles
    // qui ont produit une contrainte sont effacees (remplacees par des espaces, pour conserver les positions).
    private static void AppliquerRegle(Regle regle, ContexteRegle contexte, bool cibleElement, List<ResultatRegle> resultats)
    {
        var correspondances = regle.Motif.Matches(contexte.Travail);
        if (correspondances.Count == 0)
        {
            return;
        }

        var aEffacer = new List<Match>();
        foreach (Match correspondance in correspondances)
        {
            if (correspondance.Length == 0)
            {
                continue;
            }

            var produits = regle.Construire(correspondance, contexte).ToList();
            if (produits.Count == 0)
            {
                continue;
            }

            foreach (var produit in produits)
            {
                resultats.Add(cibleElement && produit.Cible == CibleContrainte.Auto
                    ? produit with { Cible = CibleContrainte.Element }
                    : produit);
            }

            aEffacer.Add(correspondance);
        }

        if (aEffacer.Count == 0)
        {
            return;
        }

        var tampon = contexte.Travail.ToCharArray();
        foreach (var correspondance in aEffacer)
        {
            for (var i = correspondance.Index; i < correspondance.Index + correspondance.Length; i++)
            {
                tampon[i] = ' ';
            }
        }

        contexte.Travail = new string(tampon);
    }
}
