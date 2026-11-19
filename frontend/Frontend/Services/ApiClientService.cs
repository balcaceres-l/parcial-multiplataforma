using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Frontend.Models;

namespace Frontend.Services;

public interface IApiClientService
{
    HttpClient Client { get; }
    string BaseUrl { get; }
    Task<ApiHealthResponse?> CheckHealthAsync();
    Task<(LoginApiResponse? Data, ApiResultVm Result)> LoginAsync(string username, string password);
    Task<List<SolicitudCompraVm>> GetSolicitudesAsync(string? estado = null, string? departamento = null, string? busqueda = null);
    Task<SolicitudCompraVm?> GetSolicitudByIdAsync(int id);
    Task<ApiResultVm> CrearSolicitudAsync(CrearSolicitudVm model);
    Task<ApiResultVm> AprobarSolicitudAsync(int id);
    Task<ApiResultVm> EmitirOrdenCompraAsync(int id, EmitirOrdenVm model, string? customRequestId = null);
    Task<(ApiResultVm RespuestaA, ApiResultVm RespuestaB)> SimularConcurrenciaOrdenAsync(int id, string proveedor, decimal monto, string rowVersion);
    Task<LogsApiResponse?> GetLogsAsync();
    Task<ApiResultVm> ProbarExcepcionGlobalAsync();
}

public class ApiHealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string[] Features { get; set; } = [];
    public DateTime Timestamp { get; set; }
}

public class ApiClientService : IApiClientService
{
    public HttpClient Client { get; }
    public string BaseUrl { get; }
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ApiClientService(HttpClient client, IConfiguration config)
    {
        Client = client;
        BaseUrl = config["ApiSettings:BaseUrl"] ?? "http://localhost:5191";
    }

    public async Task<ApiHealthResponse?> CheckHealthAsync()
    {
        try
        {
            return await Client.GetFromJsonAsync<ApiHealthResponse>("/api/health");
        }
        catch
        {
            return null;
        }
    }

