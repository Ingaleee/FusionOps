using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FusionOps.Infrastructure.Projector;

public static class ProjectorMetrics
{
    private static double _projectionLagSeconds;
    public static void SetLag(double lag) => _projectionLagSeconds = lag;

    public static void MapMetrics(this WebApplication app)
    {
        app.MapGet("/metrics", async context =>
        {
            await context.Response.WriteAsync($"# HELP projection_lag_seconds\n# TYPE projection_lag_seconds gauge\nprojection_lag_seconds {_projectionLagSeconds}\n");
        });
    }
}