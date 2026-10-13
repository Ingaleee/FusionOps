using System.Text.Json;
using System.Text.Json.Serialization;
using FusionOps.Domain.Projects;

namespace FusionOps.Domain.Scheduling;

public static class TemporalInputCodec
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static string Write(TemporalPlanningInput input)
    {
        TemporalModel.Validate(input);
        var normalized = input with
        {
            Definition = ProjectDefinitionCodec.Read(ProjectDefinitionCodec.Canonicalize(input.Definition)),
            Candidates = input.Candidates.OrderBy(c => c.Id).Select(c => c with
            {
                Skills = c.Skills.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value),
                BusyPeriods = c.BusyPeriods.OrderBy(p => p.Start).ThenBy(p => p.End).ToArray()
            }).ToArray(),
            Stock = input.Stock.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            LicenseCapacity = input.LicenseCapacity.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            MaterialUnitCosts = input.MaterialUnitCosts?.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value)
        };
        var text = JsonSerializer.Serialize(normalized, Json);
        if (text.Length > 4_000_000) throw new TemporalModelException("temporal_evidence_limits");
        return text;
    }
    public static TemporalPlanningInput Read(string text) => JsonSerializer.Deserialize<TemporalPlanningInput>(text, Json)
        ?? throw new TemporalModelException("temporal_evidence_missing");
}
