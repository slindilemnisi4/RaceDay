using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;

var builder = WebApplication.CreateBuilder(args);

// Register RaceDayDbContext with dependency injection and configure EF Core
// to use the SQLite connection string stored in appsettings.json.
builder.Services.AddDbContext<RaceDayDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("RaceDayDatabase")));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register the Swagger generator and API explorer so the OpenAPI document can
// also be viewed and tested through Swagger UI during local development.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Keep the built-in OpenAPI JSON endpoint and expose the browser-based
    // Swagger UI for manually testing endpoints as they are added later.
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();
