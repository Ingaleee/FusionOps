using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FusionOps.Cli.Commands;

public sealed class ScenarioCommand : Command
{
    private readonly IServiceProvider _services;

    public ScenarioCommand(IServiceProvider services) : base("scenario", "Scenario management commands")
    {
        _services = services;

        // run subcommand
        var projectOption = new Option<string>("--project", "Project ID (Guid)") { IsRequired = true };
        var deltaOption = new Option<int>("--delta", "Demand delta percent (e.g., 20)") { IsRequired = true };
        var fromOption = new Option<DateTime>("--from", "From timestamp (ISO)") { IsRequired = true };
        var toOption = new Option<DateTime>("--to", "To timestamp (ISO)") { IsRequired = true };
        var overtimeOption = new Option<bool>("--overtime", () => false, "Allow overtime");
        var licenseOverOption = new Option<int>("--license-over", () => 0, "Max license overage percent");
        var run = new Command("run", "Run a single what-if scenario");
        run.AddOption(projectOption);
        run.AddOption(deltaOption);
        run.AddOption(fromOption);
        run.AddOption(toOption);
        run.AddOption(overtimeOption);
        run.AddOption(licenseOverOption);
        run.SetHandler(async (string project, int delta, DateTime from, DateTime to, bool overtime, int licenseOver) =>
        {
            await HandleRun(project, delta, from, to, overtime, licenseOver);
        }, projectOption, deltaOption, fromOption, toOption, overtimeOption, licenseOverOption);

        // compare subcommand
        var fileOption = new Option<string>("--file", "Path to JSON array of RunScenarioCommand objects") { IsRequired = true };
        var compare = new Command("compare", "Run multiple scenarios and compare results");
        compare.AddOption(fileOption);
        compare.SetHandler(async (string file) => { await HandleCompare(file); }, fileOption);

        AddCommand(run);
        AddCommand(compare);
    }

    private async Task HandleRun(string project, int delta, DateTime from, DateTime to, bool overtime, int licenseOver)
    {
        var logger = _services.GetRequiredService<ILogger<ScenarioCommand>>();
        var http = _services.GetRequiredService<IHttpClientFactory>().CreateClient();
        var baseUrl = Environment.GetEnvironmentVariable("FUSIONOPS_API_URL") ?? "http://localhost:5122";
        var payload = new
        {
            ProjectId = Guid.Parse(project),
            DemandDeltaPercent = delta,
            From = from,
            To = to,
            AllowOvertime = overtime,
            MaxLicenseOveragePercent = licenseOver
        };
        var json = JsonSerializer.Serialize(payload);
        var res = await http.PostAsync($"{baseUrl}/api/v1/scenario/run", new StringContent(json, Encoding.UTF8, "application/json"));
        res.EnsureSuccessStatusCode();
        var text = await res.Content.ReadAsStringAsync();
        Console.WriteLine(text);
    }

    private async Task HandleCompare(string file)
    {
        var http = _services.GetRequiredService<IHttpClientFactory>().CreateClient();
        var baseUrl = Environment.GetEnvironmentVariable("FUSIONOPS_API_URL") ?? "http://localhost:5122";
        var json = await File.ReadAllTextAsync(file);
        var node = JsonNode.Parse(json);
        if (node is not JsonArray arr)
        {
            throw new InvalidOperationException("File must contain a JSON array of RunScenarioCommand objects.");
        }
        var envelope = new JsonObject { ["Commands"] = arr };
        using var content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json");
        var res = await http.PostAsync($"{baseUrl}/api/v1/scenario/compare", content);
        res.EnsureSuccessStatusCode();
        Console.WriteLine(await res.Content.ReadAsStringAsync());
    }
}


