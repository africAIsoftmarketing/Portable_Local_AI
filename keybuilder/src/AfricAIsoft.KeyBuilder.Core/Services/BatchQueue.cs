// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : file d'attente batch — traite plusieurs BuildPlan à la suite,
//           produit un BatchSummary agrégé. Thread-safe (single-writer).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;

namespace AfricAIsoft.KeyBuilder.Core.Services;

public sealed class BatchQueue
{
    private readonly List<BatchItem> _items = new();
    private readonly object _lock = new();
    public event EventHandler<BatchItem>? ItemChanged;

    public IReadOnlyList<BatchItem> Items { get { lock (_lock) return _items.ToList(); } }

    public BatchItem Enqueue(BuildPlan plan, string label)
    {
        lock (_lock)
        {
            var it = new BatchItem
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Label = label,
                Plan = plan,
                Status = BatchItemStatus.Pending,
            };
            _items.Add(it);
            return it;
        }
    }

    public void Remove(string id)
    {
        lock (_lock) _items.RemoveAll(x => x.Id == id);
    }

    /// <summary>
    /// Exécute la file en séquentiel. Chaque item reçoit le résultat via
    /// le délégué runOne (permet d'injecter n'importe quel orchestrateur).
    /// </summary>
    public async Task<BatchSummary> RunAsync(
        Func<BuildPlan, IProgressReporter, CancellationToken, Task<BuildResult>> runOne,
        IProgressReporter progress,
        CancellationToken ct)
    {
        var summary = new BatchSummary { StartedAtUtc = DateTime.UtcNow };
        List<BatchItem> snapshot;
        lock (_lock) snapshot = _items.ToList();

        int idx = 0;
        foreach (var item in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            idx++;
            item.Status = BatchItemStatus.Running;
            item.StartedAtUtc = DateTime.UtcNow;
            ItemChanged?.Invoke(this, item);
            progress.ReportMessage($"[Batch {idx}/{snapshot.Count}] {item.Label}",
                                    ProgressSeverity.Info);
            try
            {
                var res = await runOne(item.Plan, progress, ct);
                item.Result = res;
                item.Status = res.Success ? BatchItemStatus.Succeeded : BatchItemStatus.Failed;
                summary.Succeeded += res.Success ? 1 : 0;
                summary.Failed    += res.Success ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                item.Status = BatchItemStatus.Cancelled;
                summary.Cancelled++;
                throw;
            }
            catch (Exception ex)
            {
                item.Status = BatchItemStatus.Failed;
                item.Result = new BuildResult
                {
                    Success = false,
                    Errors = { ex.Message },
                    StartedAtUtc = item.StartedAtUtc ?? DateTime.UtcNow,
                    EndedAtUtc = DateTime.UtcNow,
                };
                summary.Failed++;
            }
            finally
            {
                item.EndedAtUtc = DateTime.UtcNow;
                ItemChanged?.Invoke(this, item);
            }
        }
        summary.EndedAtUtc = DateTime.UtcNow;
        summary.Total = snapshot.Count;
        summary.Items = snapshot;
        return summary;
    }
}

public sealed class BatchItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public BuildPlan Plan { get; set; } = new();
    public BatchItemStatus Status { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public BuildResult? Result { get; set; }
}

public enum BatchItemStatus { Pending, Running, Succeeded, Failed, Cancelled }

public sealed class BatchSummary
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public IReadOnlyList<BatchItem> Items { get; set; } = Array.Empty<BatchItem>();
}
