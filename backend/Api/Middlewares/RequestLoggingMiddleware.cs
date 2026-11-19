using System.Diagnostics;

namespace Api.Middlewares;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly string _logFilePath;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        var logsDir = Path.Combine(env.ContentRootPath, "Logs");
        Directory.CreateDirectory(logsDir);
        _logFilePath = Path.Combine(logsDir, "api-requests.log");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;
        var user = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? "Authenticated"
            : "Anonymous";

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var logLine = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] {method} {path} | User: {user} | Status: {statusCode} | {stopwatch.ElapsedMilliseconds}ms";

            _logger.LogInformation("{LogLine}", logLine);

            await _semaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_logFilePath, logLine + Environment.NewLine);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
