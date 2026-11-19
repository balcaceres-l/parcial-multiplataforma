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
        db.Database.EnsureCreated();

        if (!db.Usuarios.Any())
        {
            db.Usuarios.AddRange(
                new Usuario
                {
                    Username = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "Luis Felipe Balcaceres Silvestre",
                    Email = "lbalcaceres@techsupplies.com",
                    Departamento = "Dirección General",
                    Rol = "Admin"
                },
                new Usuario
                {
                    Username = "solicitante1",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "Carlos Méndez",
                    Email = "cmendez@techsupplies.com",
                    Departamento = "Infraestructura TI",
                    Rol = "Solicitante"
                },
                new Usuario
                {
                    Username = "aprobador1",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "Ana López",
                    Email = "alopez@techsupplies.com",
                    Departamento = "Gerencia de Operaciones",
                    Rol = "Aprobador"
                },
                new Usuario
                {
                    Username = "compras1",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
                    NombreCompleto = "Roberto Gómez",
                    Email = "rgomez@techsupplies.com",
                    Departamento = "Adquisiciones y Compras",
                    Rol = "Compras"
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
                FechaCreacion = DateTime.Now.AddHours(-5),
                CreadoPor = "Carlos Méndez (Infraestructura TI)",
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
                CreadoPor = "Carlos Méndez (Infraestructura TI)",
                AprobadoPor = "Ana López (Gerencia de Operaciones)",
                FechaAprobacion = DateTime.Now.AddHours(-1),
                RowVersion = Guid.NewGuid().ToString("N")
            };

            db.SolicitudesCompra.AddRange(sol1, sol2);
            db.SaveChanges();
        }
    }
}
