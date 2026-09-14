using System.Runtime.InteropServices;
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
//  2. choix du type racine (--type, ou l'unique type que personne ne reference ; avec --out <dossier>, toutes les
//     racines, un fichier chacune) ;
//  3. interpretation des commentaires en contraintes : regles regex (defaut) ou LLM local via Ollama (--llm) ;
//  4. generation deterministe (graine) des documents JSON, ecrits sur stdout ou dans --out ;
//     tout le reste (avertissements, rapport --explain, graine, progression) part sur stderr.
//
//  Usage : dotnet run --project src/GenerateurJson -- --source exemples/ModelesExemple.cs --type Bobine --count 3 --seed 42
//          voir --help pour la liste complete des options ; sans argument, les options sont demandees en console.
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

        // Sans argument depuis une vraie console (double-clic sur l'exe, dotnet run sans --) : les options sont
        // demandees une a une. Entree redirigee (script, pipe) : comportement inchange, erreur d'usage.
        if (args.Length > 0 || Console.IsInputRedirected)
        {
            return Lancer(args, journal);
        }

        var saisie = new SaisieConsole(Console.In, journal).Demander();
        if (saisie is null)
        {
            return 1;
        }

        var code = Lancer(saisie, journal);
        if (ConsoleOuvertePourCeProcessus())
        {
            // Sinon la fenetre ouverte par le double-clic se fermerait avant que le JSON ou l'erreur ne soit lu.
            journal.Write("Appuyez sur Entree pour fermer la fenetre.");
            Console.In.ReadLine();
        }

        return code;
    }

    /// <summary>
    /// Vrai si aucun autre processus ne partage la console : elle a ete creee pour ce programme (double-clic) et
    /// disparaitra avec lui. Depuis un terminal ou par dotnet run, le shell y est aussi attache.
    /// </summary>
    private static bool ConsoleOuvertePourCeProcessus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return GetConsoleProcessList(new uint[2], 2) == 1;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] listeProcessus, uint nombre);

    private static int Lancer(string[] args, TextWriter journal)
    {
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
            if (args.Length == 0 && Console.IsInputRedirected)
            {
                // Typiquement F5 dans VS Code : la console de debogage (csharp.debug.console = internalConsole par
                // defaut) n'est pas un terminal, le mode interactif n'y pourrait rien lire.
                journal.WriteLine("Sans argument, les options sont demandees en console, mais l'entree est redirigee ici (console de");
                journal.WriteLine("debogage de VS Code, pipe...) : lancez dans un terminal, ou reglez \"csharp.debug.console\": \"integratedTerminal\".");
            }

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

        var racines = ChoisirRacines(catalogue, options.Type, options.FichierSortie, journal);
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
                Total = racines.SelectMany(catalogue.TypesAtteignables)
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
            var optionsGeneration = new OptionsGeneration(graine, options.Nommage, options.ProfondeurMax, options.TauxNull, options.EnumEnEntier, options.DatePivot);
            var documents = new List<(DescripteurType Racine, JsonNode? Document)>();
            var avertissementsGeneration = new List<string>();
            foreach (var racine in racines)
            {
                if (options.Expliquer)
                {
                    journal.Write(RapportExplication.Construire(catalogue, racine, contraintes));
                    journal.WriteLine();
                }

                // Un generateur par type, repartant de la graine : chaque fichier est celui que donnerait --type seul.
                var generateur = new GenerateurDocumentJson(catalogue, optionsGeneration, contraintes);
                documents.Add((racine, options.Nombre == 1 && !options.Tableau
                    ? generateur.Generer(racine)
                    : generateur.GenererPlusieurs(racine, options.Nombre)));
                avertissementsGeneration.AddRange(generateur.Avertissements);
            }

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

            foreach (var avertissement in avertissementsGeneration)
            {
                journal.WriteLine("avertissement : " + avertissement);
            }

            foreach (var (racine, document) in documents)
            {
                EcrireJson(document, options, CheminSortie(options.FichierSortie, racine, racines), journal);
            }
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

    /// <summary>
    /// Le type de --type ; sans --type, l'unique racine, ou toutes les racines (a defaut tous les types generables)
    /// quand --out designe un dossier, ou chacune aura son fichier.
    /// </summary>
    private static IReadOnlyList<DescripteurType> ChoisirRacines(CatalogueTypes catalogue, string? nom, string? sortie, TextWriter journal)
    {
        if (nom is not null)
        {
            var type = catalogue.Resoudre(nom)
                       ?? throw new ErreurAnalyse($"type « {nom} » introuvable. Types disponibles : {Noms(catalogue.Types)}");
            if (type.EstGenerable)
            {
                return [type];
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
            return racines;
        }

        var candidats = racines.Count > 0 ? racines : catalogue.Types.Where(t => t.EstGenerable).ToList();
        if (candidats.Count > 0 && SortieDansDossier(sortie))
        {
            journal.WriteLine($"types racines ({candidats.Count}, un fichier chacun) : {Noms(candidats)}");
            return candidats;
        }

        throw new ErreurAnalyse(
            (racines.Count == 0
                ? "aucun type racine evident (tous les types generables sont references par un autre)"
                : "plusieurs types racines possibles") +
            $" : precisez --type parmi {Noms(candidats)}, ou --out <dossier> pour un fichier par type");
    }

    private static string Noms(IEnumerable<DescripteurType> types) => string.Join(", ", types.Select(t => t.NomComplet));

    /// <summary>--out designe un dossier : existant, ou chemin termine par un separateur (dossier a creer).</summary>
    private static bool SortieDansDossier(string? sortie) =>
        sortie is not null && (Path.EndsInDirectorySeparator(sortie) || Directory.Exists(sortie));

    /// <summary>
    /// Fichier de destination d'un type, null pour stdout. Dossier : &lt;Type&gt;.json dedans, avec le nom qualifie si
    /// deux types generes partagent le meme nom simple.
    /// </summary>
    private static string? CheminSortie(string? sortie, DescripteurType racine, IReadOnlyList<DescripteurType> racines)
    {
        if (sortie is null)
        {
            return null;
        }

        if (!SortieDansDossier(sortie))
        {
            return Path.GetFullPath(sortie);
        }

        var nom = racines.Count(r => r.NomSimple == racine.NomSimple) > 1 ? racine.NomComplet : racine.NomSimple;
        return Path.Combine(Path.GetFullPath(sortie), nom + ".json");
    }

    private static void EcrireJson(JsonNode? document, OptionsLigneCommande options, string? chemin, TextWriter journal)
    {
        var optionsJson = new JsonSerializerOptions
        {
            WriteIndented = !options.Compact,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var texte = (document?.ToJsonString(optionsJson) ?? "null") + "\n";
        var octets = new UTF8Encoding(false).GetBytes(texte);

        if (chemin is not null)
        {
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
