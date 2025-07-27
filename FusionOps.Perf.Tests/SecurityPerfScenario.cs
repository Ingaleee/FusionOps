using NBomber.CSharp;
using NBomber.Http;
using Newtonsoft.Json;
using System.Net.Http.Headers;

var httpFactory = HttpClientFactory.Create();

string jwt = "<PLACE_JWT_HERE>"; // TODO: supply via env var

var allocatePayload = new
{
    ProjectId = Guid.NewGuid(),
    ResourceIds = new[] { Guid.NewGuid() },
    PeriodFrom = DateTime.UtcNow,
    PeriodTo = DateTime.UtcNow.AddHours(1)
};
var jsonContent = new StringContent(JsonConvert.SerializeObject(allocatePayload));
jsonContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

var stepAuth = Step.Create("allocate-auth", clientFactory: httpFactory, execute: async ctx =>
{
    var request = Http.CreateRequest("POST", "http://localhost:5000/api/v1/allocate")
                      .WithHeader("Authorization", $"Bearer {jwt}")
                      .WithBody(jsonContent);
    return await Http.Send(request, ctx);
});

var stepNoAuth = Step.Create("allocate-no-auth", httpFactory, async ctx =>
{
    var request = Http.CreateRequest("POST", "http://localhost:5000/api/v1/allocate")
                      .WithBody(jsonContent);
    var res = await Http.Send(request, ctx);
    return res.StatusCode == System.Net.HttpStatusCode.Unauthorized ? Response.Ok() : Response.Fail();
});

var scn = ScenarioBuilder.CreateScenario("security-perf", new[] { stepAuth, stepNoAuth })
    .WithLoadSimulations(
        Simulation.RampingConstant(stepAuth, rate: 20, during: TimeSpan.FromMinutes(1)),
        Simulation.RampingConstant(stepNoAuth, rate: 5, during: TimeSpan.FromMinutes(1))
    );

NBomberRunner.RegisterScenarios(scn)
    .Run(); 