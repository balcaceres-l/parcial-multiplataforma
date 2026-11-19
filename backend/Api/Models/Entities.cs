using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Api.Models;

public static class EstadoSolicitud
{
    public const string Pendiente = "Pendiente";
    public const string Aprobada = "Aprobada";
    public const string OrdenEmitida = "OrdenEmitida";
}

public static class RolesSistema
{
    public const string Admin = "Admin";
    public const string Solicitante = "Solicitante";
}

public class Usuario
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    /// <summary>
    /// Solo existen 2 roles en el sistema: "Solicitante" y "Admin".
    /// </summary>
    public string Rol { get; set; } = RolesSistema.Solicitante;
}

public class SolicitudCompra
{
    public int Id { get; set; }

    [Required]
    public string Departamento { get; set; } = string.Empty;

    [Required]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    public string Justificacion { get; set; } = string.Empty;

    public decimal MontoEstimado { get; set; }

    public string Estado { get; set; } = EstadoSolicitud.Pendiente;

    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    /// <summary>
    /// Username del usuario que creó la solicitud (para filtrar que el Solicitante solo vea sus solicitudes y Admin vea todas).
    /// </summary>
    public string CreadoPorUsername { get; set; } = string.Empty;

    public string CreadoPor { get; set; } = string.Empty;

    /// <summary>
    /// Usuario (Admin) que aprobó la solicitud (RF3).
    /// </summary>
    public string? AprobadoPor { get; set; }

    public DateTime? FechaAprobacion { get; set; }

    public string? ImagenUrl { get; set; }

    /// <summary>
    /// Token de concurrencia optimista de EF Core ([ConcurrencyCheck] / IsConcurrencyToken).
    /// </summary>
    [ConcurrencyCheck]
    public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

    public OrdenCompra? OrdenCompra { get; set; }

    public List<AuditoriaCambio> HistorialAuditoria { get; set; } = [];
}

public class OrdenCompra
{
    public int Id { get; set; }

    public int SolicitudCompraId { get; set; }

    [JsonIgnore]
    public SolicitudCompra? SolicitudCompra { get; set; }

    [Required]
    public string Proveedor { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    public DateTime FechaEmision { get; set; } = DateTime.Now;

    public string EmitidoPor { get; set; } = string.Empty;

    public string? ComprobanteImagenUrl { get; set; }
}

/// <summary>
/// Registro de auditoría en base de datos para que el Admin pueda auditar todos los cambios
/// (quién creó, quién aprobó, quién emitió la orden o si hubo intentos con conflicto).
/// </summary>
public class AuditoriaCambio
{
    public int Id { get; set; }
    public int SolicitudCompraId { get; set; }

    [JsonIgnore]
    public SolicitudCompra? SolicitudCompra { get; set; }

    public string Accion { get; set; } = string.Empty; // SOLICITUD_CREADA, SOLICITUD_APROBADA, ORDEN_EMITIDA, CONFLICTO_CONCURRENCIA
    public string EstadoAnterior { get; set; } = string.Empty;
    public string EstadoNuevo { get; set; } = string.Empty;
    public string UsuarioUsername { get; set; } = string.Empty;
    public string UsuarioNombre { get; set; } = string.Empty;
    public string UsuarioRol { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public DateTime FechaHora { get; set; } = DateTime.Now;
}
