using FusionOps.Gateway.GraphQL;
using FusionOps.Gateway.SignalR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Polly;

var builder = WebApplication.CreateBuilder(args);

// 1. resilient REST клиент к FusionOps.Api
builder.Services.AddHttpClient("FusionApi", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["FusionApi:Base"] ?? "http://localhost:5000");
})
.AddTransientHttpErrorPolicy(p => p.RetryAsync(3));

// 2. SignalR клиент factory
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

// 4. CORS / Auth
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
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
