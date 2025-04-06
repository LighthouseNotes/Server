using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Server.Data;

public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
    // Tables
    public DbSet<Database.Event> Event => Set<Database.Event>();
    public DbSet<Database.User> User => Set<Database.User>();
    public IEnumerable<Database.UserSettings> UserSettings => Set<Database.UserSettings>();
    public DbSet<Database.Case> Case => Set<Database.Case>();
    public DbSet<Database.CaseUser> CaseUser => Set<Database.CaseUser>();
    public IEnumerable<Database.ContemporaneousNote> ContemporaneousNote => Set<Database.ContemporaneousNote>();
    public IEnumerable<Database.Tab> Tab => Set<Database.Tab>();
    public DbSet<Database.SharedTab> SharedTab => Set<Database.SharedTab>();
    public IEnumerable<Database.Exhibit> Exhibit => Set<Database.Exhibit>();

    public IEnumerable<Database.Hash> Hash => Set<Database.Hash>();

    // Override SaveChanges function and call OnBeforeSaving (Chestnut, 2019)
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        OnBeforeSaving();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    // Override SaveChangesAsync function and call OnBeforeSaving (Chestnut, 2019)
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        OnBeforeSaving();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Before saving changes to the Database Update or Set Modified / Created time (Chestnut, 2019)
    private void OnBeforeSaving()
    {
        IEnumerable<EntityEntry> entries = ChangeTracker.Entries();
        DateTime utcNow = DateTime.UtcNow;

        foreach (EntityEntry entry in entries)
            // For entities that inherit from BaseEntity, set UpdatedOn / CreatedOn appropriately
        {
            if (entry.Entity is Database.Base trackable)
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            {
                switch (entry.State)
                {
                    case EntityState.Modified:
                        // Set the updated date to "now"
                        trackable.Modified = utcNow;

                        // Mark property as "don't touch" we don't want to update on a Modify operation
                        entry.Property("Created").IsModified = false;
                        break;

                    case EntityState.Added:
                        // Set both updated and created date to "now"
                        trackable.Created = utcNow;
                        trackable.Modified = utcNow;
                        break;
                }
            }
        }
    }

    // Model Creation - Create relationships
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User settings relationship (one to one)
        modelBuilder.Entity<Database.User>()
            .HasOne(e => e.Settings)
            .WithOne(e => e.User)
            .HasForeignKey<Database.UserSettings>("EmailAddress");

        // User events relationship (one to many)
        modelBuilder.Entity<Database.User>()
            .HasMany(u => u.Events)
            .WithOne(e => e.User)
            .HasForeignKey("EmailAddress")
            .IsRequired(false);

        // Event created default value is SQL now()
        modelBuilder
            .Entity<Database.Event>()
            .Property(e => e.Created)
            .HasDefaultValueSql("now()");

        // Event updated default value is SQL now()
        modelBuilder
            .Entity<Database.Event>()
            .Property(e => e.Updated)
            .HasDefaultValueSql("now()");
    }
}