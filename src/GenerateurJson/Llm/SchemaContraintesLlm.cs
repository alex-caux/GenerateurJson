using System.Text.Json.Nodes;

namespace GenerateurJson;

/// <summary>Le contrat impose au LLM : schema JSON de la reponse (une propriete par famille de contrainte) et prompt systeme avec exemples.</summary>
public static class SchemaContraintesLlm
{
    /// <summary>A incrementer a chaque changement du prompt ou du schema : invalide le cache.</summary>
    public const string Version = "1";

    private const string SchemaJson = """
        {
          "type": "object",
          "properties": {
            "plage": {
              "type": "object",
              "properties": {
                "min": { "type": "number" },
                "max": { "type": "number" },
                "minExclusif": { "type": "boolean" },
                "maxExclusif": { "type": "boolean" }
              }
            },
            "longueurTexte": {
              "type": "object",
              "properties": { "min": { "type": "integer" }, "max": { "type": "integer" } }
            },
            "tailleCollection": {
              "type": "object",
              "properties": { "min": { "type": "integer" }, "max": { "type": "integer" } }
            },
            "valeursAutorisees": { "type": "array", "items": { "type": "string" } },
            "valeursExclues": { "type": "array", "items": { "type": "string" } },
            "formats": {
              "type": "array",
              "items": {
                "type": "string",
                "enum": ["email", "url", "guid", "telephone", "codePostal", "majuscules", "minuscules", "chiffresUniquement", "lettres", "alphanumerique", "sansEspaces", "iso8601", "utc", "dateSeule"]
              }
            },
            "formatDate": { "type": "string" },
            "motif": { "type": "string" },
            "presence": { "type": "string", "enum": ["obligatoire", "optionnel"] },
            "decimales": { "type": "integer" },
            "multiple": { "type": "number" },
            "parite": { "type": "string", "enum": ["pair", "impair"] },
            "valeurFixe": {
              "type": "object",
              "properties": { "texte": { "type": "string" }, "parDefaut": { "type": "boolean" } }
            },
            "unicite": { "type": "string", "enum": ["unique", "sequentiel"] },
            "temporalite": { "type": "string", "enum": ["passe", "futur", "aujourdhui"] },
            "plageDates": {
              "type": "object",
              "properties": { "min": { "type": "string" }, "max": { "type": "string" } }
            },
            "elementsDistincts": { "type": "boolean" },
            "unite": { "type": "string" },
            "cible": { "type": "string", "enum": ["membre", "element"] },
            "notes": { "type": "array", "items": { "type": "string" } },
            "nonCompris": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["notes", "nonCompris"]
        }
        """;

    public const string PromptSysteme = """
        Tu es un analyseur de commentaires de code C#. On te donne un membre (nom, type C#) et les commentaires qui
        le documentent. Tu extrais UNIQUEMENT les contraintes de valeur ecrites explicitement, en JSON conforme au
        schema impose. Regles :
        - Ne rien inventer : si le commentaire ne dit rien sur une famille, ne pas renseigner la propriete.
        - plage : bornes numeriques (« entre 0 et 100 », « max 50 », « > 0 » = min 0 exclusif, « positif » = min 0,
          « strictement positif » = min 0 exclusif, « negatif » = max 0 exclusif).
        - Sur un texte, « max 50 caracteres » ou « entre 3 et 10 caracteres » est une longueurTexte, pas une plage.
          Sur une collection, « entre 1 et 9 elements » ou « max 5 items » est une tailleCollection.
        - « N chiffres » sur un nombre = plage de 10^(N-1) a 10^N-1 ; sur un texte = longueurTexte min=max=N et
          format chiffresUniquement.
        - valeursAutorisees : listes explicites (« valeurs possibles : A, B ou C », « parmi ... », « A|B|C »).
          Une correspondance « 0=arret 1=marche » (au moins deux paires) donne valeursAutorisees ["0","1"] et une
          note par paire. Une seule correspondance (« 0 = illimite ») est seulement une note, jamais une restriction.
        - valeursExclues : « sauf X », « jamais X », « autre que X ».
        - formats : email, url, guid, telephone, codePostal, majuscules, minuscules, chiffresUniquement, lettres,
          alphanumerique, sansEspaces, iso8601, utc, dateSeule.
        - formatDate : un modele .NET (yyyy-MM-dd, dd/MM/yyyy HH:mm). motif : une expression reguliere.
        - presence : « obligatoire » / « optionnel » (facultatif, peut etre null, nullable, non obligatoire).
        - decimales (« 2 decimales », « arrondi a 1 decimale »), multiple (« multiple de 10 », « par pas de 0.5 »),
          parite (« pair », « impair »).
        - valeurFixe : « toujours X », « vaut X », « = X » (parDefaut=false) ; « par defaut X » (parDefaut=true).
        - unicite : « unique » ; « sequentiel » pour incremental, auto-incremente, identifiant, compteur.
        - temporalite : passe, futur, aujourdhui. plageDates : dates yyyy-MM-dd (« apres 2024-01-01 »,
          « entre 2020 et 2025 », « avant le 31/12/2026 »).
        - unite : une unite ecrite entre crochets ([%], [L/min], [mm]) ou en prose (en mm, en secondes).
          « % » ou « pourcentage » implique une plage 0..100 seulement si aucune autre plage n'est donnee.
        - cible : « element » si la contrainte porte sur chaque element d'une collection, « membre » sinon.
        - notes : informations utiles qui ne sont pas des contraintes. nonCompris : les phrases qui semblent
          exprimer une limite mais que tu ne sais pas traduire.
        - Les nombres utilisent le point decimal. Reponds uniquement avec le JSON.

        Exemples :
        Membre ReductionPct (decimal), commentaire : « [%] entre 5 et 40, 1 decimale »
        -> {"plage":{"min":5,"max":40},"decimales":1,"unite":"%","notes":[],"nonCompris":[]}
        Membre SensLaminage (int), commentaire : « 0=DER vers B2 1=B2 vers B1 »
        -> {"valeursAutorisees":["0","1"],"notes":["0 = DER vers B2","1 = B2 vers B1"],"nonCompris":[]}
        Membre Nuance (string), commentaire : « valeurs possibles : DC01, DC03 ou M400-50A »
        -> {"valeursAutorisees":["DC01","DC03","M400-50A"],"notes":[],"nonCompris":[]}
        Membre DateEntree (DateTime), commentaire : « dans le passe, UTC »
        -> {"temporalite":"passe","formats":["utc"],"notes":[],"nonCompris":[]}
        Membre NombreSpires (int), commentaire : « 0 = inconnu »
        -> {"notes":["0 = inconnu"],"nonCompris":[]}
        Membre Numero (string), commentaire : « code sur 10 caracteres alphanumeriques en majuscules »
        -> {"longueurTexte":{"min":10,"max":10},"formats":["alphanumerique","majuscules"],"notes":[],"nonCompris":[]}
        """;

    public static JsonNode Schema() => JsonNode.Parse(SchemaJson)!;
}
