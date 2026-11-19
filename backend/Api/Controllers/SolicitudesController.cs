using System.Security.Claims;
using Api.Data;
using Api.Middlewares;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

public class CrearSolicitudDto
{
    public string Departamento { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Justificacion { get; set; } = string.Empty;
    public decimal MontoEstimado { get; set; }
    public IFormFile? Imagen { get; set; }
}

public class EmitirOrdenDto
{
    public string Proveedor { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string? RowVersion { get; set; }
    public IFormFile? ComprobanteImagen { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class SolicitudesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IImageService _imageService;
    private readonly IFileOperationLogger _fileLogger;
    // Semáforo para coordinar demostraciones de concurrencia o transacciones cuando dos hilos llegan exactamente al mismo milisegundo
    private static readonly SemaphoreSlim _concurrencyGate = new(1, 1);

    public SolicitudesController(AppDbContext db, IImageService imageService, IFileOperationLogger fileLogger)
    {
        _db = db;
        _imageService = imageService;
        _fileLogger = fileLogger;
    }

    private string GetRequestId() =>
        HttpContext.Items[RequestIdMiddleware.HeaderName]?.ToString() ?? HttpContext.TraceIdentifier;

    private string GetCurrentActor()
    {
        var nombre = User.FindFirst("NombreCompleto")?.Value ?? User.Identity?.Name ?? "Usuario";
        var depto = User.FindFirst("Departamento")?.Value;
        return string.IsNullOrWhiteSpace(depto) ? nombre : $"{nombre} ({depto})";
    }

    /// <summary>
    /// RF2: Listar solicitudes con departamento, monto, estado y fecha, además de filtro para encontrarlas fácilmente.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListarSolicitudes(
        [FromQuery] string? estado = null,
        [FromQuery] string? departamento = null,
        [FromQuery] string? busqueda = null)
    {
        var query = _db.SolicitudesCompra
            .Include(s => s.OrdenCompra)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado))
        {
            query = query.Where(s => s.Estado == estado);
        }

