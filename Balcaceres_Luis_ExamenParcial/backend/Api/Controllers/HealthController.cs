using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Online",
            Service = "Grupo Empresarial Nova - Web API (Balcaceres_Luis_ExamenParcial)",
            Features = new[] { "JWT", "Roles", "Claims", "Imágenes", "Concurrencia EF Core", "Logs Middleware" },
            Timestamp = DateTime.UtcNow
        });
    }
}
