using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<SolicitudCompra> SolicitudesCompra => Set<SolicitudCompra>();
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<AuditoriaCambio> AuditoriasCambios => Set<AuditoriaCambio>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<SolicitudCompra>(entity =>
        {
            entity.Property(s => s.MontoEstimado).HasColumnType("decimal(18,2)");

            // Mecanismo de Control de Concurrencia #1: Token de concurrencia optimista (RowVersion) en EF Core
            entity.Property(s => s.RowVersion)
                  .IsConcurrencyToken()
                  .IsRequired();

            entity.HasOne(s => s.OrdenCompra)
                  .WithOne(o => o.SolicitudCompra)
                  .HasForeignKey<OrdenCompra>(o => o.SolicitudCompraId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(s => s.HistorialAuditoria)
                  .WithOne(a => a.SolicitudCompra)
                  .HasForeignKey(a => a.SolicitudCompraId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrdenCompra>(entity =>
        {
            entity.Property(o => o.Monto).HasColumnType("decimal(18,2)");

            // Mecanismo de Control de Concurrencia #2: Índice único sobre SolicitudCompraId (máximo 1 orden por solicitud)
            entity.HasIndex(o => o.SolicitudCompraId).IsUnique();
        });
    }
}

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        // Si la tabla existente tiene el esquema anterior sin AuditoriasCambios o CreadoPorUsername, recreamos limpiamente
        try
        {
            _ = db.AuditoriasCambios.Any();
            _ = db.SolicitudesCompra.Select(s => s.CreadoPorUsername).FirstOrDefault();
        }
        catch
        {
            db.Database.EnsureDeleted();
        }

        db.Database.EnsureCreated();

        if (!db.Usuarios.Any())
        {
            // Solo 2 roles en el sistema: "Admin" y "Solicitante"
            db.Usuarios.AddRange(
                new Usuario
                {
                    Username = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "Luis Felipe Balcaceres Silvestre",
                    Email = "lbalcaceres@techsupplies.com",
                    Departamento = "Dirección General",
                    Rol = RolesSistema.Admin
                },
                new Usuario
                {
                    Username = "admin2",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "Ana López (Admin Aprobadora)",
                    Email = "alopez@techsupplies.com",
                    Departamento = "Gerencia y Compras",
                    Rol = RolesSistema.Admin
                },
                new Usuario
                {
                    Username = "solicitante1",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "Carlos Méndez",
                    Email = "cmendez@techsupplies.com",
                    Departamento = "Infraestructura TI",
                    Rol = RolesSistema.Solicitante
                },
                new Usuario
                {
                    Username = "solicitante2",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "María Fernanda Ruiz",
                    Email = "mruiz@techsupplies.com",
                    Departamento = "Redes y Telecomunicaciones",
                    Rol = RolesSistema.Solicitante
                }
            );
            db.SaveChanges();
        }

        if (!db.SolicitudesCompra.Any())
        {
            var sol1 = new SolicitudCompra
            {
                Departamento = "Infraestructura TI",
                Descripcion = "5 Laptops Dell Latitude 5540 Core i7 32GB RAM",
                Justificacion = "Renovación de equipos para nuevos desarrolladores del área de plataforma.",
                MontoEstimado = 6250.00m,
                Estado = EstadoSolicitud.Pendiente,
                FechaCreacion = DateTime.Now.AddHours(-4),
                CreadoPorUsername = "solicitante1",
                CreadoPor = "Carlos Méndez [solicitante1] (Infraestructura TI)",
                RowVersion = Guid.NewGuid().ToString("N")
            };

            var sol2 = new SolicitudCompra
            {
                Departamento = "Redes y Telecomunicaciones",
                Descripcion = "2 Switches Administrables Cisco Catalyst 24 Puertos PoE",
                Justificacion = "Ampliación de nodos de red en piso 3 de oficinas administrativas.",
                MontoEstimado = 1850.00m,
                Estado = EstadoSolicitud.Aprobada,
                FechaCreacion = DateTime.Now.AddHours(-3),
                CreadoPorUsername = "solicitante2",
                CreadoPor = "María Fernanda Ruiz [solicitante2] (Redes y Telecomunicaciones)",
                AprobadoPor = "Luis Felipe Balcaceres Silvestre [admin] (Dirección General)",
                FechaAprobacion = DateTime.Now.AddHours(-1),
                RowVersion = Guid.NewGuid().ToString("N")
            };

            db.SolicitudesCompra.AddRange(sol1, sol2);
            db.SaveChanges();

            db.AuditoriasCambios.AddRange(
                new AuditoriaCambio
                {
                    SolicitudCompraId = sol1.Id,
                    Accion = "SOLICITUD_CREADA",
                    EstadoAnterior = "-",
                    EstadoNuevo = EstadoSolicitud.Pendiente,
                    UsuarioUsername = "solicitante1",
                    UsuarioNombre = "Carlos Méndez",
                    UsuarioRol = RolesSistema.Solicitante,
                    Detalle = "Solicitud registrada por Carlos Méndez (Monto: $6,250.00)",
                    RequestId = "SEED-INIT-001",
                    FechaHora = sol1.FechaCreacion
                },
                new AuditoriaCambio
                {
                    SolicitudCompraId = sol2.Id,
                    Accion = "SOLICITUD_CREADA",
                    EstadoAnterior = "-",
                    EstadoNuevo = EstadoSolicitud.Pendiente,
                    UsuarioUsername = "solicitante2",
                    UsuarioNombre = "María Fernanda Ruiz",
                    UsuarioRol = RolesSistema.Solicitante,
                    Detalle = "Solicitud registrada por María Fernanda Ruiz (Monto: $1,850.00)",
                    RequestId = "SEED-INIT-002",
                    FechaHora = sol2.FechaCreacion
                },
                new AuditoriaCambio
                {
                    SolicitudCompraId = sol2.Id,
                    Accion = "SOLICITUD_APROBADA",
                    EstadoAnterior = EstadoSolicitud.Pendiente,
                    EstadoNuevo = EstadoSolicitud.Aprobada,
                    UsuarioUsername = "admin",
                    UsuarioNombre = "Luis Felipe Balcaceres Silvestre",
                    UsuarioRol = RolesSistema.Admin,
                    Detalle = "Solicitud aprobada por el administrador Luis Felipe Balcaceres Silvestre [admin]",
                    RequestId = "SEED-INIT-003",
                    FechaHora = sol2.FechaAprobacion!.Value
                }
            );
            db.SaveChanges();
        }
    }
}
