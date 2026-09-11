using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GenerateurJson;

/// <summary>Lecture syntaxique des attributs ([Range(0, 100)], [JsonPropertyName("x")]...), sans compilation.</summary>
public static class LecteurAttributs
{
    public static IReadOnlyList<AttributDeclare> Lire(SyntaxList<AttributeListSyntax> listes)
    {
        var resultat = new List<AttributDeclare>();
        foreach (var liste in listes)
        {
            foreach (var attribut in liste.Attributes)
            {
                var positionnels = new List<object?>();
                var nommes = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (attribut.ArgumentList is not null)
                {
                    foreach (var argument in attribut.ArgumentList.Arguments)
                    {
                        var valeur = Evaluer(argument.Expression);
                        if (argument.NameEquals is not null)
                        {
                            nommes[argument.NameEquals.Name.Identifier.ValueText] = valeur;
                        }
                        else if (argument.NameColon is not null)
                        {
                            nommes[argument.NameColon.Name.Identifier.ValueText] = valeur;
                            positionnels.Add(valeur);
                        }
                        else
                        {
                            positionnels.Add(valeur);
                        }
                    }
                }

                resultat.Add(new AttributDeclare(NomAttribut(attribut.Name), positionnels, nommes));
            }
        }

        return resultat;
    }

    /// <summary>Identifiant le plus a droite, sans le suffixe Attribute : System.ComponentModel.DataAnnotations.RangeAttribute -> Range.</summary>
    public static string NomAttribut(NameSyntax nom)
    {
        var texte = nom switch
        {
            QualifiedNameSyntax qualifie => qualifie.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => nom.ToString(),
        };
        return texte.Length > 9 && texte.EndsWith("Attribute", StringComparison.Ordinal) ? texte[..^9] : texte;
    }

    /// <summary>Litteraux, nombres negatifs, typeof(T) (nom du type), nameof(x) ("x"), A.B (B) ; sinon le texte brut.</summary>
    public static object? Evaluer(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax litteral:
                return litteral.Token.Value;
            case PrefixUnaryExpressionSyntax moins when moins.IsKind(SyntaxKind.UnaryMinusExpression):
                return Negatif(Evaluer(moins.Operand));
            case TypeOfExpressionSyntax typeOf:
                return typeOf.Type.ToString();
            case InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } nameOf:
                return nameOf.ArgumentList.Arguments.Count == 1 ? nameOf.ArgumentList.Arguments[0].ToString() : null;
            case MemberAccessExpressionSyntax acces:
                return acces.Name.Identifier.ValueText;
            case ParenthesizedExpressionSyntax parenthese:
                return Evaluer(parenthese.Expression);
            default:
                return expression.ToString();
        }
    }

    private static object? Negatif(object? valeur) => valeur switch
    {
        int i => -i,
        long l => -l,
        double d => -d,
        float f => -f,
        decimal m => -m,
        _ => null,
    };

    public static string? NomJson(IReadOnlyList<AttributDeclare> attributs) =>
        attributs.FirstOrDefault(a => a.Nom == "JsonPropertyName")?.Positionnels.FirstOrDefault() as string;

    /// <summary>[JsonIgnore] sans condition, ou avec une condition autre que Never.</summary>
    public static bool EstIgnore(IReadOnlyList<AttributDeclare> attributs)
    {
        var ignore = attributs.FirstOrDefault(a => a.Nom == "JsonIgnore");
        if (ignore is null)
        {
            return false;
        }

        return !(ignore.Nommes.TryGetValue("Condition", out var condition) && condition is "Never");
    }
}
