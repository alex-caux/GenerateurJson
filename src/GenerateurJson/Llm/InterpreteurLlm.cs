using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GenerateurJson;

/// <summary>
/// Interpretation des commentaires par le LLM local : un appel par membre commente, reponse structuree validee par
/// le moteur deterministe avant d'entrer dans le jeu de contraintes (memes records, meme fusion, meme rapport).
/// </summary>
public sealed class InterpreteurLlm(ClientOllama client, CacheLlm cache, TextWriter journal) : IInterpreteurCommentaires
{
    private int _appels;

    /// <summary>Nombre de membres commentes a traiter, pour l'affichage de la progression.</summary>
    public int Total { get; set; }

    public int Appels => _appels;

    public string Nom => "llm " + client.Modele;

    public JeuContraintes Interpreter(DescripteurMembre membre, CatalogueTypes catalogue)
    {
        var jeu = new JeuContraintes();
        ContraintesCommunes.AjouterAttributsEtType(membre, jeu);
        if (membre.Commentaires.EstVide)
        {
            return jeu;
        }

        var message = ConstruireMessage(membre);
        var cle = CacheLlm.Cle(client.Modele, SchemaContraintesLlm.Version, membre.Type.TexteOriginal, message);
        var reponse = cache.Obtenir(cle);
        if (reponse is null)
        {
            _appels++;
            journal.WriteLine($"llm {_appels}/{Math.Max(Total, _appels)} {membre.NomQualifie}");
            reponse = client.CompleterJson(SchemaContraintesLlm.PromptSysteme, message, SchemaContraintesLlm.Schema());
            cache.Enregistrer(cle, reponse);
        }

        foreach (var resultat in Convertir(reponse, membre, jeu.Avertissements))
        {
            ContraintesCommunes.AjouterAvecCible(jeu, membre.Type, resultat);
        }

        return jeu;
    }

    private static string ConstruireMessage(DescripteurMembre membre)
    {
        var sb = new StringBuilder();
        sb.Append("Membre : ").AppendLine(membre.Nom);
        sb.Append("Type C# : ").Append(membre.Type.TexteOriginal).Append(" (genre : ").Append(membre.Type.GenreCible);
        if (membre.Type.EstConteneur)
        {
            sb.Append(", collection dont les elements sont de ce genre");
        }

        sb.AppendLine(")");
        var cible = membre.Type.TypeCible;
        if (cible.Genre == GenreValeur.Enum && cible.Declaration is not null)
        {
            sb.Append("Membres de l'enum ").Append(cible.Declaration.NomSimple).Append(" : ")
                .AppendLine(string.Join(", ", cible.Declaration.MembresEnum.Select(m => m.Nom)));
        }

        sb.AppendLine("Commentaires :");
        foreach (var (source, texte) in membre.Commentaires.Sources())
        {
            sb.Append("- (").Append(source).Append(") « ").Append(texte.Replace("\n", " / ", StringComparison.Ordinal)).AppendLine(" »");
        }

        return sb.ToString();
    }

