using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenerateurJson;

/// <summary>Erreur liee au LLM local (Ollama injoignable, modele absent, reponse invalide) : code de sortie 4.</summary>
public sealed class ErreurLlm(string message) : Exception(message);

/// <summary>
/// Client minimal de l'API locale d'Ollama (HttpClient de la BCL, aucun package). Par construction, il refuse toute
/// adresse qui n'est pas locale : les commentaires et le code ne quittent jamais le poste. Aucun proxy systeme.
/// </summary>
public sealed class ClientOllama : IDisposable
{
    private readonly HttpClient _http;

    public ClientOllama(string url, string modele, bool autoriserDistant)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ErreurLigneCommande($"--llm-url invalide : {url}");
        }

        if (!autoriserDistant && !EstLocal(uri))
        {
            throw new ErreurLigneCommande(
                $"--llm-url pointe vers l'hote « {uri.Host} » : seul un hote local est accepte (localhost, 127.0.0.1, ::1) " +
                "pour que les commentaires et le code restent sur ce poste. Ajoutez --llm-allow-remote pour passer outre en connaissance de cause.");
        }

        Modele = modele;
        Url = uri;
        _http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromSeconds(180),
        };
    }

    public string Modele { get; }

    public Uri Url { get; }

    public static bool EstLocal(Uri uri) => uri.IsLoopback || uri.Host is "localhost" or "127.0.0.1" or "::1" or "[::1]";

    /// <summary>GET /api/tags : Ollama repond et le modele demande est telecharge.</summary>
    public void VerifierDisponibilite()
    {
        JsonNode? reponse;
        try
        {
            using var requete = new HttpRequestMessage(HttpMethod.Get, "api/tags");
            using var http = _http.Send(requete);
            http.EnsureSuccessStatusCode();
            using var flux = http.Content.ReadAsStream();
            reponse = JsonNode.Parse(flux);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            throw new ErreurLlm(
                $"Ollama injoignable a {Url} ({e.Message}).\n" +
                "Installation (une seule fois, gratuit, tout reste en local) : winget install Ollama.Ollama\n" +
                "puis lancer Ollama (icone dans la barre des taches, ou « ollama serve » dans un terminal).");
        }

        var modeles = reponse?["models"]?.AsArray()
            .Select(m => m?["name"]?.GetValue<string>())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList() ?? [];
        var present = modeles.Any(n =>
            n == Modele || n == Modele + ":latest" || (!Modele.Contains(':') && n.StartsWith(Modele + ":", StringComparison.Ordinal)));
        if (!present)
        {
            var disponibles = modeles.Count == 0 ? "aucun" : string.Join(", ", modeles);
            throw new ErreurLlm(
                $"modele « {Modele} » absent d'Ollama (modeles presents : {disponibles}).\n" +
                $"Telechargement unique (quelques Go) : ollama pull {Modele}");
        }
    }

    /// <summary>POST /api/chat sans streaming, avec un schema JSON impose a la reponse (sorties structurees d'Ollama).</summary>
    public string CompleterJson(string systeme, string utilisateur, JsonNode schema)
    {
        var corps = new JsonObject
        {
            ["model"] = Modele,
            ["stream"] = false,
            ["format"] = schema.DeepClone(),
            ["options"] = new JsonObject { ["temperature"] = 0, ["seed"] = 42, ["num_ctx"] = 4096 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = systeme },
                new JsonObject { ["role"] = "user", ["content"] = utilisateur }),
        };
        var json = corps.ToJsonString();

        Exception? derniere = null;
        for (var tentative = 0; tentative < 2; tentative++)
        {
            try
            {
                using var requete = new HttpRequestMessage(HttpMethod.Post, "api/chat")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                using var http = _http.Send(requete);
                using var lecteur = new StreamReader(http.Content.ReadAsStream(), Encoding.UTF8);
                var texte = lecteur.ReadToEnd();
                if (!http.IsSuccessStatusCode)
                {
                    throw new ErreurLlm($"Ollama a repondu {(int)http.StatusCode} : {texte}");
                }

                var contenu = JsonNode.Parse(texte)?["message"]?["content"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(contenu))
                {
                    throw new ErreurLlm("reponse vide d'Ollama");
                }

                return contenu;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException or ErreurLlm)
            {
                derniere = e;
            }
        }

        throw new ErreurLlm($"echec de l'appel a Ollama apres 2 tentatives : {derniere?.Message}");
    }

    public void Dispose() => _http.Dispose();
}
