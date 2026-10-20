using EFPerformanceDemo.DBContext;
using EFPerformanceDemo.Repositories;
using EFPerformanceDemo.Utility;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Entity.Infrastructure.Interception;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "executable_queries.sql");

DbInterception.Add(new SqlFileLogger(logFilePath));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddScoped(_ => new AppDbContext(connectionString));
builder.Services.AddScoped<IOrderSearchRepository, OrderSearchRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Seed demo data for ClientAccount/Holding/ModelPortfolio
// Seed demo data for ClientAccount/Holding/ModelPortfolio. This will create the
// LocalDB database (if it does not exist) and insert demo rows. Run at startup
// so local dev environment has data available for the demo endpoints.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // top-level await is supported; ensure seed completes before serving requests
    await SeedData.SeedAsync(context);
}
//using (var scope = app.Services.CreateScope())
//{
//    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    // top-level await is supported in this project; ensure seed completes before serving requests
//    await SeedData.SeedAsync(context);
//}

app.Run();
