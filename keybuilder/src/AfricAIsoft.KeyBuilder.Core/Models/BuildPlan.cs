// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : plan de fabrication d'une clé — capture toutes les décisions
//           opérateur (source, cible, modèle, skills, plateformes, options).
//           Sérialisable JSON pour reprise après crash ET mode batch.
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using System.Text.Json.Serialization;
using AfricAIsoft.KeyBuilder.Core.Abstractions;

namespace AfricAIsoft.KeyBuilder.Core.Models;

public sealed class BuildPlan
{
    /// <summary>Racine du dépôt source (répertoire d'installation du studio).</summary>
    public string SourceRoot { get; set; } = "";

    /// <summary>Racine de la clé USB cible (E:\ ou /media/xxx).</summary>
    public string TargetRoot { get; set; } = "";

    /// <summary>Nom du fichier .gguf à embarquer (relatif à SourceRoot/models).</summary>
    public string ModelFile { get; set; } = "";

    /// <summary>Skills MCP à inclure (les autres seront filtrés).</summary>
    public List<string> IncludedSkillIds { get; set; } = new();

    /// <summary>Plateformes cibles (binaires embarqués correspondants).</summary>
    public List<TargetPlatform> TargetPlatforms { get; set; } = new();

    /// <summary>System prompt à injecter (remplace config/system_prompt.txt).</summary>
    public string? CustomSystemPrompt { get; set; }

    /// <summary>Système de fichiers demandé pour formatage (ou null = pas de format).</summary>
    public FileSystemKind? FormatWith { get; set; } = FileSystemKind.ExFat;

    /// <summary>Étiquette de volume à écrire lors du formatage.</summary>
    public string VolumeLabel { get; set; } = "AFRICAISOFT";

    /// <summary>Version du build (personnalisation).</summary>
    public string Version { get; set; } = "0.5.0";

    /// <summary>Identifiant client (imprimé dans le rapport et le marker).</summary>
    public string? ClientId { get; set; }

    /// <summary>Opérateur ayant lancé le build (utilisateur Windows courant).</summary>
    public string OperatorName { get; set; } = "";

    /// <summary>Chemin absolu vers un logo optionnel (copié dans ui/assets).</summary>
    public string? CustomLogoPath { get; set; }

    /// <summary>Features désactivées à la copie (retire dossiers/paramètres).</summary>
    public List<string> DisabledFeatures { get; set; } = new();

    /// <summary>Effectuer la vérification SHA-256 post-copie (recommandé).</summary>
    public bool VerifyChecksums { get; set; } = true;

    /// <summary>Lancer un test de démarrage rapide après copie.</summary>
    public bool RunSmokeTest { get; set; } = true;

    /// <summary>UUID unique de cette clé, écrit dans /marker.json.</summary>
    public string SerialNumber { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Horodatage de création du plan (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string ShortSerial => SerialNumber.Length > 8
        ? SerialNumber[..8].ToUpperInvariant()
        : SerialNumber;
}
