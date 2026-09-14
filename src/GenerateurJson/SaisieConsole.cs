using System.Globalization;

namespace GenerateurJson;

/// <summary>
/// Mode interactif, quand le programme est lance sans argument : les options essentielles sont demandees en console
/// et traduites en arguments, que <see cref="OptionsLigneCommande.Analyser"/> relit ensuite comme s'ils avaient ete
/// tapes ; il reste le seul endroit ou les valeurs sont validees. Questions et messages partent sur stderr : stdout ne
/// porte que le JSON.
/// </summary>
public sealed class SaisieConsole(TextReader entree, TextWriter journal)
{
    /// <summary>Fin du flux d'entree (Ctrl+Z puis Entree) : l'utilisateur abandonne.</summary>
    private sealed class SaisieInterrompue() : Exception;

    /// <summary>Arguments equivalents aux reponses, ou null si la saisie a ete interrompue.</summary>
    public string[]? Demander()
    {
        journal.WriteLine("GenerateurJson sans argument : mode interactif (Entree seule = valeur entre crochets).");
        journal.WriteLine("Toutes les options sont decrites par --help.");
        journal.WriteLine();
        try
        {
            var arguments = new List<string>();
            DemanderMoteur(arguments);
            var catalogue = DemanderSource(arguments);
            DemanderType(catalogue, arguments);
            DemanderValeur("Nombre de documents", "1", "--count", arguments);
            DemanderValeur("Graine (vide = tiree au hasard)", null, "--seed", arguments);
            var sortie = SansGuillemets(Lire("Fichier ou dossier de sortie (vide = affichage ici)", null));
            if (sortie.Length > 0)
            {
                arguments.Add("--out");
                arguments.Add(sortie);
            }

            if (DemanderOuiNon("Afficher le rapport --explain", defaut: false))
            {
                arguments.Add("--explain");
            }

            journal.WriteLine();
            journal.WriteLine("commande equivalente : GenerateurJson " + string.Join(" ", arguments.Select(Citer)));
            journal.WriteLine();
            return [.. arguments];
        }
        catch (SaisieInterrompue)
        {
            journal.WriteLine();
            journal.WriteLine("saisie interrompue");
            return null;
        }
    }

    /// <summary>
    /// Moteur d'interpretation des commentaires : regles (defaut) ou LLM local. Ollama et le modele sont verifies tout
    /// de suite et ce qui manque est propose au telechargement (<see cref="InstallateurOllama"/>), pour ne pas le
    /// decouvrir apres toutes les autres questions ; refus ou echec : la question est reposee (n ou Entree : regles).
    /// </summary>
    private void DemanderMoteur(List<string> arguments)
    {
        var defauts = new OptionsLigneCommande();
        while (DemanderOuiNon("Interpreter les commentaires avec le LLM local (Ollama)", defaut: false))
        {
            var modele = Lire("Modele Ollama", defauts.LlmModele);
            bool pret;
            try
            {
                pret = new InstallateurOllama(journal).Preparer(defauts.LlmUrl, modele, question => DemanderOuiNon("  " + question, defaut: true));
            }
            catch (ErreurLlm e)
            {
                foreach (var ligne in e.Message.Split('\n'))
                {
                    journal.WriteLine("  " + ligne);
                }

                pret = false;
            }

            if (!pret)
            {
                journal.WriteLine("  (n ou Entree : continuer avec le moteur regles)");
                continue;
            }

            journal.WriteLine($"  Ollama repond, modele {modele} present");
            arguments.Add("--llm");
            if (modele != defauts.LlmModele)
            {
                arguments.Add("--llm-model");
                arguments.Add(modele);
            }

            return;
        }
    }

    /// <summary>Redemande tant que la source est introuvable ou sans type generable ; renvoie son catalogue pour proposer les types.</summary>
    private CatalogueTypes DemanderSource(List<string> arguments)
    {
        while (true)
        {
            var chemin = SansGuillemets(Lire("Source : fichier .cs ou dossier (glisser-deposer accepte)", null));
            if (chemin.Length == 0)
            {
                continue;
            }

            try
            {
                var catalogue = AnalyseurSources.Analyser([chemin]);
                if (catalogue.Types.Any(t => t.EstGenerable))
                {
                    arguments.Add("--source");
                    arguments.Add(chemin);
                    return catalogue;
                }

                journal.WriteLine("  aucune classe, record ou struct generable dans cette source");
            }
            catch (Exception e) when (e is ErreurAnalyse or IOException or UnauthorizedAccessException)
            {
                journal.WriteLine("  " + e.Message);
            }
        }
    }

