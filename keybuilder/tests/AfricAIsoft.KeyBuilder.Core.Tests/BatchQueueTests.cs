using AfricAIsoft.KeyBuilder.Core.Abstractions;
using AfricAIsoft.KeyBuilder.Core.Models;
using AfricAIsoft.KeyBuilder.Core.Services;
using FluentAssertions;

namespace AfricAIsoft.KeyBuilder.Core.Tests;

public class BatchQueueTests
{
    [Fact]
    public async Task RunAsync_processes_items_in_order()
    {
        var q = new BatchQueue();
        q.Enqueue(new BuildPlan { SerialNumber = "1" }, "clé 1");
        q.Enqueue(new BuildPlan { SerialNumber = "2" }, "clé 2");
        q.Enqueue(new BuildPlan { SerialNumber = "3" }, "clé 3");
        var seen = new List<string>();
        var summary = await q.RunAsync(async (plan, _, _) =>
        {
            await Task.Yield();
            seen.Add(plan.SerialNumber);
            return new BuildResult { Success = true };
        }, NullProgressReporter.Instance, CancellationToken.None);
        seen.Should().Equal("1", "2", "3");
        summary.Succeeded.Should().Be(3);
        summary.Failed.Should().Be(0);
        summary.Total.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_captures_failures()
    {
        var q = new BatchQueue();
        q.Enqueue(new BuildPlan { SerialNumber = "a" }, "a");
        q.Enqueue(new BuildPlan { SerialNumber = "b" }, "b");
        var summary = await q.RunAsync((plan, _, _) =>
        {
            if (plan.SerialNumber == "b") throw new IOException("boom");
            return Task.FromResult(new BuildResult { Success = true });
        }, NullProgressReporter.Instance, CancellationToken.None);
        summary.Succeeded.Should().Be(1);
        summary.Failed.Should().Be(1);
        summary.Items[1].Status.Should().Be(BatchItemStatus.Failed);
        summary.Items[1].Result!.Errors.Should().Contain(e => e.Contains("boom"));
    }
}
