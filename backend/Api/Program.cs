using System.Text;
using Api.Data;
using Api.Middlewares;
using Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. Base de Datos con EF Core (SQLite con soporte de Concurrencia Optimista e Índice Único)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Autenticación JWT con Roles y Claims
var jwtSection = builder.Configuration.GetSection("Jwt");
var keyBytes = Encoding.UTF8.GetBytes(jwtSection["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
    };
});

builder.Services.AddAuthorization();

// 3. Servicios (JWT, Imágenes y Logs en archivo TXT)
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddSingleton<IFileOperationLogger, FileOperationLogger>();

// 4. CORS para permitir peticiones del Frontend MVC y exposición del encabezado X-Request-Id
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5257", "https://localhost:7243")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders(RequestIdMiddleware.HeaderName);
    });
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Inicializar base de datos y datos semilla (Usuarios con Roles/Claims y Solicitudes de ejemplo)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DbSeeder.Seed(db);
}

// Asegurar carpetas de imágenes estáticas
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "solicitudes"));
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "ordenes"));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Middleware Personalizado #1: Identificador único por solicitud HTTP (X-Request-Id)
app.UseMiddleware<RequestIdMiddleware>();

// Middleware Personalizado #2: Manejo global de excepciones con JSON uniforme + registro en archivo TXT
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseStaticFiles();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
