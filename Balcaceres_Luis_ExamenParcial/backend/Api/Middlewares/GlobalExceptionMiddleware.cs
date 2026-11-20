using System.Text.Json;
using Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.Middlewares;

/// <summary>
/// Middleware Personalizado #2: Manejo global de excepciones con respuesta JSON uniforme
/// que incluye el identificador de la solicitud (X-Request-Id) y registra en el archivo TXT.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IFileOperationLogger _fileLogger;

    public GlobalExceptionMiddleware(RequestDelegate next, IFileOperationLogger fileLogger)
    {
        _next = next;
        _fileLogger = fileLogger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var requestId = context.Items[RequestIdMiddleware.HeaderName]?.ToString() ?? context.TraceIdentifier;

            int statusCode;
            string errorType;
            string userMessage;
            string logCategory;

            if (ex is DbUpdateConcurrencyException)
            {
                statusCode = StatusCodes.Status409Conflict;
                errorType = "Conflicto de Concurrencia";
                userMessage = "Conflicto detectado (Regla 4): Otro usuario modificó o emitió la orden de compra de esta solicitud al mismo tiempo.";
                logCategory = "CONFLICTO_CONCURRENCIA";
            }
            else if (ex is DbUpdateException dbEx &&
                    (dbEx.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true ||
                     dbEx.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
            {
                statusCode = StatusCodes.Status409Conflict;
                errorType = "Conflicto de Unicidad / Concurrencia";
                userMessage = "Conflicto detectado (Regla 3 y 4): Ya se emitió una orden de compra para esta solicitud en una operación concurrente.";
                logCategory = "CONFLICTO_CONCURRENCIA";
            }
            else
            {
                statusCode = StatusCodes.Status500InternalServerError;
                errorType = "Error Interno del Servidor";
                userMessage = ex.Message;
                logCategory = "ERROR_EXCEPCION";
            }

            await _fileLogger.LogOperationAsync(
                requestId,
                logCategory,
                $"Excepción en {context.Request.Method} {context.Request.Path}: {userMessage} | Detalle técnico: {ex.GetType().Name} - {ex.Message}");

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.Headers[RequestIdMiddleware.HeaderName] = requestId;

                var payload = new
                {
                    requestId,
                    status = statusCode,
                    error = errorType,
                    message = userMessage,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await context.Response.WriteAsync(json);
            }
        }
    }
}
