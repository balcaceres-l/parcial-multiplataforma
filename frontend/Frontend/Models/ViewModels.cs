using System.ComponentModel.DataAnnotations;

namespace Frontend.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "El usuario es obligatorio")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria")]
    public string Password { get; set; } = string.Empty;
}

public class LoginApiResponse
{
    public string RequestId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];
}

public class SolicitudCompraVm
{
    public int Id { get; set; }
    public string Departamento { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Justificacion { get; set; } = string.Empty;
    public decimal MontoEstimado { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public DateTime FechaCreacion { get; set; }
    public string CreadoPor { get; set; } = string.Empty;
    public string? AprobadoPor { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public string? ImagenUrl { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public OrdenCompraVm? OrdenCompra { get; set; }
}

public class OrdenCompraVm
{
    public int Id { get; set; }
    public int SolicitudCompraId { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateTime FechaEmision { get; set; }
    public string EmitidoPor { get; set; } = string.Empty;
    public string? ComprobanteImagenUrl { get; set; }
}

public class CrearSolicitudVm
{
    [Required(ErrorMessage = "El departamento es obligatorio")]
    public string Departamento { get; set; } = string.Empty;

    [Required(ErrorMessage = "La descripción es obligatoria")]
    public string Descripcion { get; set; } = string.Empty;

    [Required(ErrorMessage = "La justificación es obligatoria (Regla de Negocio 1)")]
    public string Justificacion { get; set; } = string.Empty;

    [Required(ErrorMessage = "El monto estimado es obligatorio")]
    public decimal MontoEstimado { get; set; }

    public IFormFile? Imagen { get; set; }
}

public class EmitirOrdenVm
{
    public int SolicitudId { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string? RowVersion { get; set; }
    public IFormFile? ComprobanteImagen { get; set; }
}

public class ApiResultVm
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ErrorTitle { get; set; }
}

public class LogsApiResponse
{
    public string Archivo { get; set; } = string.Empty;
    public int TotalLineas { get; set; }
    public string[] Logs { get; set; } = [];
}
