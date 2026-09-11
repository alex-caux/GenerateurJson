using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenerateurJson;

// ---------------------------------------------------------------------------------------
//  POINT D'ENTREE
//
//  1. analyse syntaxique (Roslyn, sans compilation) des fichiers .cs fournis : types, membres, attributs,
//     commentaires ;
//  2. choix du type racine (--type, ou l'unique type que personne ne reference) ;
//  3. interpretation des commentaires en contraintes : regles regex (defaut) ou LLM local via Ollama (--llm) ;
//  4. generation deterministe (graine) des documents JSON, ecrits sur stdout ou dans --out ;
//     tout le reste (avertissements, rapport --explain, graine, progression) part sur stderr.
//
//  Usage : dotnet run --project src/GenerateurJson -- --source exemples/ModelesExemple.cs --type Bobine --count 3 --seed 42
//          voir --help pour la liste complete des options.
// ---------------------------------------------------------------------------------------

internal static class Program
{
    private static int Main(string[] args)
    {
        var journal = Console.Error;
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // console non interactive : sans importance
        }

        OptionsLigneCommande options;
        try
        {
            options = OptionsLigneCommande.Analyser(args);
        }
        catch (ErreurLigneCommande e)
        {
            journal.WriteLine("erreur : " + e.Message);
            journal.WriteLine("Utilisez --help pour l'aide.");
            return 1;
        }

        if (options.Aide)
        {
            Console.Out.Write(OptionsLigneCommande.TexteAide());
            return 0;
        }

        if (options.Sources.Count == 0)
        {
            journal.WriteLine("erreur : aucune source indiquee (fichier .cs ou dossier). Utilisez --help.");
            return 1;
        }

