using FusionOps.Domain.Services.Planning;

namespace FusionOps.Domain.Tests;

public sealed class AssignmentSolverTests
{
    [Test]
    public void AssignmentBeatsRowWiseGreedySelection()
    {
        decimal?[,] costs = { { 1, 2 }, { 2, 100 } };
        var result = HungarianAssignmentSolver.Solve(costs);
        Assert.That(result.Columns, Is.EqualTo(new[] { 1, 0 }));
        Assert.That(result.TotalCost, Is.EqualTo(4));
    }

    [Test]
    public void SparseRandomMatricesMatchExhaustiveSearchAndHaveValidDualCertificates()
    {
        var random = new Random(7419);
        for (var sample = 0; sample < 300; sample++)
        {
            var rows = random.Next(1, 6);
            var columns = random.Next(rows, 7);
            var costs = new decimal?[rows, columns];
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    costs[row, column] = random.Next(4) == 0 ? null : random.Next(-200, 1000) / 10m;
            var expected = BruteForce(costs, 0, new HashSet<int>());
            var result = HungarianAssignmentSolver.Solve(costs);
            Assert.That(result.IsFeasible, Is.EqualTo(expected.HasValue), $"sample {sample}");
            if (!result.IsFeasible) continue;
            Assert.That(result.TotalCost, Is.EqualTo(expected));
            Assert.That(result.Columns.Distinct().Count(), Is.EqualTo(rows));
            Assert.That(result.RowPotentials.Sum() + result.ColumnPotentials.Sum(), Is.EqualTo(result.TotalCost));
            Assert.That(result.ColumnPotentials.All(v => v <= 0), Is.True);
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    if (costs[row, column] is { } cost)
                    {
                        var dual = result.RowPotentials[row] + result.ColumnPotentials[column];
                        Assert.That(dual, Is.LessThanOrEqualTo(cost));
                        if (result.Columns[row] == column) Assert.That(dual, Is.EqualTo(cost));
                    }
        }
    }

    [Test]
    public void EmptyAndImpossibleProblemsAreExplicit()
    {
        Assert.That(HungarianAssignmentSolver.Solve(new decimal?[0, 0]).IsFeasible, Is.True);
        Assert.That(HungarianAssignmentSolver.Solve(new decimal?[2, 1]).IsFeasible, Is.False);
        Assert.That(HungarianAssignmentSolver.Solve(new decimal?[1, 2]).IsFeasible, Is.False);
    }

    [Test]
    public void SolverHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => HungarianAssignmentSolver.Solve(new decimal?[1, 1], cancellation.Token));
    }

    private static decimal? BruteForce(decimal?[,] costs, int row, HashSet<int> used)
    {
        if (row == costs.GetLength(0)) return 0;
        decimal? best = null;
        for (var column = 0; column < costs.GetLength(1); column++)
        {
            if (used.Contains(column) || costs[row, column] is not { } cost) continue;
            used.Add(column);
            var suffix = BruteForce(costs, row + 1, used);
            used.Remove(column);
            if (suffix is not null && (best is null || suffix + cost < best)) best = suffix + cost;
        }
        return best;
    }
}
