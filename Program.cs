using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BulbaLib.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddSingleton(new SqliteService(
    builder.Configuration.GetConnectionString("DefaultConnection")!
));
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();

app.MapControllers();
app.MapRazorPages();    

app.Run();