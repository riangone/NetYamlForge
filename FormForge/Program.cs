using FormForge.Services;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "formforge.db");
builder.Services.AddScoped<SqliteConnection>(_ =>
    new SqliteConnection($"Data Source={dbPath}"));
builder.Services.AddScoped<FormRepository>();
builder.Services.AddScoped<ResponseRepository>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var conn = scope.ServiceProvider.GetRequiredService<SqliteConnection>();
    await DbInitializer.InitAsync(conn);
}

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
