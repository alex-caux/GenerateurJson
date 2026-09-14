using System.Globalization;

namespace GenerateurJson;

/// <summary>Erreur d'usage de la ligne de commande : code de sortie 1.</summary>
public sealed class ErreurLigneCommande(string message) : Exception(message);

/// <summary>Options de la ligne de commande, analysees a la main (aucun package).</summary>
public sealed class OptionsLigneCommande
{
    public List<string> Sources { get; } = [];

    public string? Type { get; set; }

    public int Nombre { get; set; } = 1;

    public int? Graine { get; set; }

    public string? FichierSortie { get; set; }

    public ModeNommage Nommage { get; set; } = ModeNommage.Camel;

    public bool Tableau { get; set; }

    public bool Lister { get; set; }

    public bool Expliquer { get; set; }

    public int ProfondeurMax { get; set; } = 3;

    public double TauxNull { get; set; }

    public bool EnumEnEntier { get; set; }

    public DateTime DatePivot { get; set; } = DateTime.Today;

    public bool Compact { get; set; }

    public bool Aide { get; set; }

    public bool Llm { get; set; }

    public string LlmModele { get; set; } = "qwen2.5:7b";

    public string LlmUrl { get; set; } = "http://localhost:11434";

    public bool LlmSansCache { get; set; }

    public bool LlmAutoriserDistant { get; set; }

