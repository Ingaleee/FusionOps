namespace FusionOps.Domain.Services.Planning;

public sealed record AssignmentSolution(
    bool IsFeasible, IReadOnlyList<int> Columns, decimal TotalCost,
    IReadOnlyList<decimal> RowPotentials, IReadOnlyList<decimal> ColumnPotentials);

/// <summary>
/// Rectangular shortest augmenting path Hungarian algorithm: O(rows² × columns), O(rows + columns) workspace.
/// A null edge is forbidden. Decimal arithmetic avoids floating point drift in monetary objectives.
/// </summary>
public static class HungarianAssignmentSolver
{
    public static AssignmentSolution Solve(decimal?[,] costs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(costs);
        var rows = costs.GetLength(0);
        var columns = costs.GetLength(1);
        if (rows > columns) return new(false, [], 0, [], []);
        var u = new decimal[rows + 1];
        var v = new decimal[columns + 1];
        var matchedRow = new int[columns + 1];
        var previousColumn = new int[columns + 1];

        for (var row = 1; row <= rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            matchedRow[0] = row;
            var currentColumn = 0;
            var minimum = new decimal?[columns + 1];
            var used = new bool[columns + 1];
            do
            {
                used[currentColumn] = true;
                var currentRow = matchedRow[currentColumn];
                decimal? delta = null;
                var nextColumn = 0;
                for (var column = 1; column <= columns; column++)
                {
                    if (used[column]) continue;
                    if (costs[currentRow - 1, column - 1] is { } cost)
                    {
                        var reducedCost = cost - u[currentRow] - v[column];
                        if (minimum[column] is null || reducedCost < minimum[column])
                        {
                            minimum[column] = reducedCost;
                            previousColumn[column] = currentColumn;
                        }
                    }
                    if (minimum[column] is { } frontierCost && (delta is null || frontierCost < delta))
                    {
                        delta = frontierCost;
                        nextColumn = column;
                    }
                }
                if (delta is null) return new(false, [], 0, [], []);
                for (var column = 0; column <= columns; column++)
                {
                    if (used[column])
                    {
                        u[matchedRow[column]] += delta.Value;
                        v[column] -= delta.Value;
                    }
                    else if (minimum[column] is not null) minimum[column] -= delta.Value;
                }
                currentColumn = nextColumn;
            } while (matchedRow[currentColumn] != 0);

            do
            {
                var predecessor = previousColumn[currentColumn];
                matchedRow[currentColumn] = matchedRow[predecessor];
                currentColumn = predecessor;
            } while (currentColumn != 0);
        }

        var assignment = new int[rows];
        for (var column = 1; column <= columns; column++)
            if (matchedRow[column] != 0) assignment[matchedRow[column] - 1] = column - 1;
        var total = Enumerable.Range(0, rows).Sum(row => costs[row, assignment[row]]!.Value);
        return new(true, assignment, total, u[1..], v[1..]);
    }
}
