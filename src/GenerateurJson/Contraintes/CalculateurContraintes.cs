namespace GenerateurJson;

/// <summary>
/// Calcule et memorise les contraintes d'un membre : interpreteur choisi (regles ou LLM) puis indices tires du nom.
/// Partage entre le rapport et le generateur, pour que le LLM ne soit appele qu'une fois par membre.
/// </summary>
public sealed class CalculateurContraintes(CatalogueTypes catalogue, IInterpreteurCommentaires interpreteur)
{
    private readonly Dictionary<DescripteurMembre, JeuContraintes> _cache = [];

    public IInterpreteurCommentaires Interpreteur => interpreteur;

    /// <summary>Membres deja interpretes, avec leur jeu de contraintes (pour remonter les avertissements).</summary>
    public IEnumerable<(DescripteurMembre Membre, JeuContraintes Jeu)> Calcules => _cache.Select(p => (p.Key, p.Value));

    public JeuContraintes Pour(DescripteurMembre membre)
    {
        if (_cache.TryGetValue(membre, out var deja))
        {
            return deja;
        }

        var jeu = interpreteur.Interpreter(membre, catalogue);
        IndicesNom.Appliquer(membre.Nom, membre.Type, jeu);
        _cache[membre] = jeu;
        return jeu;
    }
}
