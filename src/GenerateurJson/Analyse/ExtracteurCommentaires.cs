using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GenerateurJson;

/// <summary>
/// Lit les commentaires rattaches a un membre : documentation XML (summary, remarks, param), commentaire de fin
/// de ligne et lignes de commentaire juste au-dessus. Toute la subtilite des trivia Roslyn est concentree ici.
/// </summary>
public static class ExtracteurCommentaires
{
    private static readonly Regex EspacesMultiples = new(@"[ \t]+", RegexOptions.Compiled);

    /// <summary>Ligne de separation ou de titre de section (« ----- Files ----- », « ===== ») : pas un commentaire de membre.</summary>
    private static readonly Regex Banniere = new(@"^\s*[-=*#_]{3,}", RegexOptions.Compiled);

    public static CommentairesMembre Extraire(MemberDeclarationSyntax membre)
    {
        var doc = Documentation(membre);
        return Construire(
            resume: doc is null ? null : Element(doc, "summary"),
            remarques: doc is null ? null : Element(doc, "remarks"),
            paramDoc: null,
            finDeLigne: CommentaireFinDeLigne(membre),
            lignesAuDessus: LignesAuDessus(membre, membre.AttributeLists));
    }

    /// <summary>Parametre positionnel d'un record : le &lt;param&gt; est porte par le record, pas par le parametre.</summary>
    public static CommentairesMembre ExtrairePourParametre(ParameterSyntax parametre, IReadOnlyDictionary<string, string> docParams)
    {
        var doc = Documentation(parametre);
        docParams.TryGetValue(parametre.Identifier.ValueText, out var paramDoc);
        return Construire(
            resume: doc is null ? null : Element(doc, "summary"),
            remarques: doc is null ? null : Element(doc, "remarks"),
            paramDoc: paramDoc,
            finDeLigne: CommentaireFinDeLigne(parametre),
            lignesAuDessus: LignesAuDessus(parametre, parametre.AttributeLists));
    }

    public static string? Resume(SyntaxNode noeud)
    {
        var doc = Documentation(noeud);
        return doc is null ? null : Element(doc, "summary");
    }

    /// <summary>Les &lt;param name="X"&gt; de la documentation d'un type (record positionnel), par nom de parametre.</summary>
    public static IReadOnlyDictionary<string, string> DocParams(SyntaxNode type)
    {
        var resultat = new Dictionary<string, string>(StringComparer.Ordinal);
        var doc = Documentation(type);
        if (doc is null)
        {
            return resultat;
        }

        foreach (var element in doc.Content.OfType<XmlElementSyntax>())
        {
            if (element.StartTag.Name.LocalName.ValueText != "param")
            {
                continue;
            }

            var nom = AttributNom(element.StartTag);
            if (nom is null)
            {
                continue;
            }

            var texte = Normaliser(TexteXml(element.Content));
            if (texte.Length > 0)
            {
                resultat[nom] = texte;
            }
        }

        return resultat;
    }

    private static CommentairesMembre Construire(string? resume, string? remarques, string? paramDoc, string? finDeLigne, string? lignesAuDessus)
    {
        static string? Nettoyer(string? texte) => string.IsNullOrWhiteSpace(texte) ? null : texte;
        return new CommentairesMembre(Nettoyer(resume), Nettoyer(remarques), Nettoyer(paramDoc), Nettoyer(finDeLigne), Nettoyer(lignesAuDessus));
    }

    // ----- Documentation XML -----

    private static DocumentationCommentTriviaSyntax? Documentation(SyntaxNode noeud)
    {
        DocumentationCommentTriviaSyntax? dernier = null;
        foreach (var trivia in noeud.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
                !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                continue;
            }

            if (trivia.HasStructure && trivia.GetStructure() is DocumentationCommentTriviaSyntax structure)
            {
                dernier = structure;
            }
        }

