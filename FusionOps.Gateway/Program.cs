using FusionOps.Gateway.GraphQL;
using FusionOps.Gateway.SignalR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http.Headers;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.HttpOverrides;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// auth context accessor
builder.Services.AddHttpContextAccessor();

// Response compression (gzip/br)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/graphql-response+json",
        "application/json",
        "application/problem+json",
        "text/plain"
    });
});

// OpenTelemetry Metrics (service name: fusionops-gateway)
builder.Services.AddOpenTelemetry()
    .WithMetrics(m =>
    {
        m.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("fusionops-gateway"))
         .AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddPrometheusExporter();
    });

// Rate limiting: глобально + именованные политики для GraphQL и /metrics
builder.Services.AddRateLimiter(options =>
{
    // Глобальный сейфгард
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(partitionKey: "global", factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 50,
            AutoReplenishment = true
        }));

    // Политика по умолчанию для GraphQL
    options.AddFixedWindowLimiter("default", opt =>
    {
        opt.PermitLimit = 80;
        opt.Window = TimeSpan.FromSeconds(1);
        opt.QueueLimit = 40;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.AutoReplenishment = true;
    });

    // Более строгая политика для /metrics
    options.AddFixedWindowLimiter("metrics", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromSeconds(1);
        opt.QueueLimit = 5;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.AutoReplenishment = true;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// HealthChecks
builder.Services.AddHealthChecks();

// 1. resilient REST клиент к FusionOps.Api + JWT propagation
builder.Services.AddHttpClient("FusionApi", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["FusionApi:Base"] ?? "http://localhost:5000");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
})
.AddHttpMessageHandler(sp => new JwtPropagationHandler(sp.GetRequiredService<IHttpContextAccessor>()))
.AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError().OrResult(r => (int)r.StatusCode == 429)
    .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(100 * i)))
.AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5)));

// 2. SignalR клиент factory
builder.Services.AddSingleton<SignalRClientFactory>();

// 3. HotChocolate
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddSubscriptionType<Subscription>()
    .AddInMemorySubscriptions()
    .AddTypeExtension<AllocationResolvers>()
    .AddDataLoader<AllocationDataLoader>();

// 4. CORS / Auth (разрешаем нужные источники из конфигурации)
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length == 0 || (allowedOrigins.Length == 1 && allowedOrigins[0] == "*"))
    {
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    }
    else
    {
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
}));

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

// За прокси (ingress): корректная схема/host из X-Forwarded-*
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseWebSockets();
app.UseResponseCompression();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGraphQL().RequireRateLimiting("default");
app.MapHealthChecks("/live").RequireRateLimiting("default");
app.MapHealthChecks("/ready").RequireRateLimiting("default");
app.MapGet("/healthz", () => "OK").RequireRateLimiting("default");
app.MapPrometheusScrapingEndpoint().RequireRateLimiting("metrics");
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
        var correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier
            ?? _httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class CorrelationMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;
    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var cid) || string.IsNullOrWhiteSpace(cid))
        {
            cid = context.TraceIdentifier;
            context.Response.Headers[HeaderName] = cid;
        }
        else
        {
            context.Response.Headers[HeaderName] = cid.ToString();
        }
        await _next(context);
    }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        // Relax CSP for GraphQL IDE to avoid blank screen due to blocked inline scripts/styles
        if (context.Request.Path.StartsWithSegments("/graphql"))
        {
            headers["Content-Security-Policy"] = string.Join("; ", new[]
            {
                "default-src 'self'",
                "object-src 'none'",
                "frame-ancestors 'none'",
                // Banana Cake Pop needs inline/eval and websockets for subscriptions
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' https: blob:",
                "style-src 'self' 'unsafe-inline' https:",
                "img-src 'self' data: blob: https:",
                "font-src 'self' data: https:",
                "connect-src 'self' http: https: ws: wss:"
            });
        }
        else
        {
            headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; object-src 'none'";
        }
        await _next(context);
    }
}
