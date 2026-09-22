// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : accesseurs typés vers Resources.resx (culture neutre = fr-FR ;
//           override en-US disponible). Utilisés par KnowledgeBaseView.xaml
//           via {x:Static props:Resources.XXX} — garantit aucune chaîne en dur
//           dans le XAML. Le ResourceManager charge le .resources embarqué.
// Auteur  : AfricAIsoft — Licence : MIT
// ─────────────────────────────────────────────────────────────────────────────
using System.Globalization;
using System.Resources;

namespace AfricAIsoft.KeyBuilder.Wpf.Properties;

public static class Resources
{
    private static readonly ResourceManager _rm = new(
        "AfricAIsoft.KeyBuilder.Wpf.Properties.Resources",
        typeof(Resources).Assembly);

    /// <summary>Culture active (null = culture système). Peut être forcée à
    /// en-US via l'argument CLI --lang=en-US côté App.xaml.cs.</summary>
    public static CultureInfo? Culture { get; set; }

    private static string Get(string key) => _rm.GetString(key, Culture) ?? key;

    // ─── Bandeau global (déjà utilisées par MainWindow — restent hors XAML pour l'instant) ─
    public static string AppTitle             => Get(nameof(AppTitle));
    public static string DriveListLabel       => Get(nameof(DriveListLabel));
    public static string RefreshBtn           => Get(nameof(RefreshBtn));
    public static string ModelLabel           => Get(nameof(ModelLabel));
    public static string SkillsLabel          => Get(nameof(SkillsLabel));
    public static string PlatformsLabel       => Get(nameof(PlatformsLabel));
    public static string SystemPromptLabel    => Get(nameof(SystemPromptLabel));
    public static string ImportPromptBtn      => Get(nameof(ImportPromptBtn));
    public static string BuildBtn             => Get(nameof(BuildBtn));
    public static string AddBatchBtn          => Get(nameof(AddBatchBtn));
    public static string RunBatchBtn          => Get(nameof(RunBatchBtn));
    public static string VolumeLabelLabel     => Get(nameof(VolumeLabelLabel));
    public static string VersionLabel         => Get(nameof(VersionLabel));
    public static string ClientIdLabel        => Get(nameof(ClientIdLabel));
    public static string FormatLabel          => Get(nameof(FormatLabel));
    public static string FormatBeforeCopy     => Get(nameof(FormatBeforeCopy));
    public static string VerifyChecksums      => Get(nameof(VerifyChecksums));
    public static string SmokeTest            => Get(nameof(SmokeTest));
    public static string EstimationLabel      => Get(nameof(EstimationLabel));
    public static string StatusReady          => Get(nameof(StatusReady));

    // ─── Base de connaissances (RAG) — écran KnowledgeBaseView ─────────────
    public static string KnowledgeBaseTitle             => Get(nameof(KnowledgeBaseTitle));
    public static string KnowledgeBaseHint              => Get(nameof(KnowledgeBaseHint));
    public static string AddKnowledgeFilesBtn           => Get(nameof(AddKnowledgeFilesBtn));
    public static string AddKnowledgeFolderBtn          => Get(nameof(AddKnowledgeFolderBtn));
    public static string ClearKnowledgeBtn              => Get(nameof(ClearKnowledgeBtn));
    public static string RemoveKnowledgeItemBtn         => Get(nameof(RemoveKnowledgeItemBtn));
    public static string KnowledgeColumnDocument        => Get(nameof(KnowledgeColumnDocument));
    public static string KnowledgeColumnRelative        => Get(nameof(KnowledgeColumnRelative));
    public static string KnowledgeColumnSize            => Get(nameof(KnowledgeColumnSize));
    public static string KnowledgeReindexOnFirstLaunch  => Get(nameof(KnowledgeReindexOnFirstLaunch));
    public static string KnowledgeEmptyState            => Get(nameof(KnowledgeEmptyState));
    public static string KnowledgeLargeFileWarning      => Get(nameof(KnowledgeLargeFileWarning));
    public static string KnowledgeAddFilesTooltip       => Get(nameof(KnowledgeAddFilesTooltip));
    public static string KnowledgeAddFolderTooltip      => Get(nameof(KnowledgeAddFolderTooltip));
    public static string KnowledgeReindexTooltip        => Get(nameof(KnowledgeReindexTooltip));
}
