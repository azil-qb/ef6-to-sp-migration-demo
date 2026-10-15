using EFPerformanceDemo.DBContext;
using EFPerformanceDemo.Repositories;
using EFPerformanceDemo.Utility;
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

app.Run();
