using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RaceDay.API.Data;
using RaceDay.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Register RaceDayDbContext with dependency injection and configure EF Core
// to use the SQLite connection string stored in appsettings.json.
builder.Services.AddDbContext<RaceDayDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("RaceDayDatabase")));

// Register the password hashing service so future registration and login flows
// can hash passwords and verify hashes without storing or logging plain text.
builder.Services.AddScoped<PasswordHashService>();

// Register the JWT service so verified users can receive signed tokens using
// the configured issuer, audience, secret, and expiration period.
builder.Services.AddSingleton<JwtTokenService>();

// Register JWT bearer authentication so future endpoints can validate tokens.
// Token creation is handled by JwtTokenService after successful login.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtConfiguration = builder.Configuration.GetSection("Jwt");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtConfiguration["SecretKey"] ?? string.Empty)),
            ValidateIssuer = true,
            ValidIssuer = jwtConfiguration["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtConfiguration["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

// Add authorization services now so protected endpoints can be introduced later.
// No controllers are protected at this stage.
builder.Services.AddAuthorization();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
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
    app.MapControllers();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Authentication must run before authorization when protected endpoints are added.
app.UseAuthentication();
app.UseAuthorization();

app.Run();
