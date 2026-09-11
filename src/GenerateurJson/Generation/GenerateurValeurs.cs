using System.Globalization;
using System.Text.Json.Nodes;

namespace GenerateurJson;

/// <summary>Valeurs scalaires et enums, tirees au sort dans le respect des contraintes du membre.</summary>
public sealed class GenerateurValeurs(ContexteGeneration contexte)
{
    private readonly GenerateurTexte _texte = new(contexte.Aleatoire);

    public JsonNode? Generer(ReferenceType type, JeuContraintes c, string nomMembre, string cle)
    {
        switch (type.Genre)
        {
            case GenreValeur.Entier:
                return GenererEntier(type, c, cle);
            case GenreValeur.Reel:
                return GenererReel(type, c, cle);
            case GenreValeur.Booleen:
                return GenererBooleen(c);
            case GenreValeur.Texte:
                return GenererTexte(c, nomMembre, cle);
            case GenreValeur.Caractere:
                return GenererCaractere(c);
            case GenreValeur.Guid:
                return GenererGuid(c);
            case GenreValeur.DateHeure:
                return GenererDateHeure(type, c, cle);
            case GenreValeur.DateSeule:
                return GenererDateSeule(c, cle);
            case GenreValeur.HeureSeule:
                return GenererHeureSeule(c);
            case GenreValeur.Duree:
                return GenererDuree(c, cle);
            case GenreValeur.Uri:
                return JsonValue.Create(_texte.Url());
            case GenreValeur.Octets:
                return GenererOctets();
            case GenreValeur.Enum:
                return GenererEnum(type, c, cle);
            default:
                contexte.Avertir($"{cle} : type {type.TexteOriginal} non supporte, genere a null");
                return null;
        }
    }

    // ----- Nombres -----

    private JsonNode? GenererEntier(ReferenceType type, JeuContraintes c, string cle)
    {
        var (clrMin, clrMax) = BornesClr(type.Nom);
        if (c.ValeurFixe is { ParDefaut: false } fixe)
        {
            if (NormaliseurTexte.Nombre(fixe.Texte) is { } valeurFixe)
            {
                return JsonValue.Create((long)Math.Clamp(decimal.Truncate(valeurFixe), clrMin, clrMax));
            }

            contexte.Avertir($"{cle} : valeur fixe « {fixe.Texte} » non numerique, ignoree");
        }

        var min = c.Plage?.Min ?? Math.Max(0, clrMin);
        var max = c.Plage?.Max ?? Math.Min(1000, clrMax);
        if (c.Plage is { MinExclusif: true })
        {
            min += 1;
        }

        if (c.Plage is { MaxExclusif: true })
        {
            max -= 1;
        }

        min = Math.Clamp(decimal.Ceiling(min), clrMin, clrMax);
        max = Math.Clamp(decimal.Floor(max), clrMin, clrMax);

        if (c.Valeurs is not null)
        {
            var candidats = c.Valeurs.Valeurs
                .Select(NormaliseurTexte.Nombre)
                .Where(v => v is not null && v >= min && v <= max && !EstExclu(c, v.Value))
                .Select(v => v!.Value)
                .ToList();
            if (candidats.Count > 0)
            {
                return JsonValue.Create((long)candidats[contexte.Aleatoire.Next(candidats.Count)]);
            }

            contexte.Avertir($"{cle} : aucune valeur autorisee compatible avec la plage, tirage libre");
        }

        if (c.Unicite is not null)
        {
            var sequence = contexte.ProchaineSequence(cle);
            if (c.Plage?.Max is { } plafond && sequence > plafond)
            {
                contexte.Avertir($"{cle} : la sequence depasse le maximum {NormaliseurTexte.FormaterNombre(plafond)}");
            }

            return JsonValue.Create(sequence);
        }

        if (c.ValeurFixe is { ParDefaut: true } defaut && !c.AContrainteDeValeur && NormaliseurTexte.Nombre(defaut.Texte) is { } valeurDefaut)
        {
            return JsonValue.Create((long)decimal.Truncate(valeurDefaut));
        }

        if (min > max)
        {
            contexte.Avertir($"{cle} : plage vide ({NormaliseurTexte.FormaterNombre(min)} > {NormaliseurTexte.FormaterNombre(max)}), minimum utilise");
            return JsonValue.Create((long)min);
        }

        var borneMin = (long)min;
        var borneMax = (long)max;
        for (var essai = 0; essai < 20; essai++)
        {
            long valeur;
            if (c.Multiple is { } multiple && multiple.Pas >= 1 && multiple.Pas == decimal.Truncate(multiple.Pas))
            {
                var pas = (long)multiple.Pas;
                var kMin = DivisionPlafond(borneMin, pas);
                var kMax = DivisionPlancher(borneMax, pas);
                if (kMin > kMax)
                {
                    contexte.Avertir($"{cle} : aucun multiple de {pas} dans la plage, minimum utilise");
                    return JsonValue.Create(borneMin);
                }

                valeur = contexte.EntierEntre(kMin, kMax) * pas;
            }
            else if (c.Parite is { } parite)
            {
                var decalage = parite.Pair ? 0 : 1;
                var kMin = DivisionPlafond(borneMin - decalage, 2);
                var kMax = DivisionPlancher(borneMax - decalage, 2);
                if (kMin > kMax)
                {
                    contexte.Avertir($"{cle} : aucune valeur {parite.Decrire()} dans la plage, minimum utilise");
                    return JsonValue.Create(borneMin);
                }

                valeur = contexte.EntierEntre(kMin, kMax) * 2 + decalage;
            }
            else
            {
                valeur = contexte.EntierEntre(borneMin, borneMax);
            }

            if (!EstExclu(c, valeur))
            {
                return JsonValue.Create(valeur);
            }
        }

        contexte.Avertir($"{cle} : impossible d'eviter les valeurs exclues");
        return JsonValue.Create(borneMin);
    }

