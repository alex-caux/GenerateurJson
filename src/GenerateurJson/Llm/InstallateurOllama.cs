using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenerateurJson;

/// <summary>
/// Rend le LLM local utilisable depuis le mode interactif : installe Ollama s'il est absent (installeur officiel publie
/// sur GitHub, empreinte SHA256 verifiee, installation silencieuse par utilisateur sans droits administrateur ; a
/// defaut winget), le demarre, puis lui fait telecharger le modele. Chaque telechargement est confirme au prealable et
/// suivi en pourcentage : ce sont les seules sorties vers Internet, ensuite tout reste sur le poste. Windows seulement.
/// </summary>
public sealed class InstallateurOllama(TextWriter journal)
{
    private const string UrlDerniereVersion = "https://api.github.com/repos/ollama/ollama/releases/latest";

    private const string NomInstalleur = "OllamaSetup.exe";

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly Dictionary<string, string> TaillesConnues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["qwen2.5:7b"] = "environ 4,7 Go",
        ["qwen2.5:3b"] = "environ 1,9 Go",
    };

    private readonly object _verrou = new();

    /// <summary>
    /// Verifie Ollama et le modele, propose d'installer ou de telecharger ce qui manque. Vrai si le modele est pret ;
    /// faux si l'utilisateur refuse ou si une etape echoue (la raison est deja ecrite sur le journal).
    /// </summary>
    public bool Preparer(string url, string modele, Func<string, bool> confirmer)
    {
        using var client = new ClientOllama(url, modele, autoriserDistant: false);
        var modeles = ModelesSiJoignable(client) ?? Demarrer(client, confirmer);
        if (modeles is null)
        {
            return false;
        }

        if (client.ModelePresent(modeles))
        {
            return true;
        }

        journal.WriteLine($"  Le modele {modele} n'est pas encore telecharge.");
        if (!confirmer($"Le telecharger maintenant ({TaillesConnues.GetValueOrDefault(modele, "plusieurs Go selon le modele")}, une seule fois)"))
        {
            return false;
        }

        TelechargerModele(client);
        if (client.ModelePresent(client.Modeles()))
        {
            return true;
        }

        journal.WriteLine($"  Le modele {modele} n'apparait pas dans Ollama apres le telechargement.");
        return false;
    }

    /// <summary>
    /// ollama.exe dans le PATH du processus, de l'utilisateur ou de la machine (une installation toute fraiche n'est pas
    /// encore dans celui du processus), ou dans le dossier d'installation par defaut. Null s'il est introuvable.
    /// </summary>
    public static string? TrouverExecutable()
    {
        var dossiers = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            .SelectMany(cible => (Environment.GetEnvironmentVariable("PATH", cible) ?? string.Empty).Split(Path.PathSeparator))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama"));
        foreach (var dossier in dossiers)
        {
            if (string.IsNullOrWhiteSpace(dossier))
            {
                continue;
            }

            try
            {
                var candidat = Path.Combine(Environment.ExpandEnvironmentVariables(dossier.Trim().Trim('"')), "ollama.exe");
                if (File.Exists(candidat))
                {
                    return candidat;
                }
            }
            catch (ArgumentException)
            {
                // entree du PATH mal formee : ignoree
            }
        }

        return null;
    }

    /// <summary>Installe au besoin puis demarre Ollama ; renvoie ses modeles une fois qu'il repond, null sinon.</summary>
    private IReadOnlyList<string>? Demarrer(ClientOllama client, Func<string, bool> confirmer)
    {
        var executable = TrouverExecutable();
        if (executable is null)
        {
            journal.WriteLine("  Ollama n'est pas installe sur ce poste.");
            if (!OperatingSystem.IsWindows())
            {
                journal.WriteLine("  Installation automatique prevue pour Windows seulement : voir https://ollama.com/download");
                return null;
            }

            if (!confirmer("L'installer maintenant (logiciel libre et gratuit, environ 1,6 Go a telecharger, sans droits administrateur)"))
            {
                return null;
            }

            executable = Installer();
            if (executable is null)
            {
                journal.WriteLine("  Installation non aboutie. A faire a la main : https://ollama.com/download");
                return null;
            }

            // L'installeur lance lui-meme l'application Ollama : lui laisser le temps de repondre.
            if (Attendre(client, TimeSpan.FromSeconds(15)) is { } dejaDemarre)
            {
                journal.WriteLine("  Ollama demarre.");
                return dejaDemarre;
            }
        }

        journal.WriteLine("  Demarrage d'Ollama...");
        Lancer(executable);
        var modeles = Attendre(client, TimeSpan.FromSeconds(60));
        journal.WriteLine(modeles is null
            ? "  Ollama ne repond toujours pas apres 60 s : lancez l'application Ollama, puis reessayez."
            : "  Ollama demarre.");
        return modeles;
    }

    /// <summary>
    /// Installeur officiel depuis GitHub d'abord (avancement en %, empreinte publiee verifiee), sinon winget (qui verifie
    /// aussi l'empreinte, mais n'affiche sa barre de progression que dans une vraie console) ; chemin de ollama.exe, ou null.
    /// </summary>
    private string? Installer()
    {
        if (InstallerDepuisGitHub() && TrouverExecutable() is { } parGitHub)
        {
            return parGitHub;
        }

        journal.WriteLine("  Repli sur winget (paquet Ollama.Ollama)...");
        var code = Executer(
            "winget",
            "install --id Ollama.Ollama --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            console: !Console.IsOutputRedirected);
        if (TrouverExecutable() is { } parWinget)
        {
            return parWinget;
        }

        journal.WriteLine(code is null ? "  winget est absent de ce poste." : $"  winget n'a pas abouti (code 0x{code.Value:X8}).");
        return null;
    }

    /// <summary>Derniere version publiee sur GitHub : telechargement suivi, empreinte verifiee, installation silencieuse.</summary>
    private bool InstallerDepuisGitHub()
    {
        journal.WriteLine("  Recherche de la derniere version d'Ollama sur GitHub...");
        var publication = DerniereVersion();
        if (publication is null)
        {
            return false;
        }

        var (version, url, taille, empreinte) = publication.Value;
        var chemin = Path.Combine(Path.GetTempPath(), $"OllamaSetup-{version}.exe");
        journal.WriteLine($"  Telechargement d'Ollama {version} : {url}");
        if (!TelechargerVerifie(url, taille, empreinte, chemin, "Ollama " + version))
        {
            return false;
        }

        journal.WriteLine("  Installation silencieuse (une a deux minutes)...");
        var code = Executer(chemin, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-", console: false);
        Supprimer(chemin);
        if (code == 0)
        {
            return true;
        }

        journal.WriteLine(code is null ? "  Installeur introuvable apres telechargement." : $"  L'installeur a echoue (code {code}).");
        return false;
    }

    /// <summary>API GitHub : version, adresse, taille et empreinte SHA256 de l'installeur Windows ; null si indisponible.</summary>
    private (string Version, string Url, long Taille, string? Empreinte)? DerniereVersion()
    {
        try
        {
            using var http = NouveauClient(TimeSpan.FromSeconds(30));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var requete = new HttpRequestMessage(HttpMethod.Get, UrlDerniereVersion);
            using var reponse = http.Send(requete);
            reponse.EnsureSuccessStatusCode();
            using var flux = reponse.Content.ReadAsStream();
            var publication = JsonNode.Parse(flux);
            var fichier = publication?["assets"]?.AsArray().FirstOrDefault(a => a?["name"]?.GetValue<string>() == NomInstalleur);
            var url = fichier?["browser_download_url"]?.GetValue<string>();
            if (fichier is null || url is null)
            {
                journal.WriteLine($"  {NomInstalleur} absent de la derniere publication GitHub.");
                return null;
            }

            // « sha256:<hex> », publie par GitHub pour les fichiers deposes depuis 2025.
            var digest = fichier["digest"]?.GetValue<string>();
            var empreinte = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest["sha256:".Length..] : null;
            return (publication?["tag_name"]?.GetValue<string>() ?? "?", url, fichier["size"]?.GetValue<long>() ?? 0, empreinte);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidOperationException)
        {
            journal.WriteLine($"  GitHub injoignable : {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Telecharge <paramref name="url"/> dans <paramref name="fichier"/> avec l'avancement en %, en calculant l'empreinte
    /// au fil de l'eau ; un fichier dont l'empreinte differe de celle publiee est supprime et refuse.
    /// </summary>
    private bool TelechargerVerifie(string url, long taille, string? empreinte, string fichier, string libelle)
    {
        try
        {
            using var http = NouveauClient(System.Threading.Timeout.InfiniteTimeSpan);
            using var requete = new HttpRequestMessage(HttpMethod.Get, url);
            using var reponse = http.Send(requete, HttpCompletionOption.ResponseHeadersRead);
            reponse.EnsureSuccessStatusCode();
            var total = reponse.Content.Headers.ContentLength ?? taille;
            var avancement = new Avancement(journal, libelle);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var source = reponse.Content.ReadAsStream())
            using (var cible = File.Create(fichier))
            {
                var tampon = new byte[1 << 16];
                long recu = 0;
                int lus;
                while ((lus = source.Read(tampon, 0, tampon.Length)) > 0)
                {
                    cible.Write(tampon, 0, lus);
                    sha.AppendData(tampon, 0, lus);
                    recu += lus;
                    avancement.Afficher(recu, total);
                }
            }

            if (empreinte is null)
            {
                journal.WriteLine("  Empreinte non publiee : fichier non verifie (telecharge en HTTPS).");
                return true;
            }

            if (string.Equals(Convert.ToHexString(sha.GetHashAndReset()), empreinte, StringComparison.OrdinalIgnoreCase))
            {
                journal.WriteLine("  Empreinte SHA256 verifiee.");
                return true;
            }

            journal.WriteLine("  Empreinte SHA256 differente de celle publiee : fichier supprime.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            journal.WriteLine($"  Telechargement impossible : {e.Message}");
        }

        Supprimer(fichier);
        return false;
    }

    /// <summary>Client pour Internet : proxy systeme (reseau d'entreprise) et User-Agent, exige par l'API GitHub.</summary>
    private static HttpClient NouveauClient(TimeSpan delai)
    {
        var http = new HttpClient { Timeout = delai };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GenerateurJson");
        return http;
    }

    private static void Supprimer(string fichier)
    {
        try
        {
            File.Delete(fichier);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // fichier temporaire : sans importance
        }
    }

    /// <summary>
    /// Application Ollama (icone de la barre des taches, qui lance le serveur) si elle est la, sinon « ollama serve » en
    /// fenetre cachee. Par le shell : le serveur n'herite ni de la console ni de stdout (ou part le JSON), et continue de
    /// tourner apres la fin du programme, comme l'application Ollama.
    /// </summary>
    private void Lancer(string executable)
    {
        var application = Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, "ollama app.exe");
        var info = File.Exists(application)
            ? new ProcessStartInfo(application) { UseShellExecute = true }
            : new ProcessStartInfo(executable, "serve") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Win32Exception e)
        {
            journal.WriteLine($"  Demarrage impossible ({info.FileName}) : {e.Message}");
        }
    }

    private void TelechargerModele(ClientOllama client)
    {
        journal.WriteLine($"  Telechargement de {client.Modele} par Ollama...");
        var avancements = new Dictionary<string, Avancement>(StringComparer.Ordinal);
        var dernierStatut = string.Empty;
        client.TelechargerModele((statut, recu, total) =>
        {
            if (total >= 50_000_000)
            {
                // Couche lourde (les poids du modele) : avancement chiffre. Les petites couches passent sans bruit.
                if (!avancements.TryGetValue(statut, out var avancement))
                {
                    avancements[statut] = avancement = new Avancement(journal, "modele");
                }

                avancement.Afficher(recu, total);
            }
            else if (total == 0 && statut != dernierStatut && !statut.StartsWith("pulling", StringComparison.Ordinal))
            {
                journal.WriteLine("    " + statut);
            }

            dernierStatut = statut;
        });
    }

    private static IReadOnlyList<string>? Attendre(ClientOllama client, TimeSpan delai)
    {
        var chrono = Stopwatch.StartNew();
        while (true)
        {
            if (ModelesSiJoignable(client) is { } modeles)
            {
                return modeles;
            }

            if (chrono.Elapsed >= delai)
            {
                return null;
            }

            Thread.Sleep(500);
        }
    }

    private static IReadOnlyList<string>? ModelesSiJoignable(ClientOllama client)
    {
        try
        {
            return client.Modeles();
        }
        catch (ErreurLlm)
        {
            return null;
        }
    }

    /// <summary>
    /// Lance un programme et attend sa fin ; code de sortie, ou null s'il est introuvable. <paramref name="console"/> :
    /// le programme ecrit directement dans la console (barre de progression de winget) ; sinon sa sortie est relayee
    /// sur le journal, jamais sur stdout ou part le JSON, avec un signe de vie toutes les 15 s s'il reste muet.
    /// </summary>
    private int? Executer(string fichier, string arguments, bool console)
    {
        var info = new ProcessStartInfo(fichier, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = !console,
            RedirectStandardOutput = !console,
            RedirectStandardError = !console,
        };
        if (!console)
        {
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
        }

        try
        {
            using var processus = new Process { StartInfo = info };
            if (!console)
            {
                processus.OutputDataReceived += (_, e) => Relayer(e.Data);
                processus.ErrorDataReceived += (_, e) => Relayer(e.Data);
            }

            processus.Start();
            if (!console)
            {
                processus.BeginOutputReadLine();
                processus.BeginErrorReadLine();
                var chrono = Stopwatch.StartNew();
                while (!processus.WaitForExit(15_000))
                {
                    lock (_verrou)
                    {
                        journal.WriteLine($"    en cours depuis {chrono.Elapsed.TotalSeconds:0} s...");
                    }
                }
            }

            processus.WaitForExit();
            return processus.ExitCode;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Barres de progression reecrites par \r : seul le dernier etat de la ligne est garde ; les spinners sont ignores.</summary>
    private void Relayer(string? ligne)
    {
        var texte = ligne?.Split('\r').LastOrDefault(s => s.Any(char.IsLetterOrDigit))?.Trim();
        if (string.IsNullOrEmpty(texte))
        {
            return;
        }

        lock (_verrou)
        {
            journal.WriteLine("    " + texte);
        }
    }

    private static string Taille(long octets) =>
        octets >= 1_000_000_000
            ? (octets / 1e9).ToString("0.0", Francais) + " Go"
            : (octets / 1e6).ToString("0", Francais) + " Mo";

    /// <summary>Avancement d'un telechargement sur le journal : une ligne toutes les 3 s au plus, et une a la fin.</summary>
    private sealed class Avancement(TextWriter sortie, string libelle)
    {
        private readonly Stopwatch _chrono = Stopwatch.StartNew();
        private TimeSpan? _derniere;
        private bool _fini;

        public void Afficher(long recu, long total)
        {
            var fini = total > 0 && recu >= total;
            if (_fini || (!fini && _derniere is { } derniere && _chrono.Elapsed - derniere < TimeSpan.FromSeconds(3)))
            {
                return;
            }

            _derniere = _chrono.Elapsed;
            _fini = fini;
            sortie.WriteLine(total > 0
                ? $"    {libelle} : {100 * recu / total} % ({Taille(recu)} / {Taille(total)})"
                : $"    {libelle} : {Taille(recu)}");
        }
    }
}
