using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RaceDay.API.Data;

namespace RaceDay.Tests.Infrastructure;

/// <summary>
/// Creates a test version of the RaceDay API using the real ASP.NET Core
/// application pipeline while replacing the production database with
/// an isolated in-memory database.
/// </summary>
public class RaceDayWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Remove the application's normal database registration.
            services.RemoveAll<IDbContextOptionsConfiguration<RaceDayDbContext>>();
            services.RemoveAll<DbContextOptions<RaceDayDbContext>>();
            services.RemoveAll<RaceDayDbContext>();

            // Use a unique in-memory database for each test host.
            var databaseName = $"RaceDayTest_{Guid.NewGuid()}";
            services.AddDbContext<RaceDayDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
            });
        });
    }
}