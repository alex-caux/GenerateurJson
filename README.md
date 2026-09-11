# GenerateurJson

Programme console .NET 8 qui génère des **documents JSON d'exemple à partir de modèles C# fournis à
l'exécution** (fichiers `.cs`), en respectant les **limites écrites dans les commentaires du code** :
`// max 50 caracteres`, `/// <summary>Entre 0 et 100.</summary>`, `// valeurs possibles : A, B ou C`,
`// [%] entre 5 et 40, 1 decimale`, `// 0=arret 1=marche`, `[Range(0, 100)]`…

Pourquoi des sources `.cs` et pas des DLL : les commentaires disparaissent à la compilation, une DLL ne
peut donc pas porter les limites. L'analyse est purement syntaxique (Roslyn, sans compilation) : les
fichiers n'ont pas besoin de compiler ni d'avoir leurs dépendances.

Deux moteurs d'interprétation des commentaires, au choix :

| Moteur | Quand | Installation | Réseau |
|---|---|---|---|
| **Règles** (défaut) | grammaire regex FR/EN décrite plus bas, instantané, reproductible | aucune | aucun |
| **LLM local** (`--llm`) | comprend toute tournure sans règle à écrire | Ollama + un modèle (une fois) | uniquement `127.0.0.1` |

Dans les deux cas la **génération des valeurs reste déterministe** (graine `--seed`) : le LLM ne fait que
traduire les commentaires en contraintes, jamais produire le JSON.

```bash
cd GenerateurJson
dotnet run --project src/GenerateurJson -- --source exemples/ModelesExemple.cs --list
dotnet run --project src/GenerateurJson -- --source exemples/ModelesExemple.cs --type Bobine --explain --seed 42 > NUL
dotnet run --project src/GenerateurJson -- --source exemples/ModelesExemple.cs --type Bobine --count 3 --seed 42 --out exemples/bobines.json
dotnet run --project src/GenerateurJson -- --source ../ThreadingLab/src/ThreadingLab/Domain --type Produit
```

Le JSON part sur **stdout**, tout le reste (avertissements, rapport `--explain`, graine utilisée) sur
**stderr** : `> fichier.json` reste propre.

## Options

| Option | Valeur | Défaut | Rôle |
|---|---|---|---|
| `--source`, `-s` | fichier `.cs` ou dossier | — | répétable ; un dossier est parcouru récursivement (`bin/`, `obj/`, `*.g.cs` ignorés) ; un argument sans tiret est aussi une source |
| `--type`, `-t` | nom simple, `Outer.Inner` ou nom qualifié | racine unique | type racine ; sans `--type`, l'unique type (avec au moins un membre) que personne d'autre ne référence est choisi, sinon la liste des candidats est affichée |
| `--list` | | | liste les types trouvés (avec « racine possible ») et s'arrête |
| `--explain` | | | affiche sur stderr, pour chaque type atteignable et chaque membre, les commentaires bruts et ce qui en a été compris |
| `--count`, `-n` | N ≥ 1 | 1 | nombre de documents : un objet si 1, un tableau sinon |
| `--array` | | | toujours un tableau |
| `--seed` | entier | tirée | graine aléatoire ; sans `--seed`, la graine tirée est affichée sur stderr pour rejouer |
| `--date-pivot` | `yyyy-MM-dd` | aujourd'hui | date de référence pour « dans le passé » / « dans le futur » ; nécessaire pour une reproductibilité d'un jour à l'autre |
| `--naming` | `camel`, `pascal`, `none` | `camel` | nommage des propriétés JSON (`EpaisseurMm` → `epaisseurMm`) ; `[JsonPropertyName]` prime |
| `--max-depth` | N ≥ 1 | 3 | profondeur maximale d'objets imbriqués ; au-delà `null`, `[]` ou `{}` |
| `--null-rate` | 0..1 | 0 | probabilité de `null` pour les membres nullables (`T?`) ou optionnels ; 0 = documents maximaux |
| `--enum-as-int` | | | enums en entier plutôt qu'en nom |
| `--compact` | | | JSON sur une ligne |
| `--out`, `-o` | fichier | stdout | fichier de sortie (UTF-8 sans BOM) |
| `--llm` | | | interprète les commentaires avec le LLM local |
| `--llm-model` | nom Ollama | `qwen2.5:7b` | modèle utilisé (implique `--llm`) |
| `--llm-url` | URL | `http://localhost:11434` | adresse d'Ollama ; **hôte local obligatoire** |
| `--llm-no-cache` | | | ignore le cache des réponses |
| `--llm-allow-remote` | | | seule façon d'autoriser une adresse non locale |
| `--help`, `-h` | | | aide |

