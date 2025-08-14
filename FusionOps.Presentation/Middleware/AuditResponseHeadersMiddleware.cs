using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;

namespace FusionOps.Presentation.Middleware;

public class AuditResponseHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public AuditResponseHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        // Добавляем ETag и Last-Modified для аудита
        if (context.Request.Path.StartsWithSegments("/api/v1/audit"))
        {
            var response = context.Response;
            var content = await GetResponseContent(response);
            
            if (!string.IsNullOrEmpty(content))
            {
                var etag = GenerateETag(content);
                response.Headers.ETag = etag;
                response.Headers.LastModified = DateTime.UtcNow.ToString("R");
            }
        }
    }

    private static Task<string> GetResponseContent(HttpResponse response)
    {
        // В реальном приложении здесь нужно получить содержимое ответа
        // Для простоты возвращаем пустую строку
        return Task.FromResult(string.Empty);
    }

    private static string GenerateETag(string content)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        return $"\"{Convert.ToBase64String(hash)}\"";
    }
}
