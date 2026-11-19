using Api.Data;
using Api.Middlewares;
using Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJwtService _jwtService;
    private readonly IFileOperationLogger _fileLogger;

    public AuthController(AppDbContext db, IJwtService jwtService, IFileOperationLogger fileLogger)
    {
        _db = db;
        _jwtService = jwtService;
        _fileLogger = fileLogger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var requestId = HttpContext.Items[RequestIdMiddleware.HeaderName]?.ToString() ?? HttpContext.TraceIdentifier;

        var user = await _db.Usuarios.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await _fileLogger.LogOperationAsync(requestId, "ERROR_AUTH", $"Intento de login fallido para usuario '{request.Username}'");
            return Unauthorized(new
            {
                requestId,
                status = 401,
                error = "Credenciales inválidas",
                message = "Usuario o contraseña incorrectos.",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        var roles = user.Rol == "Admin"
            ? new[] { "Admin", "Solicitante", "Aprobador", "Compras" }
            : new[] { user.Rol };

        var customClaims = new Dictionary<string, string>
        {
            ["Departamento"] = user.Departamento,
            ["NombreCompleto"] = user.NombreCompleto,
            ["RolPrincipal"] = user.Rol
        };

        var token = _jwtService.GenerateToken(
            user.Id.ToString(),
            user.Username,
            user.Email,
            roles,
            customClaims);

        await _fileLogger.LogOperationAsync(
            requestId,
            "AUTH_LOGIN",
            $"Inicio de sesión exitoso: Usuario='{user.Username}', Nombre='{user.NombreCompleto}', Rol='{user.Rol}', Departamento='{user.Departamento}'");

        return Ok(new
        {
            requestId,
            token,
            username = user.Username,
            nombreCompleto = user.NombreCompleto,
            email = user.Email,
            departamento = user.Departamento,
            rol = user.Rol,
            roles
        });
    }
}
