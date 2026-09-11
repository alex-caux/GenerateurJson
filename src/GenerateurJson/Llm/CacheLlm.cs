using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GenerateurJson;

/// <summary>
/// Cache des reponses du LLM, sur le poste (%LOCALAPPDATA%\GenerateurJson\cache-llm.json). Cle : empreinte SHA-256
/// du modele, de la version du prompt, du type C# et du texte des commentaires. Une execution identique ne rappelle
/// jamais le modele et reste donc instantanee et reproductible.
/// </summary>
public sealed class CacheLlm
{
    private readonly Dictionary<string, string> _entrees = new(StringComparer.Ordinal);
    private readonly bool _actif;
    private bool _modifie;

    public CacheLlm(string? chemin, bool actif)
    {
        _actif = actif;
        Chemin = chemin ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenerateurJson", "cache-llm.json");
        if (!actif || !File.Exists(Chemin))
        {
            return;
        }

        try
        {
            var lu = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Chemin, Encoding.UTF8));
            if (lu is not null)
            {
                foreach (var (cle, valeur) in lu)
                {
                    _entrees[cle] = valeur;
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Cache illisible : on repart de zero, il sera reecrit.
        }
    }

    public string Chemin { get; }

    public int Nombre => _entrees.Count;

    public static string Cle(params string[] parties) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("", parties))));

    public string? Obtenir(string cle) => _actif && _entrees.TryGetValue(cle, out var valeur) ? valeur : null;

    public void Enregistrer(string cle, string valeur)
    {
        if (!_actif)
        {
            return;
        }

        _entrees[cle] = valeur;
        _modifie = true;
    }

    public void Sauvegarder()
    {
        if (!_actif || !_modifie)
        {
            return;
        }

        var dossier = Path.GetDirectoryName(Chemin);
        if (!string.IsNullOrEmpty(dossier))
        {
            Directory.CreateDirectory(dossier);
        }

        File.WriteAllText(Chemin, JsonSerializer.Serialize(_entrees, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        _modifie = false;
    }
}