    /// <summary>Liste numerotee des racines (ou de tous les types generables s'il n'y en a pas) ; un nom de type est aussi accepte.</summary>
    private void DemanderType(CatalogueTypes catalogue, List<string> arguments)
    {
        var racines = catalogue.Racines();
        var proposes = racines.Count > 0 ? racines : catalogue.Types.Where(t => t.EstGenerable).ToList();
        journal.WriteLine(racines.Count > 0 ? "Types racines (references par aucun autre type) :" : "Types generables :");
        for (var i = 0; i < proposes.Count; i++)
        {
            journal.WriteLine($"  {i + 1}. {proposes[i].NomComplet}");
        }

        while (true)
        {
            // Entree seule : le premier type propose, quel que soit leur nombre.
            var reponse = Lire("Type racine : numero, ou nom de n'importe quel type", "1");
            DescripteurType? type;
            if (int.TryParse(reponse, NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
            {
                type = numero >= 1 && numero <= proposes.Count ? proposes[numero - 1] : null;
                if (type is null)
                {
                    journal.WriteLine($"  numero entre 1 et {proposes.Count} attendu");
                    continue;
                }
            }
            else
            {
                try
                {
                    type = catalogue.Resoudre(reponse) ?? ResoudreSansCasse(catalogue, reponse);
                }
                catch (ErreurAnalyse e)
                {
                    journal.WriteLine("  " + e.Message);
                    continue;
                }

                if (type is null || !type.EstGenerable)
                {
                    journal.WriteLine(type is null
                        ? $"  type « {reponse} » introuvable"
                        : $"  {type.NomComplet} n'est pas generable (enum, interface, abstrait, statique ou generique)");
                    continue;
                }
            }

            arguments.Add("--type");
            arguments.Add(type.NomComplet);
            return;
        }
    }

    /// <summary>
    /// Repli de la saisie (« bobine » pour Bobine) ; le nom exact est ensuite passe a --type, qui reste sensible a la
    /// casse comme C#. <see cref="ErreurAnalyse"/> si plusieurs types correspondent.
    /// </summary>
    private static DescripteurType? ResoudreSansCasse(CatalogueTypes catalogue, string nom)
    {
        var candidats = catalogue.Types
            .Where(t => string.Equals(t.NomSimple, nom, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.NomComplet, nom, StringComparison.OrdinalIgnoreCase) ||
                        t.NomComplet.EndsWith("." + nom, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return candidats.Count switch
        {
            0 => null,
            1 => candidats[0],
            _ => throw new ErreurAnalyse($"le nom « {nom} » est ambigu : {string.Join(", ", candidats.Select(c => c.NomComplet))}"),
        };
    }

    /// <summary>Reponse validee par l'analyseur de la ligne de commande lui-meme (memes regles, memes messages) ; vide ou egale au defaut : option omise.</summary>
    private void DemanderValeur(string question, string? defaut, string option, List<string> arguments)
    {
        while (true)
        {
            var reponse = Lire(question, defaut);
            if (reponse.Length == 0 || reponse == defaut)
            {
                return;
            }

            try
            {
                OptionsLigneCommande.Analyser([option, reponse]);
                arguments.Add(option);
                arguments.Add(reponse);
                return;
            }
            catch (ErreurLigneCommande e)
            {
                journal.WriteLine("  " + e.Message);
            }
        }
    }

    private bool DemanderOuiNon(string question, bool defaut)
    {
        while (true)
        {
            switch (Lire(question + " (o/n)", defaut ? "o" : "n").ToLowerInvariant())
            {
                case "o" or "oui" or "y" or "yes":
                    return true;
                case "n" or "non" or "no":
                    return false;
            }
        }
    }

    private string Lire(string question, string? defaut)
    {
        journal.Write(defaut is null ? $"{question} : " : $"{question} [{defaut}] : ");
        var ligne = (entree.ReadLine() ?? throw new SaisieInterrompue()).Trim();
        return ligne.Length == 0 ? defaut ?? string.Empty : ligne;
    }

    /// <summary>Un glisser-deposer dans la console colle le chemin entre guillemets s'il contient un espace.</summary>
    private static string SansGuillemets(string texte) => texte.Trim().Trim('"', '\'').Trim();

    private static string Citer(string argument) =>
        argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
}
