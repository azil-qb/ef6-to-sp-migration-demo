using EFPerformanceDemo.DBContext;
using EFPerformanceDemo.Utility;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "executable_queries.sql");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
        .EnableSensitiveDataLogging() // Ensures parameters show actual values rather than '?'
        .UseLoggerFactory(LoggerFactory.Create(b =>
            b.AddProvider(new SqlFileLoggerProvider(logFilePath))
             .AddFilter((category, level) =>
                 category == DbLoggerCategory.Database.Command.Name && level == LogLevel.Information))));


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
