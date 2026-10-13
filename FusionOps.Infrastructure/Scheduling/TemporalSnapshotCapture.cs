using System.Data;
using FusionOps.Domain.Entities;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.ValueObjects;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.SqlServer;
using FusionOps.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Scheduling;

public sealed record CapturedTemporalSnapshot(TemporalSourceManifest Manifest, TemporalPlanningInput Input);
public sealed class TemporalSnapshotCapture(IDbContextFactory<WorkforceContext> workforce, IDbContextFactory<FulfillmentContext> fulfillment)
{
    public async Task<CapturedTemporalSnapshot> CaptureAsync(Guid projectId, TemporalScenarioRequestValues request, string actor, CancellationToken ct)
    {
        ProjectDefinition definition;
        long version, definitionVersion;
        string digest;
        DateTimeOffset pgObserved;
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        var prices = new Dictionary<string, Money>(StringComparer.Ordinal);
        var seats = new Dictionary<string, int>(StringComparer.Ordinal);
        var stockObservations = new List<StockObservation>(); var licenseObservations = new List<LicenseObservation>();
        // Each store contributes one bounded consistent observation. No cross-store atomic snapshot is implied.
        await using (var pg = await fulfillment.CreateDbContextAsync(ct))
        await using (var snapshot = await pg.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
        {
            var project = await pg.Set<OperationalProject>().Include(p => p.Members).AsSplitQuery().AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
                ?? throw new ProjectAccessException();
            project.Require(actor, ProjectPermission.Plan); project.VerifyEvidence();
            if (project.Version != request.ExpectedProjectVersion) throw new ProjectVersionException();
            definition = project.Definition; version = project.Version; definitionVersion = project.DefinitionVersion; digest = project.DefinitionDigest;
            var skuNames = definition.Tasks.SelectMany(t => t.Materials).Select(m => m.Sku).Distinct().Order().ToArray();
            var productNames = definition.Tasks.SelectMany(t => t.Licenses).Select(l => l.Product).Distinct().Order().ToArray();
            var items = await pg.StockItems.AsNoTracking().Where(s => skuNames.Contains(s.Sku)).ToListAsync(ct);
            var pools = await pg.Set<LicensePool>().AsNoTracking().Where(p => productNames.Contains(p.Product)).ToListAsync(ct);
            foreach (var sku in skuNames)
            {
                var item = items.SingleOrDefault(s => s.Sku == sku); stock[sku] = item?.Quantity ?? 0;
                if (item is not null) prices[sku] = item.UnitCost;
                stockObservations.Add(new(sku, item?.Id.Value, item?.Quantity ?? 0, item?.UnitCost));
            }
            foreach (var product in productNames)
            {
                var pool = pools.SingleOrDefault(p => p.Product == product); seats[product] = pool is null ? 0 : pool.TotalSeats - pool.AllocatedSeats;
                licenseObservations.Add(new(product, pool?.Id, pool?.Version, pool?.TotalSeats ?? 0, pool?.AllocatedSeats ?? 0));
            }
            pgObserved = (await pg.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").ToListAsync(ct)).Single();
            await snapshot.CommitAsync(ct);
        }
        var candidates = new List<ResourceCandidate>(); var revisions = new List<ResourceObservation>(); DateTimeOffset sqlObserved;
        await using (var sql = await workforce.CreateDbContextAsync(ct))
        await using (var snapshot = await sql.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            foreach (var id in request.CandidateIds.Order())
            {
                var resource = await CatalogLocks.RequireAsync(sql, id, true, ct);
                var bookings = await sql.Allocations.FromSqlInterpolated($"SELECT *, PeriodStart AS Period_Start, PeriodEnd AS Period_End FROM Allocations WITH (HOLDLOCK, INDEX(IX_Allocations_TenantId_ResourceId), FORCESEEK) WHERE TenantId = {sql.TenantId} AND ResourceId = {id}")
                    .AsNoTracking().Where(a => !a.IsCancelled && a.Period.Start < definition.Horizon.End && definition.Horizon.Start < a.Period.End)
                    .OrderBy(a => a.Period.Start).ThenBy(a => a.Id).Take(5001).ToListAsync(ct);
                if (bookings.Count > 5000 || candidates.Sum(c => c.BusyPeriods.Count) + bookings.Count > 20000)
                    throw new TemporalModelException("temporal_calendar_limits");
                candidates.Add(new(id, resource.Name, resource.Kind, resource.HourRate, resource.Skills.ToDictionary(s => s.Name, s => s.Level),
                    bookings.Select(a => a.Period).ToArray(), resource.EquipmentType)); revisions.Add(new(id, resource.Revision));
            }
            sqlObserved = await SqlQuoteAcceptance.DatabaseTimeAsync(sql, ct); await snapshot.CommitAsync(ct);
        }
        var input = new TemporalPlanningInput(projectId, definition, candidates, stock, seats, request.GridMinutes, request.MaxSearchNodes, prices, request.MaxElapsedMilliseconds);
        TemporalModel.Validate(input);
        return new(new(1, version, definitionVersion, digest, pgObserved, sqlObserved, revisions, stockObservations, licenseObservations), input);
    }
}
public sealed record TemporalScenarioRequestValues(long ExpectedProjectVersion, IReadOnlyList<Guid> CandidateIds,
    int GridMinutes, long MaxSearchNodes, int MaxElapsedMilliseconds);