    private JsonNode? GenererReel(ReferenceType type, JeuContraintes c, string cle)
    {
        if (c.ValeurFixe is { ParDefaut: false } fixe)
        {
            if (NormaliseurTexte.Nombre(fixe.Texte) is { } valeurFixe)
            {
                return Reel(type, valeurFixe);
            }

            contexte.Avertir($"{cle} : valeur fixe « {fixe.Texte} » non numerique, ignoree");
        }

        var decimales = c.Decimales?.Nombre ?? (c.Multiple is { } m ? NombreDecimales(m.Pas) : 2);
        decimales = Math.Clamp(decimales, 0, 10);
        var echelle = Puissance10(decimales);
        var min = c.Plage?.Min ?? 0;
        var max = c.Plage?.Max ?? 1000;

        if (c.Valeurs is not null)
        {
            var candidats = c.Valeurs.Valeurs
                .Select(NormaliseurTexte.Nombre)
                .Where(v => v is not null && v >= min && v <= max && !EstExclu(c, v.Value))
                .Select(v => v!.Value)
                .ToList();
            if (candidats.Count > 0)
            {
                return Reel(type, candidats[contexte.Aleatoire.Next(candidats.Count)]);
            }

            contexte.Avertir($"{cle} : aucune valeur autorisee compatible avec la plage, tirage libre");
        }

        if (c.ValeurFixe is { ParDefaut: true } defaut && !c.AContrainteDeValeur && NormaliseurTexte.Nombre(defaut.Texte) is { } valeurDefaut)
        {
            return Reel(type, valeurDefaut);
        }

        var kMin = decimal.Ceiling(min * echelle) + (c.Plage is { MinExclusif: true } ? 1 : 0);
        var kMax = decimal.Floor(max * echelle) - (c.Plage is { MaxExclusif: true } ? 1 : 0);
        if (kMin > kMax)
        {
            contexte.Avertir($"{cle} : plage vide avec {decimales} decimale(s), minimum utilise");
            return Reel(type, decimal.Round(min, decimales));
        }

        var borneMin = (long)Math.Clamp(kMin, long.MinValue / 2, long.MaxValue / 2);
        var borneMax = (long)Math.Clamp(kMax, long.MinValue / 2, long.MaxValue / 2);
        long pas = 1;
        if (c.Multiple is { } multiple)
        {
            var pasEchelle = multiple.Pas * echelle;
            if (pasEchelle >= 1 && pasEchelle == decimal.Truncate(pasEchelle))
            {
                pas = (long)pasEchelle;
            }
            else
            {
                contexte.Avertir($"{cle} : pas {NormaliseurTexte.FormaterNombre(multiple.Pas)} incompatible avec {decimales} decimale(s), ignore");
            }
        }

        var indiceMin = DivisionPlafond(borneMin, pas);
        var indiceMax = DivisionPlancher(borneMax, pas);
        if (indiceMin > indiceMax)
        {
            contexte.Avertir($"{cle} : aucun multiple dans la plage, minimum utilise");
            return Reel(type, decimal.Round(min, decimales));
        }

        for (var essai = 0; essai < 20; essai++)
        {
            var valeur = AvecEchelle(contexte.EntierEntre(indiceMin, indiceMax) * pas / echelle, decimales);
            if (!EstExclu(c, valeur))
            {
                return Reel(type, valeur);
            }
        }

        contexte.Avertir($"{cle} : impossible d'eviter les valeurs exclues");
        return Reel(type, decimal.Round(min, decimales));
    }

