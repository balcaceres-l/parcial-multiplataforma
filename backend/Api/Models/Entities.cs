using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Api.Models;

public static class EstadoSolicitud
{
    public const string Pendiente = "Pendiente";
    public const string Aprobada = "Aprobada";
    public const string OrdenEmitida = "OrdenEmitida";
}

public class Usuario
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
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

    public string CreadoPor { get; set; } = string.Empty;

    public string? AprobadoPor { get; set; }

    public DateTime? FechaAprobacion { get; set; }

    public string? ImagenUrl { get; set; }

    /// <summary>
    /// Token de concurrencia optimista de EF Core ([ConcurrencyCheck] / IsConcurrencyToken).
    /// Se actualiza en cada cambio de estado para garantizar que dos emisiones de orden simultáneas
    /// detecten colisión (DbUpdateConcurrencyException) y devuelvan 409 Conflict controlado.
    /// </summary>
    [ConcurrencyCheck]
    public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

    public OrdenCompra? OrdenCompra { get; set; }
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
