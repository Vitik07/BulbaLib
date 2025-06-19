using BulbaLib.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration; // Added for IConfiguration

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

builder.Services.AddScoped<MySqlService>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var fileService = serviceProvider.GetRequiredService<FileService>();
    var connectionString = configuration.GetConnectionString("DefaultConnection")!;
    return new MySqlService(connectionString, fileService);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// Добавляем cookie-авторизацию с корректной обработкой редиректов для API
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.Cookie.Name = "BulbaLibAuth";

        // Критично важно: возвращать 401/403 для API, а не редиректить на /login
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});
var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseCors();

app.UseAuthentication(); // <-- Обязательно ДО UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();
app.MapRazorPages();

app.Run();