    public async Task<(LoginApiResponse? Data, ApiResultVm Result)> LoginAsync(string username, string password)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { username, password });
        var result = await ParseResponseAsync(response);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<LoginApiResponse>(body, _jsonOpts);
            return (data, result);
        }

        return (null, result);
    }

    public async Task<List<SolicitudCompraVm>> GetSolicitudesAsync(string? estado = null, string? departamento = null, string? busqueda = null)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(estado)) queryParams.Add($"estado={Uri.EscapeDataString(estado)}");
        if (!string.IsNullOrWhiteSpace(departamento)) queryParams.Add($"departamento={Uri.EscapeDataString(departamento)}");
        if (!string.IsNullOrWhiteSpace(busqueda)) queryParams.Add($"busqueda={Uri.EscapeDataString(busqueda)}");

        var url = "/api/solicitudes" + (queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "");
        var res = await Client.GetFromJsonAsync<List<SolicitudCompraVm>>(url, _jsonOpts);
        return res ?? [];
    }

    public async Task<SolicitudCompraVm?> GetSolicitudByIdAsync(int id)
    {
        var response = await Client.GetAsync($"/api/solicitudes/{id}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<SolicitudCompraVm>(_jsonOpts);
    }

    public async Task<ApiResultVm> CrearSolicitudAsync(CrearSolicitudVm model)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model.Departamento ?? ""), "Departamento");
        form.Add(new StringContent(model.Descripcion ?? ""), "Descripcion");
        form.Add(new StringContent(model.Justificacion ?? ""), "Justificacion");
        form.Add(new StringContent(model.MontoEstimado.ToString(System.Globalization.CultureInfo.InvariantCulture)), "MontoEstimado");

        if (model.Imagen != null && model.Imagen.Length > 0)
        {
            var streamContent = new StreamContent(model.Imagen.OpenReadStream());
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(model.Imagen.ContentType);
            form.Add(streamContent, "Imagen", model.Imagen.FileName);
        }

        var response = await Client.PostAsync("/api/solicitudes", form);
        return await ParseResponseAsync(response);
    }

    public async Task<ApiResultVm> AprobarSolicitudAsync(int id)
    {
        var response = await Client.PutAsync($"/api/solicitudes/{id}/aprobar", null);
        return await ParseResponseAsync(response);
    }

    public async Task<ApiResultVm> EmitirOrdenCompraAsync(int id, EmitirOrdenVm model, string? customRequestId = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model.Proveedor ?? ""), "Proveedor");
        form.Add(new StringContent(model.Monto.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Monto");
        if (!string.IsNullOrWhiteSpace(model.RowVersion))
        {
            form.Add(new StringContent(model.RowVersion), "RowVersion");
        }

        if (model.ComprobanteImagen != null && model.ComprobanteImagen.Length > 0)
        {
            var streamContent = new StreamContent(model.ComprobanteImagen.OpenReadStream());
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(model.ComprobanteImagen.ContentType);
            form.Add(streamContent, "ComprobanteImagen", model.ComprobanteImagen.FileName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/solicitudes/{id}/orden-compra")
        {
            Content = form
        };

        if (!string.IsNullOrWhiteSpace(customRequestId))
        {
            request.Headers.Add("X-Request-Id", customRequestId);
        }

        var response = await Client.SendAsync(request);
        return await ParseResponseAsync(response);
    }

    /// <summary>
    /// Simula 2 usuarios/empleados procesando la misma solicitud al mismo tiempo (Task.WhenAll)
    /// enviando exactamente el mismo RowVersion leído previamente para demostrar la Regla de Negocio 4.
    /// </summary>
    public async Task<(ApiResultVm RespuestaA, ApiResultVm RespuestaB)> SimularConcurrenciaOrdenAsync(
        int id, string proveedor, decimal monto, string rowVersion)
    {
        var reqIdA = $"SIM-EMPLEADO1-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        var reqIdB = $"SIM-EMPLEADO2-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var modelA = new EmitirOrdenVm
        {
            SolicitudId = id,
            Proveedor = $"{proveedor} (Intento Empleado A)",
            Monto = monto,
            RowVersion = rowVersion
        };

        var modelB = new EmitirOrdenVm
        {
            SolicitudId = id,
            Proveedor = $"{proveedor} (Intento Empleado B)",
            Monto = monto,
            RowVersion = rowVersion
        };

        var taskA = EmitirOrdenCompraAsync(id, modelA, reqIdA);
        var taskB = EmitirOrdenCompraAsync(id, modelB, reqIdB);

        var results = await Task.WhenAll(taskA, taskB);
        return (results[0], results[1]);
    }

    public async Task<LogsApiResponse?> GetLogsAsync()
    {
        return await Client.GetFromJsonAsync<LogsApiResponse>("/api/solicitudes/logs", _jsonOpts);
    }

    public async Task<ApiResultVm> ProbarExcepcionGlobalAsync()
    {
        var response = await Client.GetAsync("/api/solicitudes/probar-excepcion");
        return await ParseResponseAsync(response);
    }

    private static async Task<ApiResultVm> ParseResponseAsync(HttpResponseMessage response)
    {
        var requestId = response.Headers.Contains("X-Request-Id")
            ? response.Headers.GetValues("X-Request-Id").FirstOrDefault() ?? ""
            : "";

        var body = await response.Content.ReadAsStringAsync();
        string message = response.ReasonPhrase ?? "Operación completada";
        string? errorTitle = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("requestId", out var rId) && string.IsNullOrWhiteSpace(requestId))
                {
                    requestId = rId.GetString() ?? "";
                }
                if (root.TryGetProperty("message", out var msgProp))
                {
                    message = msgProp.GetString() ?? message;
                }
                if (root.TryGetProperty("error", out var errProp))
                {
                    errorTitle = errProp.GetString();
                }
            }
            catch
            {
                message = body;
            }
        }

        return new ApiResultVm
        {
            Success = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            RequestId = requestId,
            Message = message,
            ErrorTitle = errorTitle
        };
    }
}
