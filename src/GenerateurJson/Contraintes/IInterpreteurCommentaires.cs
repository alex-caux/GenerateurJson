namespace GenerateurJson;

/// <summary>
/// Transforme les commentaires (et attributs) d'un membre en contraintes. Deux implementations : les regles regex
/// (<see cref="InterpreteurContraintes"/>) et le LLM local (<see cref="InterpreteurLlm"/>). Le generateur et le
/// rapport ne connaissent que cette interface.
/// </summary>
public interface IInterpreteurCommentaires
{
    string Nom { get; }

    JeuContraintes Interpreter(DescripteurMembre membre, CatalogueTypes catalogue);
}
