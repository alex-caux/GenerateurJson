namespace GenerateurJson;

public enum ModeNommage
{
    Camel,
    Pascal,
    Aucun,
}

/// <summary>Reglages d'une generation. La date pivot remplace l'horloge pour que deux executions soient identiques.</summary>
public sealed record OptionsGeneration(
    int Graine,
    ModeNommage Nommage,
    int ProfondeurMax,
    double TauxNull,
    bool EnumEnEntier,
    DateTime DatePivot);

/// <summary>
/// Etat partage d'une generation : l'unique source aleatoire (graine fixee), les compteurs sequentiels par membre,
/// la profondeur d'imbrication et les avertissements dedoublonnes. Rien ici ne lit l'horloge ni Random.Shared.
/// </summary>
public sealed class ContexteGeneration(OptionsGeneration options)
{
    private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dejaSignales = new(StringComparer.Ordinal);

    public OptionsGeneration Options { get; } = options;

    public Random Aleatoire { get; } = new(options.Graine);

    public int Profondeur { get; private set; }

    /// <summary>Decalage utilise pour les DateTimeOffset non UTC (fixe, pour la reproductibilite).</summary>
    public TimeSpan DecalageDateTimeOffset { get; } = TimeSpan.FromHours(1);

    public List<string> Avertissements { get; } = [];

    /// <summary>Compteur 1, 2, 3... propre a chaque cle (Type.Membre), partage par tous les documents d'une execution.</summary>
    public long ProchaineSequence(string cle)
    {
        _sequences.TryGetValue(cle, out var valeur);
        valeur++;
        _sequences[cle] = valeur;
        return valeur;
    }

    public void Entrer() => Profondeur++;

    public void Sortir() => Profondeur--;

    public void Avertir(string message)
    {
        if (_dejaSignales.Add(message))
        {
            Avertissements.Add(message);
        }
    }

    public long EntierEntre(long min, long maxInclus)
    {
        if (maxInclus <= min)
        {
            return min;
        }

        return maxInclus == long.MaxValue ? Aleatoire.NextInt64(min, maxInclus) : Aleatoire.NextInt64(min, maxInclus + 1);
    }
}
