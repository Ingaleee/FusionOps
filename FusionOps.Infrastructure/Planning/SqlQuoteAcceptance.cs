using FusionOps.Domain.Catalog;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FusionOps.Infrastructure.Planning;

public static class SqlQuoteAcceptance
{
    public static async Task<PlanningQuote> ValidateAsync(WorkforceContext context, ReservationWorkflow workflow, CancellationToken ct)
    {
        var binding = workflow.Quote ?? throw new InvalidOperationException("A quote binding is required");
        await context.Database.SqlQuery<Guid>($"SELECT Id AS Value FROM PlanningQuotes WITH (UPDLOCK, HOLDLOCK) WHERE Id = {binding.Id}").ToListAsync(ct);
        var quote = await context.PlanningQuotes.SingleOrDefaultAsync(q => q.Id == binding.Id, ct)
            ?? throw new QuoteAdmissionException("quote_missing");
        quote.VerifyBinding(binding);
        if (quote.ConsumedByWorkflowId is { } owner && owner != workflow.Id) throw new QuoteAdmissionException("quote_consumed");
        var evidence = quote.Evidence;
        if (workflow.ProjectId != evidence.Request.ProjectId || workflow.Period != evidence.Request.Period
            || !workflow.ResourceIds.Order().SequenceEqual(evidence.Result.Assignments.Select(a => a.ResourceId).Order()))
            throw new QuoteAdmissionException("quote_binding_mismatch");
        foreach (var id in workflow.ResourceIds.Order())
        {
            var resource = await CatalogLocks.RequireAsync(context, id, true, ct);
            if (resource.Revision != evidence.Candidates.Single(c => c.Resource.Id == id).Revision)
                throw new ResourceAdmissionException("catalog_changed", id);
        }
        return quote;
    }
    public static async Task<DateTimeOffset> DatabaseTimeAsync(WorkforceContext context, CancellationToken ct) =>
        (await context.Database.SqlQuery<DateTimeOffset>($"SELECT SYSDATETIMEOFFSET() AS Value").ToListAsync(ct)).Single();
}
