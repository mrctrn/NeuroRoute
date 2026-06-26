using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace NeuroRoute.Service.Diagnostics;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var status = context.Response.StatusCode;

            _logger.LogInformation(
                "{Method} {Path} responded {Status} in {ElapsedMs}ms",
                method, path, status, sw.ElapsedMilliseconds);
        }
    }
}
