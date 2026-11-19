using Frontend.Handlers;
using Frontend.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

// Sesión para almacenar el JWT y datos de usuario
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Autenticación por Cookies en el Frontend MVC (alimentada por los Roles y Claims del JWT del Backend)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
    });

builder.Services.AddAuthorization();

// Configuración del HttpClient conectado al Backend API con inyección automática de JWT
builder.Services.AddTransient<JwtAuthorizationMessageHandler>();
builder.Services.AddHttpClient<IApiClientService, ApiClientService>(client =>
{
    var baseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5191";
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<JwtAuthorizationMessageHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