    private static IEnumerable<ResultatRegle> Convertir(string reponse, DescripteurMembre membre, List<string> avertissements)
    {
        JsonObject? objet;
        try
        {
            objet = JsonNode.Parse(reponse) as JsonObject;
        }
        catch (JsonException)
        {
            objet = null;
        }

        if (objet is null)
        {
            avertissements.Add("llm : reponse illisible, commentaires ignores");
            yield break;
        }

        const string source = "llm";
        var type = membre.Type;
        var cibleTexte = Texte(objet["cible"]);
        var cible = cibleTexte == "element" ? CibleContrainte.Element : CibleContrainte.Auto;

        if (objet["plage"] is JsonObject plage)
        {
            var min = Nombre(plage["min"]);
            var max = Nombre(plage["max"]);
            if (min is not null || max is not null)
            {
                yield return new ResultatRegle(new Plage(min, max, Booleen(plage["minExclusif"]), Booleen(plage["maxExclusif"])) { Source = source }, cible);
            }
        }

        if (objet["longueurTexte"] is JsonObject longueur)
        {
            var min = Entier(longueur["min"]);
            var max = Entier(longueur["max"]);
            if (min is not null || max is not null)
            {
                yield return new ResultatRegle(new LongueurTexte(min, max) { Source = source }, cible);
            }
        }

        if (objet["tailleCollection"] is JsonObject taille)
        {
            var min = Entier(taille["min"]);
            var max = Entier(taille["max"]);
            if (min is not null || max is not null)
            {
                yield return new ResultatRegle(new TailleCollection(min, max) { Source = source }, CibleContrainte.Membre);
            }
        }

        var autorisees = ContraintesCommunes.FiltrerValeurs(Textes(objet["valeursAutorisees"]), type.TypeCible, avertissements, source);
        if (autorisees.Count > 0)
        {
            yield return new ResultatRegle(new ValeursAutorisees(autorisees) { Source = source }, cible);
        }

        var exclues = ContraintesCommunes.FiltrerValeurs(Textes(objet["valeursExclues"]), type.TypeCible, avertissements, source);
        if (exclues.Count > 0)
        {
            yield return new ResultatRegle(new ValeursExclues(exclues) { Source = source }, cible);
        }

        foreach (var format in Textes(objet["formats"]))
        {
            if (Enum.TryParse<GenreFormat>(format, ignoreCase: true, out var genre))
            {
                yield return new ResultatRegle(new Format(genre) { Source = source }, cible);
            }
            else
            {
                avertissements.Add($"llm : format « {format} » inconnu, ignore");
            }
        }

        if (Texte(objet["formatDate"]) is { Length: > 0 } formatDate)
        {
            if (NormaliseurTexte.EstFormatDate(formatDate, 1))
            {
                yield return new ResultatRegle(new FormatDate(formatDate) { Source = source }, cible);
            }
            else
            {
                avertissements.Add($"llm : format de date « {formatDate} » invalide, ignore");
            }
        }

        if (Texte(objet["motif"]) is { Length: > 0 } motif)
        {
            if (RegexValide(motif))
            {
                yield return new ResultatRegle(new Motif(motif) { Source = source }, cible);
            }
            else
            {
                avertissements.Add($"llm : expression reguliere « {motif} » invalide, ignoree");
            }
        }

        switch (Texte(objet["presence"]))
        {
            case "obligatoire":
                yield return new ResultatRegle(new Presence(true) { Source = source }, CibleContrainte.Membre);
                break;
            case "optionnel":
                yield return new ResultatRegle(new Presence(false) { Source = source }, CibleContrainte.Membre);
                break;
        }

        if (Entier(objet["decimales"]) is { } decimales && decimales >= 0)
        {
            yield return new ResultatRegle(new Decimales(decimales) { Source = source }, cible);
        }

        if (Nombre(objet["multiple"]) is { } multiple && multiple > 0)
        {
            yield return new ResultatRegle(new Multiple(multiple) { Source = source }, cible);
        }

        switch (Texte(objet["parite"]))
        {
            case "pair":
                yield return new ResultatRegle(new Parite(true) { Source = source }, cible);
                break;
            case "impair":
                yield return new ResultatRegle(new Parite(false) { Source = source }, cible);
                break;
        }

        if (objet["valeurFixe"] is JsonObject fixe && Texte(fixe["texte"]) is { Length: > 0 } texteFixe)
        {
            yield return new ResultatRegle(new ValeurFixe(texteFixe, Booleen(fixe["parDefaut"])) { Source = source }, cible);
        }

        switch (Texte(objet["unicite"]))
        {
            case "unique":
                yield return new ResultatRegle(new Unicite(false) { Source = source }, cible);
                break;
            case "sequentiel":
                yield return new ResultatRegle(new Unicite(true) { Source = source }, cible);
                break;
        }

        switch (Texte(objet["temporalite"]))
        {
            case "passe":
                yield return new ResultatRegle(new Temporalite(GenreTemporalite.Passe) { Source = source }, cible);
                break;
            case "futur":
                yield return new ResultatRegle(new Temporalite(GenreTemporalite.Futur) { Source = source }, cible);
                break;
            case "aujourdhui":
                yield return new ResultatRegle(new Temporalite(GenreTemporalite.Aujourdhui) { Source = source }, cible);
                break;
        }

        if (objet["plageDates"] is JsonObject plageDates)
        {
            var min = Date(plageDates["min"]);
            var max = Date(plageDates["max"]);
            if (min is not null || max is not null)
            {
                yield return new ResultatRegle(new PlageDates(min, max) { Source = source }, cible);
            }
        }

        if (Booleen(objet["elementsDistincts"]))
        {
            yield return new ResultatRegle(new ElementsDistincts { Source = source }, CibleContrainte.Membre);
        }

        if (Texte(objet["unite"]) is { Length: > 0 } unite)
        {
            yield return new ResultatRegle(new Unite(unite) { Source = source }, cible);
            var symbole = unite.Trim().ToLowerInvariant();
            if (type.TypeCible.EstNumerique && symbole is "%" or "pct" or "pourcent" or "percent")
            {
                yield return new ResultatRegle(new Plage(0, 100) { Origine = OrigineContrainte.Nom, Source = "unite %" }, cible);
            }
        }

        foreach (var note in Textes(objet["notes"]))
        {
            yield return new ResultatRegle(new Note(note) { Source = source }, CibleContrainte.Membre);
        }

        foreach (var phrase in Textes(objet["nonCompris"]))
        {
            avertissements.Add($"phrase non reconnue (llm) : « {phrase} »");
        }
    }

    private static string? Texte(JsonNode? noeud)
    {
        try
        {
            return noeud is JsonValue valeur ? valeur.GetValue<string>() : null;
        }
        catch (InvalidOperationException)
        {
            return noeud?.ToJsonString();
        }
    }

    private static IReadOnlyList<string> Textes(JsonNode? noeud) =>
        noeud is JsonArray tableau
            ? tableau.Select(Texte).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList()
            : [];

    private static decimal? Nombre(JsonNode? noeud)
    {
        if (noeud is not JsonValue valeur)
        {
            return null;
        }

        try
        {
            return valeur.GetValue<decimal>();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or OverflowException)
        {
            return Texte(noeud) is { } texte ? NormaliseurTexte.Nombre(texte) : null;
        }
    }

    private static int? Entier(JsonNode? noeud) => NormaliseurTexte.Entier(Nombre(noeud));

    private static bool Booleen(JsonNode? noeud)
    {
        if (noeud is not JsonValue valeur)
        {
            return false;
        }

        try
        {
            return valeur.GetValue<bool>();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            return Texte(noeud)?.ToLowerInvariant() is "true" or "vrai" or "oui";
        }
    }

    private static DateTime? Date(JsonNode? noeud)
    {
        var texte = Texte(noeud);
        if (string.IsNullOrWhiteSpace(texte))
        {
            return null;
        }

        return NormaliseurTexte.Date(texte)
               ?? (DateTime.TryParse(texte, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null);
    }

    private static bool RegexValide(string motif)
    {
        try
        {
            _ = new Regex(motif);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
