namespace ModelesExpedition;

// Seconde partie de la classe partielle Expedition (voir Expedition.cs) : ses membres suivent ceux de la premiere.

public sealed partial class Expedition
{
    public ModeTransport Mode { get; set; } // jamais Bateau

    public string Immatriculation { get; set; } = string.Empty; // motif "^[A-Z]{2}-[0-9]{3}-[A-Z]{2}$"

    public List<string> Plombs { get; set; } = []; // 1 a 4 elements, sans doublon; chaque element : 7 chiffres

    public Transporteur Transporteur { get; set; } = new();
}

public enum ModeTransport
{
    Camion,
    Wagon,
    Bateau,
}

/// <summary>Transporteur retenu pour l'expedition.</summary>
public sealed class Transporteur
{
    public string Nom { get; set; } = string.Empty; // max 40 caracteres

    public string? Telephone { get; set; }

    public string Siret { get; set; } = string.Empty; // 14 chiffres
}
