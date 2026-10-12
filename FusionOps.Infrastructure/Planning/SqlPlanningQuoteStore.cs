using System.Data;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Planning;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Planning;

public sealed class SqlPlanningQuoteStore(IDbContextFactory<WorkforceContext> factory, IExecutionContext execution,
    ResourcePlanner planner) : IPlanningQuoteStore
{
    public async Task<PlanningQuote> CreateAsync(QuoteRequest request, CancellationToken ct)
    {
        if (request.CandidateIds is not { Count: > 0 and <= 200 } || request.CandidateIds.Any(id => id == Guid.Empty)
            || request.CandidateIds.Distinct().Count() != request.CandidateIds.Count || request.Slots is not { Count: > 0 }
            || request.ValiditySeconds is < 15 or > 900)
            throw new DomainException("Provide 1–200 unique candidate IDs, 1–50 demand slots and validity of 15–900 seconds");
        // Validate all caller-controlled dimensions before taking database locks.
        planner.Plan(new(request.ProjectId, request.Period, request.Currency, request.Slots, []), ct);
        if (request.Period.Start.UtcTicks % 10 != 0 || request.Period.End.UtcTicks % 10 != 0)
            throw new DomainException("Authoritative quote periods require microsecond precision for exact cross-store reservation");
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var candidates = new List<QuotedCandidate>();
        foreach (var id in request.CandidateIds.Order())
        {
            var resource = await CatalogLocks.RequireAsync(context, id, true, ct);
            // One actual conflicting booking is sufficient evidence of ineligibility; bound history reads.
            var busy = await context.Allocations.Where(a => a.ResourceId == id && !a.IsCancelled
                && a.Period.Start < request.Period.End && request.Period.Start < a.Period.End)
                .OrderBy(a => a.Period.Start).ThenBy(a => a.Id).Select(a => a.Period).Take(1).ToListAsync(ct);
            candidates.Add(new(resource.Revision, new(id, resource.Name, resource.Kind, resource.HourRate,
                resource.Skills.ToDictionary(s => s.Name, s => s.Level), busy, resource.EquipmentType)));
        }
        var authoritative = new PlanningRequest(request.ProjectId, request.Period, request.Currency, request.Slots,
            candidates.Select(c => c.Resource).ToArray());
        var result = planner.Plan(authoritative, ct);
        var databaseNow = await SqlQuoteAcceptance.DatabaseTimeAsync(context, ct);
        var quote = PlanningQuote.Create(new(authoritative, candidates, result), execution.Actor, databaseNow, request.ValiditySeconds);
        context.PlanningQuotes.Add(quote);
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return quote;
    }
    public async Task<PlanningQuote?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        var quote = await context.PlanningQuotes.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, ct);
        quote?.VerifyIntegrity();
        return quote;
    }
}
