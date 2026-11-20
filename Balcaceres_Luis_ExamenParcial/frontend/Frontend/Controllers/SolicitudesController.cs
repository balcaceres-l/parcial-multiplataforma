using Frontend.Models;
using Frontend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Frontend.Controllers;

[Authorize]
public class SolicitudesController : Controller
{
    private readonly IApiClientService _apiClient;

    public SolicitudesController(IApiClientService apiClient)
    {
        _apiClient = apiClient;
    }

    /// <summary>
    /// RF2: Listar solicitudes con departamento, monto, estado y fecha, además de filtro.
    /// - Rol "Solicitante": Solo ve sus propias solicitudes.
    /// - Rol "Admin": Ve todas las solicitudes de todas las personas.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? estado = null, string? departamento = null, string? busqueda = null)
    {
        var solicitudes = await _apiClient.GetSolicitudesAsync(estado, departamento, busqueda);
        ViewBag.FiltroEstado = estado;
        ViewBag.FiltroDepartamento = departamento;
        ViewBag.FiltroBusqueda = busqueda;
        ViewBag.ApiBaseUrl = _apiClient.BaseUrl;
        return View(solicitudes);
    }

    /// <summary>
    /// RF1: Formulario para registrar solicitud de compra.
    /// </summary>
    [HttpGet]
    public IActionResult Crear()
    {
        var deptoClaim = User.FindFirst("Departamento")?.Value ?? "Infraestructura TI";
        return View(new CrearSolicitudVm { Departamento = deptoClaim });
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearSolicitudVm model)
    {
        var res = await _apiClient.CrearSolicitudAsync(model);
        if (res.Success)
        {
            TempData["Success"] = $"{res.Message} (X-Request-Id: {res.RequestId})";
            return RedirectToAction(nameof(Index));
        }

        TempData["Error"] = $"Error {res.StatusCode} [{res.ErrorTitle}]: {res.Message} (X-Request-Id: {res.RequestId})";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Detalles(int id)
    {
        var solicitud = await _apiClient.GetSolicitudByIdAsync(id);
        if (solicitud == null)
        {
            TempData["Error"] = $"No tienes acceso a la solicitud #{id} o no existe.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ApiBaseUrl = _apiClient.BaseUrl;
        return View(solicitud);
    }

    /// <summary>
    /// RF3: Aprobar solicitud pendiente (Solo Admin).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Aprobar(int id)
    {
        var res = await _apiClient.AprobarSolicitudAsync(id);
        if (res.Success)
        {
            TempData["Success"] = $"{res.Message} (X-Request-Id: {res.RequestId})";
        }
        else
        {
            TempData["Error"] = $"Error {res.StatusCode} [{res.ErrorTitle}]: {res.Message} (X-Request-Id: {res.RequestId})";
        }

        return RedirectToAction(nameof(Detalles), new { id });
    }

    /// <summary>
    /// RF4: Emitir orden de compra desde una solicitud Aprobada (Solo Admin).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EmitirOrden(EmitirOrdenVm model)
    {
        var res = await _apiClient.EmitirOrdenCompraAsync(model.SolicitudId, model);
        if (res.Success)
        {
            TempData["Success"] = $"{res.Message} (X-Request-Id: {res.RequestId})";
        }
        else
        {
            TempData["Error"] = $"[{res.StatusCode} - {res.ErrorTitle}] {res.Message} (X-Request-Id: {res.RequestId})";
        }

        return RedirectToAction(nameof(Detalles), new { id = model.SolicitudId });
    }

    /// <summary>
    /// Demostración en vivo de la Regla de Negocio 4: Lanza 2 peticiones HTTP simultáneas
    /// intentando emitir la orden de compra sobre la misma solicitud Aprobada.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SimularConcurrencia(int solicitudId, string proveedor, decimal monto, string rowVersion)
    {
        var (respA, respB) = await _apiClient.SimularConcurrenciaOrdenAsync(solicitudId, proveedor, monto, rowVersion);

        TempData["ConcurrencySimulation"] =
            $"RESULTADO SIMULACIÓN DE CONCURRENCIA (REGLA 4): " +
            $"• Petición 1 [{respA.RequestId}]: HTTP {respA.StatusCode} -> {respA.Message} | " +
            $"• Petición 2 [{respB.RequestId}]: HTTP {respB.StatusCode} -> {respB.Message}";

        return RedirectToAction(nameof(Detalles), new { id = solicitudId });
    }

    /// <summary>
    /// Panel exclusivo del Admin para auditar todos los cambios realizados sobre todas las solicitudes
    /// (quién creó, quién aprobó, quién emitió orden y conflictos detectados).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Auditoria()
    {
        var historial = await _apiClient.GetAuditoriaAsync();
        return View(historial);
    }

    /// <summary>
    /// Vista exclusiva del Admin para consultar los Logs en archivo TXT generados por los middlewares y operaciones.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Logs()
    {
        var logs = await _apiClient.GetLogsAsync();
        return View(logs ?? new LogsApiResponse());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ProbarExcepcion()
    {
        var res = await _apiClient.ProbarExcepcionGlobalAsync();
        TempData["Error"] = $"Middleware Global de Excepciones verificado -> HTTP {res.StatusCode} [{res.ErrorTitle}]: {res.Message} (X-Request-Id: {res.RequestId})";
        return RedirectToAction(nameof(Logs));
    }
}
