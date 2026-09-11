using System.Globalization;
using System.Text;

namespace GenerateurJson;

/// <summary>Rapport « --explain » : pour chaque type atteignable et chaque membre, les commentaires bruts et ce qui en a ete compris.</summary>
public static class RapportExplication
{
    public static string Construire(CatalogueTypes catalogue, DescripteurType racine, CalculateurContraintes contraintes)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Interpretation des commentaires (moteur : {contraintes.Interpreteur.Nom}, racine : {racine.NomComplet})");

        var types = catalogue.TypesAtteignables(racine)
            .OrderBy(t => t == racine ? 0 : 1)
            .ThenBy(t => t.NomComplet, StringComparer.Ordinal);
        foreach (var type in types)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"== {type.NomComplet} ({Nature(type)}, {type.Emplacement}) ==");
            if (type.Resume is not null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"   {type.Resume.Replace("\n", " / ", StringComparison.Ordinal)}");
            }

            if (type.Genre == GenreDeclaration.Enum)
            {
                sb.AppendLine("   membres : " + string.Join(", ", type.MembresEnum.Select(m => $"{m.Nom}={m.Valeur}")));
                continue;
            }

            foreach (var membre in catalogue.MembresEffectifs(type))
            {
                var jeu = contraintes.Pour(membre);
                var herite = membre.TypeDeclarant != type.NomComplet ? $" (herite de {membre.TypeDeclarant})" : string.Empty;
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {membre.Nom} : {membre.Type.TexteOriginal}{herite}");
                foreach (var (source, texte) in membre.Commentaires.Sources())
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    commentaire ({source}) : « {texte.Replace("\n", " / ", StringComparison.Ordinal)} »");
                }

                foreach (var attribut in membre.Attributs)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    attribut : [{attribut.Nom}{Arguments(attribut)}]");
                }

                Ecrire(sb, jeu, "    ");
                if (jeu.AElement)
                {
                    sb.AppendLine("    elements :");
                    Ecrire(sb, jeu.Element, "      ");
                }

                if (!jeu.Effectives().Any() && !jeu.AElement && jeu.Notes.Count == 0)
                {
                    sb.AppendLine("    (aucune contrainte : valeur libre)");
                }
            }
        }

        return sb.ToString();
    }

    private static void Ecrire(StringBuilder sb, JeuContraintes jeu, string retrait)
    {
        foreach (var contrainte in jeu.Effectives())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{retrait}-> {contrainte.Decrire()}  [{contrainte.Etiquette}]");
        }

        foreach (var note in jeu.Notes)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{retrait}~ {note}");
        }

        foreach (var avertissement in jeu.Avertissements)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{retrait}! {avertissement}");
        }
    }

    private static string Nature(DescripteurType type) => type.Genre switch
    {
        GenreDeclaration.Classe => type.EstAbstrait ? "classe abstraite" : "classe",
        GenreDeclaration.Record => "record",
        GenreDeclaration.Struct => "struct",
        GenreDeclaration.RecordStruct => "record struct",
        GenreDeclaration.Interface => "interface",
        _ => "enum",
    };

    private static string Arguments(AttributDeclare attribut)
    {
        var parties = attribut.Positionnels.Select(Formater)
            .Concat(attribut.Nommes.Select(n => $"{n.Key} = {Formater(n.Value)}"))
            .ToList();
        return parties.Count == 0 ? string.Empty : "(" + string.Join(", ", parties) + ")";
    }

    private static string Formater(object? valeur) => valeur switch
    {
        null => "null",
        string texte => "\"" + texte + "\"",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => valeur.ToString() ?? string.Empty,
    };
}
