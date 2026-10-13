using FusionOps.Domain.Catalog;
using FusionOps.Domain.Enumerations;
using FusionOps.Domain.Services.Planning;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Workflows;

namespace FusionOps.Domain.Tests;

public sealed class PlanningQuoteTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T08:00:00Z");
    private static QuoteEvidence Evidence()
    {
        var candidate = new ResourceCandidate(Guid.NewGuid(), "Architect", ResourceKind.Human, Money.Usd(50), new Dictionary<string, int> { ["c#"] = 5 }, []);
        var request = new PlanningRequest(Guid.NewGuid(), new(Now.AddDays(1), Now.AddDays(1).AddHours(2)), Currency.USD,
            [new(Guid.NewGuid(), "Lead", ResourceKind.Human, new Dictionary<string, int> { ["c#"] = 5 })], [candidate]);
        return new(request, [new(1, candidate)], new ResourcePlanner().Plan(request));
    }
    [Test]
    public void QuoteExpiryBoundaryAndOneTimeConsumptionAreExplicit()
    {
        var quote = PlanningQuote.Create(Evidence(), "author", Now, 15);
        var expired = Assert.Throws<QuoteAdmissionException>(() => quote.Consume(Guid.NewGuid(), Now.AddSeconds(15)));
        Assert.That(expired!.Code, Is.EqualTo("quote_expired"));
        Assert.That(quote.ConsumedByWorkflowId, Is.Null);
        var workflow = Guid.NewGuid();
        quote.Consume(workflow, Now.AddSeconds(14));
        quote.Consume(workflow, Now.AddYears(1));
        Assert.That(Assert.Throws<QuoteAdmissionException>(() => quote.Consume(Guid.NewGuid(), Now))!.Code, Is.EqualTo("quote_consumed"));
    }
    [Test]
    public void QuoteEvidenceDoesNotExposeMutablePersistedCollections()
    {
        var evidence = Evidence();
        var quote = PlanningQuote.Create(evidence, "author", Now.AddTicks(9), 300);
        ((Dictionary<string, int>)evidence.Request.Candidates.Single().Skills)["c#"] = 0;
        var read = quote.Evidence;
        ((Dictionary<string, int>)read.Request.Candidates.Single().Skills)["c#"] = 1;
        Assert.That(quote.Evidence.Request.Candidates.Single().Skills["c#"], Is.EqualTo(5));
        Assert.That(quote.CreatedAt.UtcTicks % 10, Is.Zero);
        Assert.That(quote.Evidence.Result.TotalCost, Is.EqualTo(Money.Usd(100)));
    }
    [Test]
    public void BindingPreventsPriceAndIdentitySubstitutionAndChangesWorkflowFingerprint()
    {
        var quote = PlanningQuote.Create(Evidence(), "author", Now, 300);
        quote.VerifyBinding(quote.Binding);
        var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<PlanningQuoteBinding>(System.Text.Json.JsonSerializer.Serialize(quote.Binding, jsonOptions), jsonOptions), Is.EqualTo(quote.Binding));
        Assert.Throws<QuoteAdmissionException>(() => quote.VerifyBinding(quote.Binding with { Amount = 1 }));
        var evidence = quote.Evidence;
        var spec = new ReservationSpecification(evidence.Request.ProjectId, evidence.Result.Assignments.Select(a => a.ResourceId).ToArray(), evidence.Request.Period, [], Quote: quote.Binding);
        var a = ReservationWorkflow.Propose(Guid.NewGuid(), spec, "author", Now);
        var b = ReservationWorkflow.Propose(Guid.NewGuid(), spec with { Quote = quote.Binding with { Id = Guid.NewGuid() } }, "author", Now);
        Assert.That(a.SpecificationHash, Is.Not.EqualTo(b.SpecificationHash));
    }
    [Test]
    public void IncompleteQuotesExplainShortageButCannotBeConsumed()
    {
        var evidence = Evidence();
        var request = evidence.Request with { Candidates = [] };
        var quote = PlanningQuote.Create(new(request, [], new ResourcePlanner().Plan(request)), "author", Now, 300);
        Assert.That(quote.Evidence.Result.UnfilledSlots.Count, Is.EqualTo(1));
        Assert.That(Assert.Throws<QuoteAdmissionException>(() => quote.Consume(Guid.NewGuid(), Now))!.Code, Is.EqualTo("quote_incomplete"));
    }
    [Test]
    public void CatalogRejectsAmbiguousSkillKeysAndRetainsACopiedReadOnlyCollection()
    {
        var skills = new Dictionary<string, int> { ["C#"] = 5 };
        var resource = CatalogResource.Register(Guid.NewGuid(), "Architect", ResourceKind.Human, Money.Usd(50), skills);
        skills["C#"] = 0;
        Assert.That(resource.Skills.Single().Level, Is.EqualTo(5));
        Assert.Throws<DomainException>(() => resource.Revise("Architect", Money.Usd(50), new Dictionary<string, int> { ["c#"] = 4, ["C#"] = 5 }, null, true));
        Assert.That(resource.Revision, Is.EqualTo(1));
        Assert.Throws<DomainException>(() => resource.Revise("Architect", Money.Usd(0.0000001m), skills, null, true));
    }
}