    private static JsonNode Reel(ReferenceType type, decimal valeur) => type.Nom switch
    {
        "double" => JsonValue.Create((double)valeur),
        "float" => JsonValue.Create((float)valeur),
        _ => JsonValue.Create(valeur),
    };

    /// <summary>Fixe l'echelle du decimal pour que « 2 decimales » s'ecrive toujours avec deux chiffres (12.30 et non 12.3).</summary>
    private static decimal AvecEchelle(decimal valeur, int decimales) =>
        decimal.Parse(valeur.ToString("F" + decimales, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static (decimal Min, decimal Max) BornesClr(string nom) => nom switch
    {
        "byte" => (byte.MinValue, byte.MaxValue),
        "sbyte" => (sbyte.MinValue, sbyte.MaxValue),
        "short" => (short.MinValue, short.MaxValue),
        "ushort" => (ushort.MinValue, ushort.MaxValue),
        "int" => (int.MinValue, int.MaxValue),
        "uint" => (uint.MinValue, uint.MaxValue),
        "ulong" => (0, long.MaxValue),
        _ => (long.MinValue, long.MaxValue),
    };

    private static bool EstExclu(JeuContraintes c, decimal valeur) =>
        c.Exclusions is not null && c.Exclusions.Valeurs.Any(v => NormaliseurTexte.Nombre(v) == valeur);

    private static long DivisionPlafond(long valeur, long diviseur)
    {
        var quotient = Math.DivRem(valeur, diviseur, out var reste);
        return reste > 0 ? quotient + 1 : quotient;
    }

    private static long DivisionPlancher(long valeur, long diviseur)
    {
        var quotient = Math.DivRem(valeur, diviseur, out var reste);
        return reste < 0 ? quotient - 1 : quotient;
    }

    private static decimal Puissance10(int exposant)
    {
        decimal resultat = 1;
        for (var i = 0; i < exposant; i++)
        {
            resultat *= 10;
        }

        return resultat;
    }

    private static int NombreDecimales(decimal valeur)
    {
        var texte = valeur.ToString(CultureInfo.InvariantCulture).TrimEnd('0');
        var point = texte.IndexOf('.');
        return point < 0 ? 0 : texte.Length - point - 1;
    }

    // ----- Booleens, caracteres, Guid -----

    private JsonNode GenererBooleen(JeuContraintes c)
    {
        var fixe = c.ValeurFixe;
        if (fixe is not null && (!fixe.ParDefaut || !c.AContrainteDeValeur))
        {
            return JsonValue.Create(fixe.Texte.ToLowerInvariant() is "true" or "vrai" or "oui" or "yes" or "1");
        }

        return JsonValue.Create(contexte.Aleatoire.Next(2) == 1);
    }

    private JsonNode GenererCaractere(JeuContraintes c)
    {
        if (c.ValeurFixe is { } fixe && fixe.Texte.Length > 0)
        {
            return JsonValue.Create(fixe.Texte[..1]);
        }

        if (c.Valeurs is { Valeurs.Count: > 0 })
        {
            var choix = c.Valeurs.Valeurs[contexte.Aleatoire.Next(c.Valeurs.Valeurs.Count)];
            return JsonValue.Create(choix.Length > 0 ? choix[..1] : "A");
        }

        return JsonValue.Create(_texte.SuiteDeLettres(1, majuscules: true));
    }

    private JsonNode GenererGuid(JeuContraintes c)
    {
        if (c.ValeurFixe is { } fixe && Guid.TryParse(fixe.Texte, out var guidFixe))
        {
            return JsonValue.Create(guidFixe);
        }

        return JsonValue.Create(GuidAleatoire());
    }

    private Guid GuidAleatoire()
    {
        var octets = new byte[16];
        contexte.Aleatoire.NextBytes(octets);
        octets[7] = (byte)((octets[7] & 0x0F) | 0x40);
        octets[8] = (byte)((octets[8] & 0x3F) | 0x80);
        return new Guid(octets);
    }

    // ----- Textes -----

    private JsonNode? GenererTexte(JeuContraintes c, string nomMembre, string cle)
    {
        var formats = c.Formats;
        if (c.ValeurFixe is { ParDefaut: false } fixe)
        {
            return JsonValue.Create(fixe.Texte);
        }

        if (c.Valeurs is not null)
        {
            var candidats = c.Valeurs.Valeurs
                .Where(v => c.Exclusions is null || !c.Exclusions.Valeurs.Contains(v, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (candidats.Count > 0)
            {
                return JsonValue.Create(candidats[contexte.Aleatoire.Next(candidats.Count)]);
            }

            contexte.Avertir($"{cle} : toutes les valeurs autorisees sont exclues, texte libre");
        }

        if (c.ValeurFixe is { ParDefaut: true } defaut && !c.AContrainteDeValeur)
        {
            return JsonValue.Create(defaut.Texte);
        }

        string texte;
        if (c.Unicite is not null)
        {
            texte = TexteUnique(c, cle);
        }
        else if (c.Motif is not null)
        {
            var produit = GenerateurMotif.Generer(c.Motif.Regex, contexte.Aleatoire);
            if (produit is not null)
            {
                return JsonValue.Create(produit);
            }

            contexte.Avertir($"{cle} : motif regex « {c.Motif.Regex} » hors du sous-ensemble supporte, texte libre");
            texte = _texte.Mot();
        }
        else if (c.FormatDate is not null)
        {
            return JsonValue.Create(DateBrute(c, cle).ToString(c.FormatDate.Modele, CultureInfo.InvariantCulture));
        }
        else if (formats.Contains(GenreFormat.Email))
        {
            texte = _texte.Email();
        }
        else if (formats.Contains(GenreFormat.Url))
        {
            texte = _texte.Url();
        }
        else if (formats.Contains(GenreFormat.Guid))
        {
            texte = GuidAleatoire().ToString("D");
        }
        else if (formats.Contains(GenreFormat.Telephone))
        {
            texte = _texte.Telephone();
        }
        else if (formats.Contains(GenreFormat.CodePostal))
        {
            texte = _texte.SuiteDeChiffres(5);
        }
        else if (c.Temporalite is not null || c.PlageDates is not null || formats.Contains(GenreFormat.Iso8601))
        {
            return JsonValue.Create(DateBrute(c, cle).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        }
        else
        {
            texte = TexteParNom(nomMembre, c.Longueur);
        }

        var structure = formats.Contains(GenreFormat.Email) || formats.Contains(GenreFormat.Url) || formats.Contains(GenreFormat.Guid);
        texte = _texte.AppliquerFormats(texte, formats);
        var ajuste = _texte.AjusterLongueur(texte, c.Longueur, formats);
        if (structure && ajuste.Length != texte.Length)
        {
            contexte.Avertir($"{cle} : la longueur demandee ne permet pas de respecter le format structure");
        }

        return JsonValue.Create(ajuste);
    }

    private string TexteUnique(JeuContraintes c, string cle)
    {
        var sequence = contexte.ProchaineSequence(cle);
        var longueur = c.Longueur?.Max ?? c.Longueur?.Min ?? 9;
        if (c.Formats.Contains(GenreFormat.ChiffresUniquement))
        {
            return sequence.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(1, longueur), '0');
        }

        var prefixe = new string(cle.Split('.').Last().Where(char.IsLetter).Take(3).ToArray()).ToUpperInvariant();
        if (prefixe.Length == 0)
        {
            prefixe = "ID";
        }

        var chiffres = Math.Max(1, longueur - prefixe.Length);
        return prefixe + sequence.ToString(CultureInfo.InvariantCulture).PadLeft(chiffres, '0');
    }

    private string TexteParNom(string nomMembre, LongueurTexte? longueur)
    {
        var mots = IndicesNom.Mots(nomMembre);
        bool Contient(params string[] cles) => mots.Any(m => cles.Contains(m, StringComparer.Ordinal));

        if (Contient("description", "commentaire", "comment", "remarque", "message", "texte", "text", "note", "observation"))
        {
            return _texte.Phrase();
        }

        if (Contient("nom", "name", "libelle", "label", "titre", "title", "designation", "prenom", "firstname", "lastname", "operateur", "auteur", "client"))
        {
            return _texte.NomPropre();
        }

        if (longueur?.Max is { } max && max <= 4)
        {
            return _texte.Code(max);
        }

        return GenerateurTexte.Capitaliser(_texte.Mot());
    }

    // ----- Dates et durees -----

    private DateTime DateBrute(JeuContraintes c, string cle)
    {
        // Fenetre : les bornes explicites (« apres 2024-01-01 », « avant le 30/06/2025 ») priment ; la temporalite
        // (passe / futur / aujourd'hui) les resserre ; a defaut, un an de part et d'autre de la date pivot.
        var pivot = contexte.Options.DatePivot.Date;
        DateTime? borneMin = c.PlageDates?.Min;
        DateTime? borneMax = c.PlageDates?.Max is { } dateMax
            ? dateMax.TimeOfDay == TimeSpan.Zero ? dateMax.AddDays(1).AddMinutes(-1) : dateMax
            : null;
        switch (c.Temporalite?.Genre)
        {
            case GenreTemporalite.Passe:
                borneMax = borneMax is { } bm && bm < pivot ? bm : pivot.AddMinutes(-1);
                break;
            case GenreTemporalite.Futur:
                borneMin = borneMin is { } bn && bn > pivot ? bn : pivot.AddDays(1);
                break;
            case GenreTemporalite.Aujourdhui:
                borneMin = pivot;
                borneMax = pivot.AddDays(1).AddMinutes(-1);
                break;
        }

        var min = borneMin ?? (borneMax is { } fin && fin < pivot.AddDays(-365) ? fin.AddDays(-365) : pivot.AddDays(-365));
        var max = borneMax ?? (min > pivot.AddDays(365) ? min.AddDays(365) : pivot.AddDays(365));
        if (min > max)
        {
            contexte.Avertir($"{cle} : fenetre de dates vide, borne basse utilisee");
            max = min;
        }

        var minutes = (long)(max - min).TotalMinutes;
        var date = min.AddMinutes(contexte.EntierEntre(0, minutes));
        return c.Formats.Contains(GenreFormat.DateSeule) ? date.Date : date;
    }

    private JsonNode GenererDateHeure(ReferenceType type, JeuContraintes c, string cle)
    {
        if (c.ValeurFixe is { } fixe && DateTime.TryParse(fixe.Texte, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dateFixe))
        {
            return JsonValue.Create(dateFixe);
        }

        var date = DateBrute(c, cle);
        var utc = c.Formats.Contains(GenreFormat.Utc);
        if (c.FormatDate is not null)
        {
            return JsonValue.Create(date.ToString(c.FormatDate.Modele, CultureInfo.InvariantCulture));
        }

        if (type.Nom == "DateTimeOffset")
        {
            return JsonValue.Create(new DateTimeOffset(date, utc ? TimeSpan.Zero : contexte.DecalageDateTimeOffset));
        }

        return JsonValue.Create(DateTime.SpecifyKind(date, utc ? DateTimeKind.Utc : DateTimeKind.Unspecified));
    }

    private JsonNode GenererDateSeule(JeuContraintes c, string cle)
    {
        var date = DateBrute(c, cle).Date;
        return JsonValue.Create(date.ToString(c.FormatDate?.Modele ?? "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private JsonNode GenererHeureSeule(JeuContraintes c)
    {
        var heure = new TimeOnly(contexte.Aleatoire.Next(24), contexte.Aleatoire.Next(60));
        return JsonValue.Create(heure.ToString(c.FormatDate?.Modele ?? "HH:mm:ss", CultureInfo.InvariantCulture));
    }

    private JsonNode GenererDuree(JeuContraintes c, string cle)
    {
        var facteur = FacteurUnite(c.Unite?.Symbole);
        var min = (c.Plage?.Min ?? 0) * facteur;
        var max = (c.Plage?.Max ?? 86400 / facteur) * facteur;
        if (c.Plage is { MinExclusif: true })
        {
            min += 1;
        }

        if (c.Plage is { MaxExclusif: true })
        {
            max -= 1;
        }

        if (min > max)
        {
            contexte.Avertir($"{cle} : plage de duree vide, minimum utilise");
            max = min;
        }

        var secondes = contexte.EntierEntre((long)Math.Max(0, min), (long)Math.Max(0, max));
        var duree = TimeSpan.FromSeconds(secondes);
        if (c.FormatDate is not null)
        {
            try
            {
                return JsonValue.Create(duree.ToString(c.FormatDate.Modele, CultureInfo.InvariantCulture));
            }
            catch (FormatException)
            {
                contexte.Avertir($"{cle} : format de duree « {c.FormatDate.Modele} » invalide, format c utilise");
            }
        }

        return JsonValue.Create(duree.ToString("c", CultureInfo.InvariantCulture));
    }

    private static decimal FacteurUnite(string? symbole)
    {
        var s = (symbole ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "ms" or "milliseconde" or "millisecondes" or "millisecond" or "milliseconds" => 0.001m,
            "min" or "minute" or "minutes" => 60,
            "h" or "heure" or "heures" or "hour" or "hours" => 3600,
            "j" or "jour" or "jours" or "day" or "days" => 86400,
            _ => 1,
        };
    }

    // ----- Octets et enums -----

    private JsonNode GenererOctets()
    {
        var octets = new byte[contexte.Aleatoire.Next(8, 17)];
        contexte.Aleatoire.NextBytes(octets);
        return JsonValue.Create(Convert.ToBase64String(octets));
    }

    private JsonNode? GenererEnum(ReferenceType type, JeuContraintes c, string cle)
    {
        var membres = type.Declaration?.MembresEnum ?? [];
        if (membres.Count == 0)
        {
            contexte.Avertir($"{cle} : enum {type.Nom} sans membre, genere a null");
            return null;
        }

        if (c.ValeurFixe is { } fixe)
        {
            var fixeMembre = membres.FirstOrDefault(m => string.Equals(m.Nom, fixe.Texte, StringComparison.OrdinalIgnoreCase));
            if (fixeMembre is not null && (!fixe.ParDefaut || !c.AContrainteDeValeur))
            {
                return Enum(fixeMembre);
            }

            if (fixeMembre is null)
            {
                contexte.Avertir($"{cle} : valeur « {fixe.Texte} » inconnue pour l'enum {type.Nom}");
            }
        }

        var candidats = membres
            .Where(m => c.Valeurs is null || c.Valeurs.Valeurs.Contains(m.Nom, StringComparer.OrdinalIgnoreCase))
            .Where(m => c.Exclusions is null || !c.Exclusions.Valeurs.Contains(m.Nom, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (type.Declaration is { EstFlags: true } && candidats.Any(m => m.Valeur != 0))
        {
            candidats = candidats.Where(m => m.Valeur != 0).ToList();
        }

        if (candidats.Count == 0)
        {
            contexte.Avertir($"{cle} : aucun membre d'enum compatible avec les contraintes, tirage libre");
            candidats = membres.ToList();
        }

        return Enum(candidats[contexte.Aleatoire.Next(candidats.Count)]);
    }

    private JsonNode Enum(MembreEnum membre) =>
        contexte.Options.EnumEnEntier ? JsonValue.Create(membre.Valeur) : JsonValue.Create(membre.Nom);
}
