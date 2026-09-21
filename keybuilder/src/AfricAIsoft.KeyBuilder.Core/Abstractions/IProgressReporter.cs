// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : callback de progression fine (étape, fichier courant, %, ETA).
//           Injecté partout où une opération est longue (copie, checksum, etc.).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
namespace AfricAIsoft.KeyBuilder.Core.Abstractions;

public interface IProgressReporter
{
    void ReportStep(string stepId, string localizedLabel);
    void ReportFile(string relativePath, long bytesDone, long bytesTotal,
                    double bytesPerSecond, TimeSpan? eta);
    void ReportOverall(int itemsDone, int itemsTotal, double percent);
    void ReportMessage(string message, ProgressSeverity severity = ProgressSeverity.Info);
}

public enum ProgressSeverity { Info, Warning, Error }

/// <summary>Reporter neutre pour les tests / opérations silencieuses.</summary>
public sealed class NullProgressReporter : IProgressReporter
{
    public static readonly NullProgressReporter Instance = new();
    public void ReportStep(string stepId, string localizedLabel) { }
    public void ReportFile(string relativePath, long bytesDone, long bytesTotal,
                            double bytesPerSecond, TimeSpan? eta) { }
    public void ReportOverall(int itemsDone, int itemsTotal, double percent) { }
    public void ReportMessage(string message, ProgressSeverity severity) { }
}
