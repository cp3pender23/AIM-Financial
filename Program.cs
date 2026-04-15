using AIM.Web.Services;
using Dapper;
using Npgsql;
using System.Data;

DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

builder.Services.AddScoped<IDbConnection>(_ =>
    new NpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IVendorService, VendorService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");
else
    app.UseDeveloperExceptionPage();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthorization();  // no-op until FR-13 adds [Authorize]; slot reserved here
app.MapControllers();
app.MapRazorPages();
app.MapFallbackToPage("/Index");

app.Run();