        return dernier;
    }

    private static string? AttributNom(XmlElementStartTagSyntax balise)
    {
        foreach (var attribut in balise.Attributes)
        {
            switch (attribut)
            {
                case XmlNameAttributeSyntax nom:
                    return nom.Identifier.Identifier.ValueText;
                case XmlTextAttributeSyntax texte when texte.Name.LocalName.ValueText == "name":
                    return string.Concat(texte.TextTokens.Select(t => t.ValueText));
            }
        }

        return null;
    }

    private static string? Element(DocumentationCommentTriviaSyntax doc, string nom)
    {
        var sb = new StringBuilder();
        foreach (var element in doc.Content.OfType<XmlElementSyntax>())
        {
            if (element.StartTag.Name.LocalName.ValueText != nom)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(TexteXml(element.Content));
        }

        var texte = Normaliser(sb.ToString());
        return texte.Length == 0 ? null : texte;
    }

    private static string TexteXml(SyntaxList<XmlNodeSyntax> contenu)
    {
        var sb = new StringBuilder();
        foreach (var noeud in contenu)
        {
            AjouterTexte(noeud, sb);
        }

        return sb.ToString();
    }

    // PIEGE : element.ToString() contient les « /// » et l'indentation des lignes de continuation (trivia
    // exterieur). On reconstruit le texte a partir des tokens : ValueText exclut ce trivia et decode les entites.
    // Un retour a la ligne dans un summary est un simple repli de texte : on le remplace par un espace ;
    // seuls <para> et <br/> produisent une vraie fin de phrase.
    private static void AjouterTexte(XmlNodeSyntax noeud, StringBuilder sb)
    {
        switch (noeud)
        {
            case XmlTextSyntax texte:
                foreach (var token in texte.TextTokens)
                {
                    sb.Append(token.IsKind(SyntaxKind.XmlTextLiteralNewLineToken) ? " " : token.ValueText);
                }

                break;

            case XmlCDataSectionSyntax cdata:
                foreach (var token in cdata.TextTokens)
                {
                    sb.Append(token.ValueText);
                }

                break;

            case XmlElementSyntax element:
            {
                var nom = element.StartTag.Name.LocalName.ValueText;
                if (nom is "para" or "item")
                {
                    sb.Append('\n');
                }

                foreach (var enfant in element.Content)
                {
                    AjouterTexte(enfant, sb);
                }

                if (nom is "para")
                {
                    sb.Append('\n');
                }

                break;
            }

            case XmlEmptyElementSyntax vide:
            {
                var nom = vide.Name.LocalName.ValueText;
                if (nom is "br" or "para")
                {
                    sb.Append('\n');
                    break;
                }

                foreach (var attribut in vide.Attributes)
                {
                    switch (attribut)
                    {
                        case XmlCrefAttributeSyntax cref:
                            sb.Append(cref.Cref.ToString());
                            break;
                        case XmlNameAttributeSyntax nomAttribut:
                            sb.Append(nomAttribut.Identifier.Identifier.ValueText);
                            break;
                        case XmlTextAttributeSyntax texteAttribut:
                            sb.Append(string.Concat(texteAttribut.TextTokens.Select(t => t.ValueText)));
                            break;
                    }
                }

                break;
            }
        }
    }

    // ----- Commentaires // et /* */ -----

    // PIEGE : pour « public int X { get; set; } // c » le commentaire est dans le trivia de fin du token « } » ;
    // pour un parametre positionnel « int X, // c » il est sur la virgule, qui appartient a la liste et non au
    // parametre ; pour le dernier parametre « int Y) // c » il est sur la parenthese. Regle unique : partir du
    // dernier token du noeud et, tant que le token suivant est une ponctuation fermante sur la meme ligne,
    // regarder son trivia de fin.
    public static string? CommentaireFinDeLigne(SyntaxNode noeud)
    {
        var token = noeud.GetLastToken();
        if (token.IsKind(SyntaxKind.None))
        {
            return null;
        }

        var ligne = LigneDe(token);
        for (var etape = 0; etape < 4; etape++)
        {
            var texte = TexteCommentaires(token.TrailingTrivia);
            if (texte is not null)
            {
                return texte;
            }

            var suivant = token.GetNextToken();
            if (suivant.IsKind(SyntaxKind.None) || LigneDe(suivant) != ligne || !EstPonctuationFermante(suivant))
            {
                return null;
            }

            token = suivant;
        }

        return null;
    }

    // PIEGE : avec un attribut sur sa propre ligne, un commentaire place entre l'attribut et le membre est dans le
    // trivia du premier token qui suit l'attribut (le modificateur), pas dans celui du noeud. On lit les deux.
    // On remonte ligne par ligne et on s'arrete a la premiere ligne vide ou a un commentaire de documentation.
    public static string? LignesAuDessus(SyntaxNode noeud, SyntaxList<AttributeListSyntax> attributs)
    {
        var trivias = new List<SyntaxTrivia>(noeud.GetLeadingTrivia());
        foreach (var liste in attributs)
        {
            var apres = liste.GetLastToken().GetNextToken();
            if (!apres.IsKind(SyntaxKind.None))
            {
                trivias.AddRange(apres.LeadingTrivia);
            }
        }

        var lignes = new List<string>();
        var finDeLigneVue = false;
        for (var i = trivias.Count - 1; i >= 0; i--)
        {
            var trivia = trivias[i];
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsDirective)
            {
                continue;
            }

            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                if (finDeLigneVue)
                {
                    break;
                }

                finDeLigneVue = true;
                continue;
            }

            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                var ligne = SansBarres(trivia.ToString());
                if (!Banniere.IsMatch(ligne))
                {
                    lignes.Add(ligne);
                }

                finDeLigneVue = false;
                continue;
            }

            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                lignes.Add(NettoyerBloc(trivia.ToString()));
                finDeLigneVue = false;
                continue;
            }

            break;
        }

        if (lignes.Count == 0)
        {
            return null;
        }

        lignes.Reverse();
        var texte = Normaliser(string.Join("\n", lignes));
        return texte.Length == 0 ? null : texte;
    }

    private static string? TexteCommentaires(SyntaxTriviaList trivias)
    {
        List<string>? morceaux = null;
        foreach (var trivia in trivias)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                (morceaux ??= []).Add(SansBarres(trivia.ToString()));
            }
            else if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                (morceaux ??= []).Add(NettoyerBloc(trivia.ToString()));
            }
        }

        if (morceaux is null)
        {
            return null;
        }

        var texte = Normaliser(string.Join("\n", morceaux));
        return texte.Length == 0 ? null : texte;
    }

    private static string SansBarres(string commentaire) =>
        commentaire.StartsWith("//", StringComparison.Ordinal) ? commentaire[2..].TrimStart('/') : commentaire;

    private static string NettoyerBloc(string bloc)
    {
        if (bloc.StartsWith("/*", StringComparison.Ordinal))
        {
            bloc = bloc[2..];
        }

        if (bloc.EndsWith("*/", StringComparison.Ordinal))
        {
            bloc = bloc[..^2];
        }

        var lignes = bloc.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim().TrimStart('*').Trim());
        return string.Join("\n", lignes);
    }

    private static int LigneDe(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line;

    private static bool EstPonctuationFermante(SyntaxToken token) =>
        token.IsKind(SyntaxKind.CommaToken) ||
        token.IsKind(SyntaxKind.CloseParenToken) ||
        token.IsKind(SyntaxKind.SemicolonToken) ||
        token.IsKind(SyntaxKind.CloseBraceToken);

    /// <summary>Lignes nettoyees (espaces compactes, lignes vides retirees), separees par un saut de ligne.</summary>
    private static string Normaliser(string brut)
    {
        var lignes = brut.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(l => EspacesMultiples.Replace(l, " ").Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lignes);
    }
}
