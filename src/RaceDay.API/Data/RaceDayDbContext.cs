using Microsoft.EntityFrameworkCore;
using RaceDay.API.Models;

namespace RaceDay.API.Data;

/// <summary>
/// The EF Core context is the bridge between the C# model classes and the SQLite database.
/// Each DbSet represents one table that EF Core can query, track, and persist.
/// </summary>
public class RaceDayDbContext : DbContext
{
    public RaceDayDbContext(DbContextOptions<RaceDayDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<RaceDay.API.Models.Route> Routes => Set<RaceDay.API.Models.Route>();

    public DbSet<Entry> Entries => Set<Entry>();

    public DbSet<Result> Results => Set<Result>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // A user can organise many events, but each event belongs to one organiser.
        modelBuilder.Entity<Event>()
            .HasOne(e => e.Organizer)
            .WithMany(u => u.Events)
            .HasForeignKey(e => e.OrganizerID)
            .OnDelete(DeleteBehavior.Restrict);

        // A user can submit many entries, while each entry belongs to one participant.
        modelBuilder.Entity<Entry>()
            .HasOne(e => e.User)
            .WithMany(u => u.Entries)
            .HasForeignKey(e => e.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        // One event contains many categories, and each category belongs to a single event.
        modelBuilder.Entity<Category>()
            .HasOne(c => c.Event)
            .WithMany(e => e.Categories)
            .HasForeignKey(c => c.EventID)
            .OnDelete(DeleteBehavior.Restrict);

        // The ERD allows an event to have zero or one route, so the relationship is optional.
        modelBuilder.Entity<Event>()
            .HasOne(e => e.Route)
            .WithOne(r => r.Event)
            .HasForeignKey<RaceDay.API.Models.Route>(r => r.EventID)
            .OnDelete(DeleteBehavior.Restrict);

        // A category can have many entries, but each entry belongs to one category.
        modelBuilder.Entity<Entry>()
            .HasOne(e => e.Category)
            .WithMany(c => c.Entries)
            .HasForeignKey(e => e.CategoryID)
            .OnDelete(DeleteBehavior.Restrict);

        // An entry may have zero or one result, but each result belongs to one entry.
        modelBuilder.Entity<Entry>()
            .HasOne(e => e.Result)
            .WithOne(r => r.Entry)
            .HasForeignKey<Result>(r => r.EntryID)
            .OnDelete(DeleteBehavior.Restrict);

        // Preserve the approved RaceDay uniqueness rules for participant registration and result tracking.
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Entry>()
            .HasIndex(e => new { e.UserID, e.CategoryID })
            .IsUnique();

        // Store enum values as readable text instead of numeric values.
        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasConversion<string>();

        modelBuilder.Entity<Event>()
            .Property(e => e.EventType)
            .HasConversion<string>();

        modelBuilder.Entity<Event>()
            .Property(e => e.Status)
            .HasConversion<string>()
            .HasDefaultValue(EventStatus.Active);

        modelBuilder.Entity<User>()
            .Property(u => u.FirstName)
            .HasMaxLength(50);

        modelBuilder.Entity<User>()
            .Property(u => u.LastName)
            .HasMaxLength(50);

        modelBuilder.Entity<User>()
            .Property(u => u.Email)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<User>()
            .Property(u => u.PasswordHash)
            .HasMaxLength(255)
            .IsRequired();

        modelBuilder.Entity<Event>()
            .Property(e => e.EventName)
            .HasMaxLength(150)
            .IsRequired();

        modelBuilder.Entity<Event>()
            .Property(e => e.Description)
            .HasMaxLength(500)
            .IsRequired();

        modelBuilder.Entity<Event>()
            .Property(e => e.Location)
            .HasMaxLength(150)
            .IsRequired();

        modelBuilder.Entity<Category>()
            .Property(c => c.CategoryName)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<RaceDay.API.Models.Route>()
            .Property(r => r.RouteName)
            .HasMaxLength(150)
            .IsRequired();

        modelBuilder.Entity<RaceDay.API.Models.Route>()
            .Property(r => r.RouteDescription)
            .HasMaxLength(500);

        modelBuilder.Entity<RaceDay.API.Models.Route>()
            .Property(r => r.MapURL)
            .HasMaxLength(500);

        modelBuilder.Entity<Entry>()
            .Property(e => e.EntryDate)
            .IsRequired();

        modelBuilder.Entity<Result>()
            .Property(r => r.FinishTime)
            .IsRequired();

        modelBuilder.Entity<Result>()
            .Property(r => r.ResultStatus)
            .HasConversion<string>();

        modelBuilder.Entity<Event>()
            .Property(e => e.DistanceKM)
            .HasPrecision(6, 2);

        modelBuilder.Entity<Category>()
            .Property(c => c.DistanceKM)
            .HasPrecision(6, 2);

        modelBuilder.Entity<RaceDay.API.Models.Route>()
            .Property(r => r.DistanceKM)
            .HasPrecision(6, 2);

        modelBuilder.Entity<Category>()
            .Property(c => c.EntryFee)
            .HasPrecision(10, 2);
    }
}
