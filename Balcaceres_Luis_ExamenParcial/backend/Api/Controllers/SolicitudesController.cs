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
[Authorize]
public class SolicitudesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IImageService _imageService;
    private readonly IFileOperationLogger _fileLogger;
    private static readonly SemaphoreSlim _concurrencyGate = new(1, 1);

    public SolicitudesController(AppDbContext db, IImageService imageService, IFileOperationLogger fileLogger)
    {
        _db = db;
        _imageService = imageService;
        _fileLogger = fileLogger;
    }

    private string GetRequestId() =>
        HttpContext.Items[RequestIdMiddleware.HeaderName]?.ToString() ?? HttpContext.TraceIdentifier;

    private string GetCurrentUsername() =>
        User.Identity?.Name ?? "anonimo";

    private string GetCurrentNombreCompleto() =>
        User.FindFirst("NombreCompleto")?.Value ?? GetCurrentUsername();

    private string GetCurrentRol() =>
        User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("RolPrincipal")?.Value ?? RolesSistema.Solicitante;

    private bool IsAdmin() =>
        User.IsInRole(RolesSistema.Admin);

    private string GetCurrentActor()
    {
        var username = GetCurrentUsername();
        var nombre = GetCurrentNombreCompleto();
        var depto = User.FindFirst("Departamento")?.Value;
        return string.IsNullOrWhiteSpace(depto)
            ? $"{nombre} [{username}]"
            : $"{nombre} [{username}] ({depto})";
    }

    /// <summary>
    /// RF2: Listar solicitudes con departamento, monto, estado y fecha, además de filtro.
    /// - Si el usuario tiene rol "Solicitante": solo tiene acceso a SUS propias solicitudes.
    /// - Si el usuario tiene rol "Admin": tiene acceso a TODAS las solicitudes de todas las personas.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListarSolicitudes(
        [FromQuery] string? estado = null,
        [FromQuery] string? departamento = null,
        [FromQuery] string? busqueda = null)
    {
        var query = _db.SolicitudesCompra
            .Include(s => s.OrdenCompra)
            .Include(s => s.HistorialAuditoria)
            .AsNoTracking()
            .AsQueryable();

        // Control de acceso por Rol (2 roles: Solicitante ve solo las suyas, Admin ve las de todos)
        if (!IsAdmin())
        {
            var myUsername = GetCurrentUsername();
            query = query.Where(s => s.CreadoPorUsername == myUsername);
        }

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
            .Include(s => s.HistorialAuditoria.OrderByDescending(a => a.FechaHora))
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

        // Un Solicitante solo puede acceder a sus propias solicitudes; el Admin puede ver cualquiera
        if (!IsAdmin() && solicitud.CreadoPorUsername != GetCurrentUsername())
        {
            var msg = $"Acceso denegado: El usuario '{GetCurrentUsername()}' (Rol: Solicitante) solo tiene permiso para ver sus propias solicitudes.";
            await _fileLogger.LogOperationAsync(requestId, "ERROR_ACCESO_ROL", msg);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                requestId,
                status = 403,
                error = "Acceso Restringido a Solicitudes Propias",
                message = msg,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        return Ok(solicitud);
    }

    /// <summary>
    /// RF1: Registrar solicitud de compra (accesible tanto por Solicitante como por Admin).
    /// Estado inicial: Pendiente.
    /// Regla de negocio 1: El monto debe ser mayor a 0 y la justificación es obligatoria.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Solicitante,Admin")]
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
        var username = GetCurrentUsername();
        var nueva = new SolicitudCompra
        {
            Departamento = dto.Departamento.Trim(),
            Descripcion = dto.Descripcion.Trim(),
            Justificacion = dto.Justificacion.Trim(),
            MontoEstimado = dto.MontoEstimado,
            Estado = EstadoSolicitud.Pendiente, // RF1: Estado inicial Pendiente
            FechaCreacion = DateTime.Now,
            CreadoPorUsername = username,
            CreadoPor = actor,
            ImagenUrl = imagenUrl,
            RowVersion = Guid.NewGuid().ToString("N")
        };

        _db.SolicitudesCompra.Add(nueva);
        await _db.SaveChangesAsync();

        // Registrar en la tabla de Auditoría para que el Admin pueda auditar todos los cambios
        _db.AuditoriasCambios.Add(new AuditoriaCambio
        {
            SolicitudCompraId = nueva.Id,
            Accion = "SOLICITUD_CREADA",
            EstadoAnterior = "-",
            EstadoNuevo = EstadoSolicitud.Pendiente,
            UsuarioUsername = username,
            UsuarioNombre = GetCurrentNombreCompleto(),
            UsuarioRol = GetCurrentRol(),
            Detalle = $"Solicitud registrada en departamento '{nueva.Departamento}' por monto estimado de ${nueva.MontoEstimado:N2}",
            RequestId = requestId,
            FechaHora = nueva.FechaCreacion
        });
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
    /// RF3: Aprobar solicitud: Cambiar una solicitud pendiente a aprobada, registrando el usuario que la aprobó.
    /// Regla de negocio 2: Solo una solicitud Pendiente puede aprobarse.
    /// Solo el rol Admin puede aprobar y auditar las solicitudes.
    /// </summary>
    [HttpPut("{id:int}/aprobar")]
    [Authorize(Roles = "Admin")]
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

        var estadoAnterior = solicitud.Estado;
        var aprobador = GetCurrentActor();
        solicitud.Estado = EstadoSolicitud.Aprobada;
        solicitud.AprobadoPor = aprobador;
        solicitud.FechaAprobacion = DateTime.Now;
        solicitud.RowVersion = Guid.NewGuid().ToString("N");

        _db.AuditoriasCambios.Add(new AuditoriaCambio
        {
            SolicitudCompraId = solicitud.Id,
            Accion = "SOLICITUD_APROBADA",
            EstadoAnterior = estadoAnterior,
            EstadoNuevo = EstadoSolicitud.Aprobada,
            UsuarioUsername = GetCurrentUsername(),
            UsuarioNombre = GetCurrentNombreCompleto(),
            UsuarioRol = GetCurrentRol(),
            Detalle = $"Solicitud #{solicitud.Id} aprobada por {aprobador}",
            RequestId = requestId,
            FechaHora = solicitud.FechaAprobacion.Value
        });

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
    [Authorize(Roles = "Admin")]
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

        if (!string.IsNullOrWhiteSpace(dto.RowVersion))
        {
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
                var estadoAnterior = solicitud.Estado;
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

                _db.AuditoriasCambios.Add(new AuditoriaCambio
                {
                    SolicitudCompraId = solicitud.Id,
                    Accion = "ORDEN_EMITIDA",
                    EstadoAnterior = estadoAnterior,
                    EstadoNuevo = EstadoSolicitud.OrdenEmitida,
                    UsuarioUsername = GetCurrentUsername(),
                    UsuarioNombre = GetCurrentNombreCompleto(),
                    UsuarioRol = GetCurrentRol(),
                    Detalle = $"Orden de Compra emitida para proveedor '{orden.Proveedor}' por monto ${orden.Monto:N2}",
                    RequestId = requestId,
                    FechaHora = orden.FechaEmision
                });

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

                await RegistrarAuditoriaConflictoAsync(id, requestId, conflictMsg);

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

                await RegistrarAuditoriaConflictoAsync(id, requestId, conflictMsg);

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

    private async Task RegistrarAuditoriaConflictoAsync(int solicitudId, string requestId, string detalle)
    {
        try
        {
            _db.ChangeTracker.Clear();
            _db.AuditoriasCambios.Add(new AuditoriaCambio
            {
                SolicitudCompraId = solicitudId,
                Accion = "CONFLICTO_CONCURRENCIA",
                EstadoAnterior = EstadoSolicitud.Aprobada,
                EstadoNuevo = EstadoSolicitud.OrdenEmitida + " (Rechazado 409)",
                UsuarioUsername = GetCurrentUsername(),
                UsuarioNombre = GetCurrentNombreCompleto(),
                UsuarioRol = GetCurrentRol(),
                Detalle = detalle,
                RequestId = requestId,
                FechaHora = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        catch
        {
            // No interrumpir respuesta 409 si falla guardado secundario
        }
    }

    /// <summary>
    /// Endpoint exclusivo para Admin: Permite auditar todos los cambios realizados sobre todas las solicitudes
    /// (quién creó, quién aprobó, quién emitió orden y conflictos detectados).
    /// </summary>
    [HttpGet("auditoria")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ObtenerAuditoria()
    {
        var historial = await _db.AuditoriasCambios
            .AsNoTracking()
            .OrderByDescending(a => a.FechaHora)
            .ToListAsync();

        return Ok(historial);
    }

    /// <summary>
    /// Endpoint exclusivo para Admin: Consultar las líneas registradas en el archivo TXT de logs (Logs/operaciones.txt).
    /// </summary>
    [HttpGet("logs")]
    [Authorize(Roles = "Admin")]
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
    /// Endpoint para disparar un error de prueba y verificar el Middleware #2 (Manejo global de excepciones).
    /// </summary>
    [HttpGet("probar-excepcion")]
    [AllowAnonymous]
    public IActionResult ProbarExcepcionGlobal()
    {
        throw new InvalidOperationException("Excepción de prueba para verificar el Middleware Global de Excepciones con respuesta JSON uniforme y X-Request-Id.");
    }
}
