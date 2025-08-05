using FusionOps.Gateway.GraphQL;
using FusionOps.Gateway.SignalR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// auth context accessor
builder.Services.AddHttpContextAccessor();

// 1. resilient REST клиент к FusionOps.Api + JWT propagation
builder.Services.AddHttpClient("FusionApi", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["FusionApi:Base"] ?? "http://localhost:5000");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
})
.AddHttpMessageHandler(() => new JwtPropagationHandler(builder.Services.BuildServiceProvider()))
.AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError().OrResult(r => (int)r.StatusCode == 429)
    .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(100 * i)))
.AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5)));

// 2. SignalR клиент factory (auth pass-through может быть добавлен внутри фабрики)
builder.Services.AddSingleton<SignalRClientFactory>();

// 3. HotChocolate
builder.Services
    .AddGraphQLServer()
    .AddDocumentFromFile("Schema.graphql")
    .AddQueryType<Query>()
    .AddSubscriptionType<Subscription>()
    .AddInMemorySubscriptions()
    .AddTypeExtension<AllocationResolvers>()
    .AddDataLoader<AllocationDataLoader>();

// 4. CORS / Auth (разрешаем нужные источники из конфигурации)
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "*" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()
));

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.Audience = builder.Configuration["Jwt:Audience"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });

var app = builder.Build();
app.UseWebSockets();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGraphQL();
app.MapGet("/healthz", () => "OK");
app.Run();

public class JwtPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public JwtPropagationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