Codes de sortie : 0 ok, 1 usage, 2 analyse des sources (aucun type, racine ambiguë, `--type` inconnu,
source absente), 3 erreur d'entrée/sortie, 4 LLM indisponible (Ollama injoignable ou modèle absent).

## Ce qui est lu dans le code

**Types** : classes, records (paramètres positionnels et propriétés du corps), structs, record structs,
enums (valeurs explicites, `1 << n`, `[Flags]`), types imbriqués (`Outer.Inner`), déclarations `partial`
fusionnées, héritage (membres de la base d'abord, un nom redéclaré masque celui de la base). Un membre
typé par une classe abstraite ou une interface est généré avec son implémentation concrète si elle est
unique dans les sources. Les classes génériques ne sont jamais générées.

**Membres** : propriétés publiques automatiques (`get; set;`, `get; init;`, `get;`), champs publics,
paramètres positionnels de record. Ignorés : `static`, `const`, membres non publics, propriétés
calculées (`=> …` ou accesseur avec corps), méthodes, `[JsonIgnore]`. Les initialiseurs (`= "x"`) sont
ignorés : une valeur est toujours générée.

| Type C# | JSON généré |
|---|---|
| `int`, `long`, `short`, `byte`, `sbyte`, `uint`, `ulong`, `ushort` | entier dans `[0 ; 1000]` par défaut, borné au type |
| `decimal`, `double`, `float` | nombre à 2 décimales dans `[0 ; 1000]` par défaut |
| `bool` | `true` / `false` |
| `string`, `char` | texte prononçable (syllabes), ou format structuré |
| `Guid` | GUID déterministe |
| `DateTime`, `DateTimeOffset` | ISO 8601, ±1 an autour de la date pivot, minutes entières ; `Z` si UTC |
| `DateOnly`, `TimeOnly`, `TimeSpan` | `yyyy-MM-dd`, `HH:mm:ss`, format `c` (`00:05:30`) |
| `Uri` | `https://exemple.fr/...` |
| `byte[]` | base64 (comme System.Text.Json) |
| enum | nom du membre (ou entier avec `--enum-as-int`) ; `[Flags]` : un membre non nul |
| `T?`, `Nullable<T>` | comme `T`, ou `null` selon `--null-rate` |
| `List<T>`, `IList`, `IReadOnlyList`, `IEnumerable`, `ICollection`, `HashSet`, `ISet`, `T[]`… | tableau de 1 à 3 éléments par défaut (éléments distincts pour un ensemble) |
| `Dictionary<K,V>`, `IDictionary`, `IReadOnlyDictionary`… | objet à 2 entrées par défaut ; clés `string`, entières, enum ou `Guid` |
| classe / record / struct des sources | objet imbriqué |
| `object`, `dynamic`, tuples, `JsonElement`, tableaux multidimensionnels, types hors sources | `null` + avertissement |

**Sources de commentaires** lues pour chaque membre, dans cet ordre : `<summary>`, `<remarks>`,
`<param name="X">` du record pour le paramètre positionnel `X`, lignes `//` juste au-dessus (jusqu'à la
première ligne vide, attributs traversés), commentaire de fin de ligne (`// …` ou `/* … */` après le
membre, la virgule ou la parenthèse). Les accents sont acceptés.

**Attributs reconnus** (prioritaires sur les commentaires) : `Range`, `MinLength`, `MaxLength`, `Length`,
`StringLength(…, MinimumLength = …)`, `Required`, `RegularExpression`, `EmailAddress`, `Url`, `Phone`,
`AllowedValues`, `DeniedValues`, `DefaultValue`, `Key`, `DataType`, `JsonRequired`, `JsonPropertyName`,
`JsonIgnore`. Le modificateur `required` vaut « obligatoire ».

## Grammaire des commentaires reconnus (moteur règles)

Chaque commentaire est normalisé (accents retirés, casse ignorée), ses littéraux sont protégés
(guillemets, `^regex$`, dates, formats de date, emails, URL), puis il est découpé en phrases (`.`, `;`,
`!`, `?`, retour à la ligne). Chaque phrase passe dans la table de règles ; chaque règle reconnue
consomme son texte. Une phrase qui contient encore un nombre ou un mot déclencheur (`max`, `entre`,
`obligatoire`, `unique`…) sans qu'aucune règle ne s'applique produit un avertissement
`phrase non reconnue`, visible dans `--explain` et sur stderr.

Le **type du membre décide** : « entre 3 et 10 » est une plage sur un nombre, une longueur sur un
texte, une taille sur une collection d'objets, une plage sur les éléments d'une `List<int>`. Un mot
d'unité après les nombres tranche : `caractères` → longueur, `éléments` / `entrées` / `items` → taille,
`chiffres` → nombre de chiffres. Un préfixe « chaque élément : … » vise les éléments d'une collection.

| Famille | Formulations reconnues (FR/EN, exemples) | Effet |
|---|---|---|
| Plage numérique | `entre 0 et 100`, `de 600 à 2100`, `between -10 and 10.5`, `[0;100]`, `]0;1]`, `1..12`, `10-20`, `>= 0`, `> 0`, `<= 5`, `≥ 3`, `supérieur ou égal à 5`, `strictement inférieur à 10`, `min 3`, `max 50`, `au moins 1`, `au plus 10`, `jusqu'à 35000`, `plafond 1200`, `ne dépasse pas 100`, `50 max`, `3 minimum`, `positif`, `strictement positif`, `non négatif`, `négatif`, `non nul` (sur un nombre : 0 exclu), `code sur 5 chiffres` | bornes min/max, inclusives ou exclusives |
| Longueur de texte | `max 50 caractères`, `50 caracteres max`, `entre 3 et 10 caractères`, `au moins 3 lettres`, `longueur max 50`, `longueur 10`, `maxLength: 30`, `exactement 8 caractères`, `sur 10 caractères`, `5 chiffres` (longueur exacte + chiffres uniquement), `tronqué à 100 caractères`, `non vide` | longueur min/max/exacte |
| Valeurs autorisées | `valeurs possibles : A, B ou C`, `parmi DC01, DC03 et DC04`, `one of RED, GREEN`, `A\|B\|C`, `Statut : ACTIF, INACTIF ou SUSPENDU`, phrase entière `DC01, DC03 ou DC04` ; exclusions `sauf X`, `jamais Rebut`, `except NONE` | liste fermée, filtrée selon le type (nombres, membres d'enum avec avertissement si inconnu) |
| Correspondances | `0=arret 1=marche`, `0 = arret, 1 = marche, 2 = defaut`, `1: marche; 2: arret`, `0 -> off / 1 -> on` ; sur un texte ou un enum, clés courtes `R=rouge V=vert` | **deux paires ou plus** : valeurs autorisées = les clés, plus une note par paire ; **une seule** (`0 = illimite`) : note seulement, aucune restriction |
| Formats | `format yyyy-MM-dd`, `dd/MM/yyyy HH:mm`, `ISO 8601`, `UTC`, `date seule`, `e-mail` / `courriel`, `url` / `lien`, `guid` / `uuid`, `téléphone`, `code postal`, `majuscules` / `upper case`, `minuscules`, `chiffres uniquement` / `digits only`, `alphanumérique`, `lettres uniquement`, `sans espace` ; `regex ^…$`, `motif "…"`, `pattern …`, `^…$` seul | format de date, jeu de caractères, casse, expression régulière |
| Présence | `obligatoire`, `requis`, `required`, `non null`, `jamais null`, `non vide` ; `optionnel`, `facultatif`, `optional`, `peut être null`, `nullable`, `non obligatoire` | obligatoire / optionnel (jamais `null` pour un obligatoire) |
| Taille de collection | `entre 1 et 9 éléments`, `max 5 items`, `2 à 4 entrées`, `taille max 5`, `au moins un élément`, `liste vide autorisée`, `non vide`, `sans doublon` / `distinct` | taille min/max, éléments distincts |
| Décimales | `2 décimales`, `arrondi à 1 décimale`, `3 decimal places`, `précision 0,01` (→ pas 0,01 et 2 décimales), `sans décimale`, `nombre entier`, `au dixième` | nombre de décimales |
| Valeur fixe / défaut | `toujours "REV"`, `vaut 2`, `= true`, `constante 0` ; `par défaut 0`, `default "N/A"`, `defaults to false` | valeur fixe (prime sur tout) ; valeur par défaut (utilisée seulement si rien d'autre ne contraint la valeur) |
| Unicité | `unique`, `identifiant unique`, `clé primaire` ; `incrémental`, `auto-incrémenté`, `séquentiel`, `identifiant`, `numéro d'ordre`, `compteur` | entier : séquence 1, 2, 3… partagée par tous les documents ; texte : préfixe + numéro |
| Dates | `dans le passé`, `à venir`, `pas dans le futur`, `date du jour` / `today` ; `après 2024-01-01`, `avant le 31/12/2026`, `entre 2020 et 2025`, `depuis 2023` | fenêtre de dates autour de `--date-pivot` |
| Multiples / parité | `multiple de 10`, `par pas de 0,5`, `divisible par 5`, `pair`, `impair`, `even`, `odd` | tirage sur la grille |
| Unités | entre crochets `[%]`, `[L/min]`, `[mm]`, `[kg]`, `[°C]`, `[s]` ; en prose `en mm`, `exprimé en secondes`, `35000 kg`, `en %` | note affichée dans `--explain` ; `%` / `pourcentage` ajoute une plage 0..100 à faible priorité ; sur un `TimeSpan` l'unité (ms, s, min, h, jours) fixe la lecture de la plage (défaut : secondes) |

Les nombres acceptent la virgule ou le point décimal (`0,3` = `0.3`). Les listes se séparent par `, `,
`;`, `/`, `|`, `ou`, `et`. Pas de séparateur de milliers (`1 000` est lu comme deux nombres : la phrase
est signalée comme non reconnue, c'est le cas de `TonnageCommande` dans l'exemple).

### Priorités et fusion

Priorité par origine : **attribut > `required` > commentaire > type nullable > indice tiré du nom**.
Une famille déjà tenue par une origine plus forte ignore les suivantes (`[Range(0,100)]` +
`// entre 0 et 50` → `[0 ; 100]`, avec une note dans `--explain`). À origine égale : les plages,
longueurs et tailles s'intersectent (intersection vide → avertissement, première conservée), les
valeurs autorisées s'intersectent, les formats s'ajoutent, `obligatoire` l'emporte sur `optionnel`,
et pour le reste la dernière formulation gagne avec un avertissement si elle diffère. Sur une
collection, la présence, la taille et « sans doublon » portent sur la collection ; tout le reste porte
sur ses éléments.

### Indices tirés du nom du membre

Ajoutés avec la priorité la plus basse, donc seulement si rien d'autre ne contraint la famille : `Id`
(entier : séquence ; texte : `PRE000001`), `Email`, `Url`, `Telephone`, `CodePostal`, `Code` / `Ref` /
`Numero` (majuscules alphanumériques de 6 à 10), `Nom` / `Libelle` / `Titre` (deux mots capitalisés),
`Description` / `Commentaire` (une phrase), `Pourcentage` / `Pct` / `Taux` (0..100), `Prix` / `Montant`
(1..10000, 2 décimales), `Quantite` / `Nombre` (1..100), `Age`, `Annee`, `Mois`, `Jour`, `Heure`,
`Epaisseur` / `Largeur` / `Diametre` (0,1..3000), `Temperature`, `Poids`, `Latitude`, `Longitude`, `Pays`
(`France`), `Devise` (`EUR`), `Langue` (`fr-FR`), `Version` (`1.0.0`). Étiquetés `[nom]` dans `--explain`.

## Mode `--explain`

```
== ModelesExemple.Passe (classe, exemples/ModelesExemple.cs:52) ==
   Une passe de laminage sur le reversible.
  Id : int (herite de ModelesExemple.EntiteBase)
    commentaire (fin de ligne) : « identifiant unique, incremental »
    -> unique, sequentiel  [fin de ligne]
  ReductionPct : decimal
    commentaire (fin de ligne) : « [%] entre 5 et 40, 1 decimale »
    -> valeur >= 5 et <= 40  [fin de ligne]
    -> 1 decimale(s)  [fin de ligne]
    -> unite : %  [fin de ligne]
  SensLaminage : int
    commentaire (fin de ligne) : « 0=DER vers B2 1=B2 vers B1 »
    -> valeurs autorisees : 0, 1  [fin de ligne]
    ~ 0 = DER vers B2
    ~ 1 = B2 vers B1
  NombreSpires : int
    commentaire (fin de ligne) : « 0 = inconnu »
    -> valeur >= 1 et <= 100  [nom]
    ~ 0 = inconnu
```

`->` contrainte retenue et son origine, `~` note, `!` avertissement (phrase non reconnue, contraintes
incompatibles, valeur d'enum inconnue…).

## Mode LLM local (`--llm`, optionnel)

Le LLM remplace uniquement la grammaire ci-dessus : il lit chaque commentaire et renvoie les
contraintes dans un JSON au schéma imposé (sorties structurées d'Ollama), que le moteur déterministe
valide (nombres, regex, formats, membres d'enum) avant de les fusionner comme les autres. L'analyse des
sources, les attributs, les indices de nom et la génération ne changent pas ; `--explain` fonctionne à
l'identique (moteur `llm qwen2.5:7b`).

**Confidentialité** : Ollama est libre (MIT) et gratuit ; il exécute le modèle sur le poste et n'écoute
que sur `127.0.0.1`. L'outil refuse toute `--llm-url` dont l'hôte n'est pas local (sauf
`--llm-allow-remote`) et n'utilise aucun proxy : ni le code, ni les commentaires, ni le JSON ne quittent
la machine. Internet ne sert qu'une fois, pour télécharger Ollama et le modèle ; ensuite tout fonctionne
hors ligne. Le cache des réponses (`%LOCALAPPDATA%\GenerateurJson\cache-llm.json`) contient les
commentaires des modèles et reste sur le poste.

Installation, une seule fois :

```powershell
winget install Ollama.Ollama        # puis lancer Ollama (icone dans la barre des taches, ou « ollama serve »)
ollama pull qwen2.5:7b              # environ 4,7 Go ; ou qwen2.5:3b (2 Go, plus rapide, un peu moins precis)
```

Puis `--llm` (et `--llm-model qwen2.5:3b` pour le petit modèle). Sans carte graphique exploitée par
Ollama, compter 3 à 8 s par membre commenté avec le 7B, 1 à 3 s avec le 3B ; un modèle de 30 membres
prend 1 à 4 minutes la première fois, puis l'exécution est instantanée grâce au cache (clé : modèle,
version du prompt, type C#, texte du commentaire). La progression `llm 3/12 Passe.ReductionPct` s'affiche
sur stderr. Une réponse invalide après deux tentatives laisse le membre sans contrainte de commentaire,
avec un avertissement. Si Ollama est absent ou le modèle non téléchargé, le programme s'arrête avec le
code 4 et la commande d'installation.

## Reproductibilité

Même sources, même `--seed`, même `--date-pivot` → même JSON, octet pour octet, y compris entre
machines. Les identifiants séquentiels (`Id`) sont numérotés 1, 2, 3… sur l'ensemble des documents
d'une exécution. Sans `--seed`, la graine tirée est affichée sur stderr.

## Limites

- Regex : sous-ensemble (littéraux, classes, `\d \w \s`, quantificateurs, groupes, alternatives, ancres) ;
  un motif hors sous-ensemble produit un texte libre et un avertissement.
- Pas de séparateur de milliers ; listes séparées par `, `, `;`, `/`, `|`, `ou`, `et` ; dates `dd/MM/yyyy`
  lues à la française ; heure locale non générée (dates sans décalage, ou `Z` si UTC).
- Génériques, tuples, `object`, `dynamic`, types absents des sources : `null` avec avertissement.
- Un seul type racine par exécution ; les initialiseurs de propriétés sont ignorés.
- Pas de sémantique Roslyn : les alias `using`, les types d'autres assemblies et les constantes
  référencées dans un commentaire (« supérieur ou égal à TailleLot ») ne sont pas résolus (avertissement).
- Le moteur LLM dépend du modèle : vérifier le rapport `--explain` sur un modèle avant de s'y fier.

## Fichiers

| Fichier | Rôle |
|---|---|
| `Program.cs` | Point d'entrée : options, analyse, choix de la racine, moteur (règles ou LLM), rapport, génération, écriture |
| `OptionsLigneCommande.cs` | Analyse des arguments, texte d'aide |
| `Modele/Descripteurs.cs` | Types, membres, références de type, commentaires, attributs (données pures) |
| `Modele/CatalogueTypes.cs` | Résolution des noms, membres effectifs (héritage, partials), racines, implémentations concrètes |
| `Analyse/AnalyseurSources.cs` | Parcours Roslyn des fichiers, résolution des types C# vers un genre JSON, seconde passe de liaison |
| `Analyse/ExtracteurCommentaires.cs` | Documentation XML, commentaires de fin de ligne et au-dessus (pièges des trivia) |
| `Analyse/LecteurAttributs.cs` | Lecture syntaxique des attributs et de leurs arguments |
| `Contraintes/Contrainte.cs` | Les familles de contraintes et `JeuContraintes` (priorités, fusion, vue effective) |
| `Contraintes/NormaliseurTexte.cs` | Normalisation isométrique, masquage des littéraux, découpage en phrases, nombres, listes |
| `Contraintes/ReglesContraintes.cs` | La grammaire : table des règles regex et leurs constructeurs (dispatch par type) |
| `Contraintes/InterpreteurContraintes.cs` | Moteur règles : application des phases avec consommation, phrases non reconnues |
| `Contraintes/ContraintesCommunes.cs` | Attributs, `required`, nullable, routage membre/élément, filtrage des valeurs (partagé avec le LLM) |
| `Contraintes/CalculateurContraintes.cs` | Mémorisation par membre + indices de nom (partagé entre rapport et générateur) |
| `Llm/ClientOllama.cs`, `CacheLlm.cs`, `SchemaContraintesLlm.cs`, `InterpreteurLlm.cs` | Client local (hôte local imposé), cache, schéma + prompt, validation des réponses |
| `Generation/ContexteGeneration.cs` | Graine, séquences, profondeur, avertissements |
| `Generation/GenerateurValeurs.cs` | Scalaires et enums sous contraintes |
| `Generation/GenerateurTexte.cs`, `GenerateurMotif.cs`, `IndicesNom.cs` | Textes prononçables et formats, regex inverse, indices tirés du nom |
| `Generation/GenerateurDocumentJson.cs` | Objets, collections, dictionnaires, nommage, profondeur |
| `Rapport/RapportExplication.cs` | Le rapport `--explain` |
| `exemples/ModelesExemple.cs` | Jeu d'essai (bobine, passes, défauts, opérateur) couvrant la grammaire |

Le dépôt contient un `nuget.config` local (`nuget.org` seul) parce que le flux privé configuré sur le
poste demande une authentification et ferait échouer la restauration du package Roslyn.