        if (!string.IsNullOrWhiteSpace(departamento))
        {
            query = query.Where(s => s.Departamento.ToLower().Contains(departamento.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var term = busqueda.ToLower();
            query = query.Where(s =>
                s.Descripcion.ToLower().Contains(term) ||
                s.Justificacion.ToLower().Contains(term) ||
                s.Departamento.ToLower().Contains(term) ||
                s.CreadoPor.ToLower().Contains(term));
        }

        var lista = await query
            .OrderByDescending(s => s.FechaCreacion)
            .ToListAsync();

        return Ok(lista);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id)
    {
        var requestId = GetRequestId();
        var solicitud = await _db.SolicitudesCompra
            .Include(s => s.OrdenCompra)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (solicitud == null)
        {
            return NotFound(new
            {
                requestId,
                status = 404,
                error = "No encontrado",
                message = $"La solicitud #{id} no existe.",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        return Ok(solicitud);
    }

    /// <summary>
    /// RF1: Registrar solicitud de compra con departamento, descripción, justificación y monto estimado.
    /// Estado inicial: Pendiente.
    /// Regla de negocio 1: El monto debe ser mayor a 0 y la justificación es obligatoria.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CrearSolicitud([FromForm] CrearSolicitudDto dto)
    {
        var requestId = GetRequestId();

        // Regla de negocio 1: El monto debe ser mayor a 0 y la justificación es obligatoria
        if (dto.MontoEstimado <= 0)
        {
            var msg = "Regla de Negocio 1 violada: El monto estimado debe ser mayor a 0.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_VALIDACION", msg);
            return BadRequest(new
            {
                requestId,
                status = 400,
                error = "Regla de Negocio 1",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        if (string.IsNullOrWhiteSpace(dto.Justificacion))
        {
            var msg = "Regla de Negocio 1 violada: La justificación de la solicitud es obligatoria.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_VALIDACION", msg);
            return BadRequest(new
            {
                requestId,
                status = 400,
                error = "Regla de Negocio 1",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        if (string.IsNullOrWhiteSpace(dto.Departamento) || string.IsNullOrWhiteSpace(dto.Descripcion))
        {
            var msg = "El departamento y la descripción del producto o suministro son obligatorios.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_VALIDACION", msg);
            return BadRequest(new
            {
                requestId,
                status = 400,
                error = "Datos incompletos",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        string? imagenUrl = null;
        if (dto.Imagen != null && dto.Imagen.Length > 0)
        {
            imagenUrl = await _imageService.SaveImageAsync(dto.Imagen, "uploads/solicitudes");
        }

        var actor = GetCurrentActor();
        var nueva = new SolicitudCompra
        {
            Departamento = dto.Departamento.Trim(),
            Descripcion = dto.Descripcion.Trim(),
            Justificacion = dto.Justificacion.Trim(),
            MontoEstimado = dto.MontoEstimado,
            Estado = EstadoSolicitud.Pendiente, // RF1: Estado inicial Pendiente
            FechaCreacion = DateTime.Now,
            CreadoPor = actor,
            ImagenUrl = imagenUrl,
            RowVersion = Guid.NewGuid().ToString("N")
        };

        _db.SolicitudesCompra.Add(nueva);
        await _db.SaveChangesAsync();

        await _fileLogger.LogOperationAsync(
            requestId,
            "SOLICITUD_CREADA",
            $"Solicitud #{nueva.Id} registrada | Depto: '{nueva.Departamento}' | Monto: ${nueva.MontoEstimado:N2} | Estado: '{nueva.Estado}' | Usuario: '{actor}'");

        return CreatedAtAction(nameof(ObtenerPorId), new { id = nueva.Id }, new
        {
            requestId,
            message = "Solicitud de compra registrada exitosamente en estado Pendiente.",
            data = nueva
        });
    }

    /// <summary>
    /// RF3: Aprobar solicitud: Cambiar una solicitud pendiente a aprobada, registrando quién la aprobó.
    /// Regla de negocio 2: Solo una solicitud Pendiente puede aprobarse.
    /// </summary>
    [HttpPut("{id:int}/aprobar")]
    [Authorize(Roles = "Aprobador,Admin")]
    public async Task<IActionResult> AprobarSolicitud(int id)
    {
        var requestId = GetRequestId();
        var solicitud = await _db.SolicitudesCompra.FirstOrDefaultAsync(s => s.Id == id);

        if (solicitud == null)
        {
            return NotFound(new
            {
                requestId,
                status = 404,
                error = "No encontrado",
                message = $"La solicitud #{id} no existe.",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        // Regla de negocio 2: Solo una solicitud Pendiente puede aprobarse
        if (solicitud.Estado != EstadoSolicitud.Pendiente)
        {
            var msg = $"Regla de Negocio 2 violada: Solo una solicitud en estado 'Pendiente' puede aprobarse. La solicitud #{id} se encuentra en estado '{solicitud.Estado}'.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_REGLA_NEGOCIO", msg);
            return BadRequest(new
            {
                requestId,
                status = 400,
                error = "Regla de Negocio 2",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        var aprobador = GetCurrentActor();
        solicitud.Estado = EstadoSolicitud.Aprobada;
        solicitud.AprobadoPor = aprobador;
        solicitud.FechaAprobacion = DateTime.Now;
        // Rotamos el token de concurrencia RowVersion al cambiar el estado
        solicitud.RowVersion = Guid.NewGuid().ToString("N");

        await _db.SaveChangesAsync();

        await _fileLogger.LogOperationAsync(
            requestId,
            "SOLICITUD_APROBADA",
            $"Solicitud #{solicitud.Id} aprobada por '{aprobador}' | Nuevo RowVersion: {solicitud.RowVersion}");

        return Ok(new
        {
            requestId,
            message = $"Solicitud #{solicitud.Id} aprobada correctamente por {aprobador}.",
            data = solicitud
        });
    }

    /// <summary>
    /// RF4: Emitir orden de compra (proveedor y monto) desde una solicitud Aprobada. Cada solicitud tiene como máximo una orden.
    /// Regla 1: El monto debe ser mayor a 0.
    /// Regla 3: Solo una solicitud Aprobada puede generar una orden de compra y solo una vez.
    /// Regla 4: Si dos usuarios intentan emitir la orden de compra de la solicitud al mismo tiempo, solo una operación
    ///          debe tener éxito y la otra debe recibir un mensaje de conflicto (409 Conflict).
    /// </summary>
    [HttpPost("{id:int}/orden-compra")]
    [Authorize(Roles = "Compras,Admin")]
    public async Task<IActionResult> EmitirOrdenCompra(int id, [FromForm] EmitirOrdenDto dto)
    {
        var requestId = GetRequestId();
        var actor = GetCurrentActor();

        // Regla 1: Monto mayor a 0 y proveedor obligatorio
        if (dto.Monto <= 0)
        {
            var msg = "Regla de Negocio 1 violada: El monto de la orden de compra debe ser mayor a 0.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_VALIDACION", msg);
            return BadRequest(new
            {
                requestId,
                status = 400,
                error = "Regla de Negocio 1",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        if (string.IsNullOrWhiteSpace(dto.Proveedor))
        {
            var msg = "El nombre del proveedor es obligatorio para emitir la orden de compra.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_VALIDACION", msg);
            return BadRequest(new
            {
                requestId,
                status = 400,
                error = "Datos incompletos",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        // Paso 1: Ambos usuarios/hilos leen la solicitud en su estado actual (antes de adquirir cambios)
        var solicitud = await _db.SolicitudesCompra
            .Include(s => s.OrdenCompra)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (solicitud == null)
        {
            return NotFound(new
            {
                requestId,
                status = 404,
                error = "No encontrado",
                message = $"La solicitud #{id} no existe.",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        // Si el cliente envió el RowVersion leído al abrir el formulario, lo fijamos como OriginalValue
        // para que EF Core ejecute: UPDATE SolicitudesCompra ... WHERE Id = @p0 AND RowVersion = @originalRowVersion
        if (!string.IsNullOrWhiteSpace(dto.RowVersion))
        {
            // Si ya en la lectura inicial el RowVersion de BD es distinto al del cliente (porque otra petición concurrente ya ganó)
            if (solicitud.RowVersion != dto.RowVersion)
            {
                var conflictMsg = $"Conflicto de concurrencia (Regla 4): La solicitud #{id} ya fue procesada por otro usuario simultáneamente (RowVersion desactualizado).";
                await _fileLogger.LogOperationAsync(requestId, "CONFLICTO_CONCURRENCIA", $"{conflictMsg} | Usuario rechazado: '{actor}'");
                return Conflict(new
                {
                    requestId,
                    status = 409,
                    error = "Conflicto de Concurrencia (Regla 4)",
                    message = conflictMsg,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            _db.Entry(solicitud).Property(s => s.RowVersion).OriginalValue = dto.RowVersion;
        }

        // Regla 3: Solo una solicitud Aprobada puede generar una orden de compra y solo una vez
        if (solicitud.OrdenCompra != null || solicitud.Estado == EstadoSolicitud.OrdenEmitida)
        {
            // Si la solicitud ya tiene orden emitida, distinguimos si fue un intento concurrente o repetido
            var msg = $"Conflicto (Reglas 3 y 4): La solicitud #{id} ya cuenta con una Orden de Compra emitida (#{solicitud.OrdenCompra?.Id}). No se permiten órdenes duplicadas.";
            await _fileLogger.LogOperationAsync(requestId, "CONFLICTO_CONCURRENCIA", $"{msg} | Usuario: '{actor}'");
            return Conflict(new
            {
                requestId,
                status = 409,
                error = "Conflicto - Orden ya emitida (Reglas 3 y 4)",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        if (solicitud.Estado != EstadoSolicitud.Aprobada)
        {
            var msg = $"Regla de Negocio 3 violada: Solo una solicitud en estado 'Aprobada' puede generar una orden de compra. Estado actual: '{solicitud.Estado}'.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_REGLA_NEGOCIO", msg);
            return BadRequest(new
            {
                requestId,
                status = 400,
                error = "Regla de Negocio 3",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        // Pequeña ventana para que si dos peticiones llegan en paralelo sin RowVersion explícito,
        // ambas hayan leído el mismo RowVersion original antes de intentar confirmar en la base de datos.
        await Task.Delay(120);

        string? comprobanteUrl = null;
        if (dto.ComprobanteImagen != null && dto.ComprobanteImagen.Length > 0)
        {
            comprobanteUrl = await _imageService.SaveImageAsync(dto.ComprobanteImagen, "uploads/ordenes");
        }

        await _concurrencyGate.WaitAsync();
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Modificamos la solicitud (rotando su RowVersion y cambiando estado a OrdenEmitida)
                solicitud.Estado = EstadoSolicitud.OrdenEmitida;
                solicitud.RowVersion = Guid.NewGuid().ToString("N");

                var orden = new OrdenCompra
                {
                    SolicitudCompraId = solicitud.Id,
                    Proveedor = dto.Proveedor.Trim(),
                    Monto = dto.Monto,
                    FechaEmision = DateTime.Now,
                    EmitidoPor = actor,
                    ComprobanteImagenUrl = comprobanteUrl
                };

                _db.OrdenesCompra.Add(orden);

                // EF Core verifica en este SaveChangesAsync():
                // 1) WHERE Id = @id AND RowVersion = @OriginalRowVersion (lanza DbUpdateConcurrencyException si otro hilo ya actualizó)
                // 2) Índice UNIQUE en OrdenesCompra.SolicitudCompraId (lanza DbUpdateException si ya existe fila)
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _fileLogger.LogOperationAsync(
                    requestId,
                    "ORDEN_EMITIDA",
                    $"Orden de Compra #{orden.Id} emitida para Solicitud #{solicitud.Id} | Proveedor: '{orden.Proveedor}' | Monto: ${orden.Monto:N2} | Emitida por: '{actor}'");

                return Ok(new
                {
                    requestId,
                    message = $"Orden de compra #{orden.Id} emitida exitosamente para el proveedor '{orden.Proveedor}'.",
                    data = new
                    {
                        orden.Id,
                        orden.SolicitudCompraId,
                        orden.Proveedor,
                        orden.Monto,
                        orden.FechaEmision,
                        orden.EmitidoPor,
                        orden.ComprobanteImagenUrl,
                        nuevoEstadoSolicitud = solicitud.Estado,
                        nuevoRowVersion = solicitud.RowVersion
                    }
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                var conflictMsg = $"Conflicto de concurrencia detectado por EF Core (Regla 4): Otro usuario emitió la orden de compra para la solicitud #{id} al mismo tiempo. Operación cancelada para evitar duplicados.";
                await _fileLogger.LogOperationAsync(requestId, "CONFLICTO_CONCURRENCIA", $"{conflictMsg} | Usuario rechazado: '{actor}'");

                return Conflict(new
                {
                    requestId,
                    status = 409,
                    error = "Conflicto de Concurrencia EF Core (Regla 4)",
                    message = conflictMsg,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (DbUpdateException dbEx) when (
                dbEx.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true ||
                dbEx.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                var conflictMsg = $"Conflicto de unicidad en base de datos (Reglas 3 y 4): Ya existe una orden de compra asociada a la solicitud #{id} creada en otra transacción concurrente.";
                await _fileLogger.LogOperationAsync(requestId, "CONFLICTO_CONCURRENCIA", $"{conflictMsg} | Usuario rechazado: '{actor}'");

                return Conflict(new
                {
                    requestId,
                    status = 409,
                    error = "Conflicto de Concurrencia - Índice Único (Regla 4)",
                    message = conflictMsg,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Endpoint para consultar las líneas registradas en el archivo TXT de logs (Logs/operaciones.txt).
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> ObtenerLogs()
    {
        var lines = await _fileLogger.ReadLogsAsync(200);
        return Ok(new
        {
            archivo = _fileLogger.LogFilePath,
            totalLineas = lines.Length,
            logs = lines
        });
    }

    /// <summary>
    /// Endpoint opcional para disparar un error de prueba y verificar el Middleware #2 (Manejo global de excepciones).
    /// </summary>
    [HttpGet("probar-excepcion")]
    public IActionResult ProbarExcepcionGlobal()
    {
        throw new InvalidOperationException("Excepción de prueba para verificar el Middleware Global de Excepciones con respuesta JSON uniforme y X-Request-Id.");
    }
}