        try
        {
            return Executer(options, journal);
        }
        catch (ErreurLigneCommande e)
        {
            journal.WriteLine("erreur : " + e.Message);
            return 1;
        }
        catch (ErreurAnalyse e)
        {
            journal.WriteLine("erreur d'analyse : " + e.Message);
            return 2;
        }
        catch (ErreurLlm e)
        {
            journal.WriteLine("erreur LLM : " + e.Message);
            return 4;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            journal.WriteLine("erreur d'entree/sortie : " + e.Message);
            return 3;
        }
    }

    private static int Executer(OptionsLigneCommande options, TextWriter journal)
    {
        var catalogue = AnalyseurSources.Analyser(options.Sources);
        foreach (var avertissement in catalogue.Avertissements)
        {
            journal.WriteLine("avertissement : " + avertissement);
        }

        if (options.Lister)
        {
            Lister(catalogue);
            return 0;
        }

        if (catalogue.Types.Count == 0)
        {
            throw new ErreurAnalyse("aucun type trouve dans les sources");
        }

        var racine = ChoisirRacine(catalogue, options.Type, journal);
        var graine = options.Graine ?? Random.Shared.Next();
        journal.WriteLine($"graine utilisee : {graine}");

        ClientOllama? client = null;
        CacheLlm? cache = null;
        IInterpreteurCommentaires interpreteur;
        if (options.Llm)
        {
            client = new ClientOllama(options.LlmUrl, options.LlmModele, options.LlmAutoriserDistant);
            client.VerifierDisponibilite();
            cache = new CacheLlm(null, actif: !options.LlmSansCache);
            var llm = new InterpreteurLlm(client, cache, journal)
            {
                Total = catalogue.TypesAtteignables(racine)
                    .Where(t => t.Genre != GenreDeclaration.Enum)
                    .SelectMany(catalogue.MembresEffectifs)
                    .Distinct()
                    .Count(m => !m.Commentaires.EstVide),
            };
            interpreteur = llm;
            journal.WriteLine($"llm : {client.Modele} sur {client.Url} (cache : {cache.Chemin})");
        }
        else
        {
            interpreteur = new InterpreteurContraintes();
        }

        using (client)
        {
            var contraintes = new CalculateurContraintes(catalogue, interpreteur);
            if (options.Expliquer)
            {
                journal.Write(RapportExplication.Construire(catalogue, racine, contraintes));
                journal.WriteLine();
            }

            var optionsGeneration = new OptionsGeneration(graine, options.Nommage, options.ProfondeurMax, options.TauxNull, options.EnumEnEntier, options.DatePivot);
            var generateur = new GenerateurDocumentJson(catalogue, optionsGeneration, contraintes);
            JsonNode? document = options.Nombre == 1 && !options.Tableau
                ? generateur.Generer(racine)
                : generateur.GenererPlusieurs(racine, options.Nombre);

            if (!options.Expliquer)
            {
                foreach (var (membre, jeu) in contraintes.Calcules)
                {
                    foreach (var avertissement in jeu.Avertissements.Concat(jeu.AElement ? jeu.Element.Avertissements : []))
                    {
                        journal.WriteLine($"avertissement ({membre.NomQualifie}) : {avertissement}");
                    }
                }
            }

            foreach (var avertissement in generateur.Avertissements)
            {
                journal.WriteLine("avertissement : " + avertissement);
            }

            EcrireJson(document, options, journal);
            cache?.Sauvegarder();
        }

        return 0;
    }

    private static void Lister(CatalogueTypes catalogue)
    {
        var racines = catalogue.Racines();
        foreach (var type in catalogue.Types)
        {
            var nature = type.Genre switch
            {
                GenreDeclaration.Enum => $"enum, {type.MembresEnum.Count} membres",
                GenreDeclaration.Interface => "interface",
                _ => $"{type.Genre.ToString().ToLowerInvariant()}{(type.EstAbstrait ? " abstrait" : string.Empty)}, {type.Membres.Count} membres",
            };
            var marque = racines.Contains(type) ? "  <- racine possible" : string.Empty;
            Console.Out.WriteLine($"{type.NomComplet}  ({nature}, {type.Emplacement}){marque}");
        }
    }

    private static DescripteurType ChoisirRacine(CatalogueTypes catalogue, string? nom, TextWriter journal)
    {
        if (nom is not null)
        {
            var type = catalogue.Resoudre(nom)
                       ?? throw new ErreurAnalyse($"type « {nom} » introuvable. Types disponibles : {Noms(catalogue.Types)}");
            if (type.EstGenerable)
            {
                return type;
            }

            var implementations = catalogue.ImplementationsConcretes(type);
            var raison = type.Genre switch
            {
                GenreDeclaration.Enum => "une enumeration",
                GenreDeclaration.Interface => "une interface",
                _ when type.EstAbstrait => "abstrait",
                _ when type.EstStatique => "statique",
                _ => "generique",
            };
            throw new ErreurAnalyse(
                $"le type {type.NomComplet} n'est pas generable ({raison})." +
                (implementations.Count > 0 ? $" Sous-types concrets : {Noms(implementations)}" : string.Empty));
        }

        var racines = catalogue.Racines();
        if (racines.Count == 1)
        {
            journal.WriteLine($"type racine : {racines[0].NomComplet}");
            return racines[0];
        }

        var candidats = racines.Count > 0 ? racines : catalogue.Types.Where(t => t.EstGenerable).ToList();
        throw new ErreurAnalyse(
            (racines.Count == 0
                ? "aucun type racine evident (tous les types generables sont references par un autre)"
                : "plusieurs types racines possibles") +
            $" : precisez --type parmi {Noms(candidats)}");
    }

    private static string Noms(IEnumerable<DescripteurType> types) => string.Join(", ", types.Select(t => t.NomComplet));

    private static void EcrireJson(JsonNode? document, OptionsLigneCommande options, TextWriter journal)
    {
        var optionsJson = new JsonSerializerOptions
        {
            WriteIndented = !options.Compact,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var texte = (document?.ToJsonString(optionsJson) ?? "null") + "\n";
        var octets = new UTF8Encoding(false).GetBytes(texte);

        if (options.FichierSortie is not null)
        {
            var chemin = Path.GetFullPath(options.FichierSortie);
            var dossier = Path.GetDirectoryName(chemin);
            if (!string.IsNullOrEmpty(dossier))
            {
                Directory.CreateDirectory(dossier);
            }

            File.WriteAllBytes(chemin, octets);
            journal.WriteLine($"ecrit : {chemin}");
            return;
        }

        using var sortie = Console.OpenStandardOutput();
        sortie.Write(octets, 0, octets.Length);
        sortie.Flush();
    }
}