    public static OptionsLigneCommande Analyser(string[] args)
    {
        var options = new OptionsLigneCommande();
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            string? valeurInline = null;
            if (argument.StartsWith("--", StringComparison.Ordinal) && argument.IndexOf('=') is var egal && egal > 0)
            {
                valeurInline = argument[(egal + 1)..];
                argument = argument[..egal];
            }

            string Valeur()
            {
                if (valeurInline is not null)
                {
                    return valeurInline;
                }

                if (i + 1 >= args.Length)
                {
                    throw new ErreurLigneCommande($"l'option {argument} attend une valeur");
                }

                return args[++i];
            }

            switch (argument)
            {
                case "--source":
                case "-s":
                    options.Sources.Add(Valeur());
                    break;
                case "--type":
                case "-t":
                    options.Type = Valeur();
                    break;
                case "--count":
                case "-n":
                    options.Nombre = EntierPositif(Valeur(), argument);
                    break;
                case "--seed":
                    options.Graine = Entier(Valeur(), argument);
                    break;
                case "--out":
                case "-o":
                    options.FichierSortie = Valeur();
                    break;
                case "--naming":
                    options.Nommage = Valeur().ToLowerInvariant() switch
                    {
                        "camel" => ModeNommage.Camel,
                        "pascal" => ModeNommage.Pascal,
                        "none" or "aucun" => ModeNommage.Aucun,
                        var autre => throw new ErreurLigneCommande($"--naming : valeur « {autre} » inconnue (camel, pascal, none)"),
                    };
                    break;
                case "--array":
                    options.Tableau = true;
                    break;
                case "--list":
                    options.Lister = true;
                    break;
                case "--explain":
                    options.Expliquer = true;
                    break;
                case "--max-depth":
                    options.ProfondeurMax = EntierPositif(Valeur(), argument);
                    break;
                case "--null-rate":
                {
                    var texte = Valeur().Replace(',', '.');
                    if (!double.TryParse(texte, NumberStyles.Float, CultureInfo.InvariantCulture, out var taux) || taux < 0 || taux > 1)
                    {
                        throw new ErreurLigneCommande("--null-rate attend un nombre entre 0 et 1");
                    }

                    options.TauxNull = taux;
                    break;
                }

                case "--enum-as-int":
                    options.EnumEnEntier = true;
                    break;
                case "--date-pivot":
                {
                    var texte = Valeur();
                    if (!DateTime.TryParseExact(texte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var pivot))
                    {
                        throw new ErreurLigneCommande("--date-pivot attend une date au format yyyy-MM-dd");
                    }

                    options.DatePivot = pivot;
                    break;
                }

                case "--compact":
                    options.Compact = true;
                    break;
                case "--help":
                case "-h":
                case "-?":
                    options.Aide = true;
                    break;
                case "--llm":
                    options.Llm = true;
                    break;
                case "--llm-model":
                    options.LlmModele = Valeur();
                    options.Llm = true;
                    break;
                case "--llm-url":
                    options.LlmUrl = Valeur();
                    options.Llm = true;
                    break;
                case "--llm-no-cache":
                    options.LlmSansCache = true;
                    break;
                case "--llm-allow-remote":
                    options.LlmAutoriserDistant = true;
                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        throw new ErreurLigneCommande($"option inconnue : {argument}");
                    }

                    options.Sources.Add(argument);
                    break;
            }
        }

        return options;
    }

    private static int Entier(string texte, string option) =>
        int.TryParse(texte, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : throw new ErreurLigneCommande($"{option} attend un entier");

    private static int EntierPositif(string texte, string option)
    {
        var valeur = Entier(texte, option);
        return valeur >= 1 ? valeur : throw new ErreurLigneCommande($"{option} attend un entier superieur ou egal a 1");
    }

    public static string TexteAide() => """
        GenerateurJson : documents JSON d'exemple a partir de modeles C# fournis a l'execution,
        en respectant les limites ecrites dans les commentaires du code.

        Usage :
          GenerateurJson --source <fichier.cs|dossier> [--source ...] [--type Nom] [options]
          GenerateurJson           sans argument : moteur (regles ou LLM), source, type, nombre, graine
                                   et sortie sont demandes en console

        Sources et cible :
          --source, -s <chemin>    fichier .cs ou dossier (recursif, bin/ et obj/ ignores) ; repetable ;
                                   un argument sans tiret est aussi une source
          --type, -t <Nom>         type racine (nom simple, Outer.Inner ou nom qualifie) ;
                                   sans --type, l'unique type que personne ne reference est choisi
          --list                   liste les types trouves et s'arrete
          --explain                affiche sur stderr ce qui a ete compris de chaque commentaire

        Generation :
          --count, -n <N>          nombre de documents (defaut 1 : un objet ; sinon un tableau)
          --array                  toujours un tableau, meme pour un seul document
          --seed <N>               graine aleatoire (sinon tiree et affichee sur stderr)
          --date-pivot <yyyy-MM-dd> date de reference pour « passe » / « futur » (defaut : aujourd'hui)
          --naming camel|pascal|none  nommage des proprietes JSON (defaut camel ; [JsonPropertyName] prime)
          --max-depth <N>          profondeur maximale d'objets imbriques (defaut 3, puis null / [])
          --null-rate <0..1>       probabilite de null pour les membres nullables ou optionnels (defaut 0)
          --enum-as-int            enums en entier plutot qu'en chaine
          --compact                JSON sur une ligne
          --out, -o <chemin>       ecrit le JSON dans un fichier (sinon sur stdout ; le reste va sur stderr) ;
                                   dossier existant ou chemin termine par \ : <Type>.json dans ce dossier

        LLM local optionnel (Ollama, tout reste sur ce poste) :
          --llm                    interprete les commentaires avec le modele local au lieu des regles
          --llm-model <nom>        modele Ollama (defaut qwen2.5:7b)
          --llm-url <url>          adresse d'Ollama (defaut http://localhost:11434 ; hote local obligatoire)
          --llm-no-cache           ignore le cache %LOCALAPPDATA%\GenerateurJson\cache-llm.json
          --llm-allow-remote       autorise une adresse non locale (jamais par defaut)

        Codes de sortie : 0 ok, 1 usage, 2 analyse des sources, 3 erreur inattendue, 4 LLM indisponible.

        Exemples :
          GenerateurJson --source exemples/ModelesExemple.cs --list
          GenerateurJson --source exemples/ModelesExemple.cs --type Bobine --explain --seed 42 > NUL
          GenerateurJson --source exemples/ModelesExemple.cs --type Bobine --count 3 --seed 42 --out bobines.json

        """;
}
