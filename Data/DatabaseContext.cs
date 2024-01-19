using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Server.Data;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options)
        : base(options)
    {
    }
    
    // Override SaveChanges function and call OnBeforeSaving (Chestnut, 2019)
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        OnBeforeSaving();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    // Override SaveChangesAsync function and call OnBeforeSaving (Chestnut, 2019)
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, 
        CancellationToken cancellationToken = default(CancellationToken)
    )
    {
        OnBeforeSaving();
        return (await base.SaveChangesAsync(acceptAllChangesOnSuccess,
            cancellationToken));
    }

    // Before saving changes to the Database Update or Set Modified / Created time (Chestnut, 2019)
    private void OnBeforeSaving()
    {
        IEnumerable<EntityEntry> entries = ChangeTracker.Entries();
        DateTime utcNow = DateTime.UtcNow;

        foreach (EntityEntry entry in entries)
        {
            // for entities that inherit from BaseEntity,
            // set UpdatedOn / CreatedOn appropriately
            if (entry.Entity is Database.Base trackable)
            {
                switch (entry.State)
                {
                    case EntityState.Modified:
                        // set the updated date to "now"
                        trackable.Modified = utcNow;

                        // mark property as "don't touch"
                        // we don't want to update on a Modify operation
                        entry.Property("Created").IsModified = false;
                        break;

                    case EntityState.Added:
                        // set both updated and created date to "now"
                        trackable.Created = utcNow;
                        trackable.Modified = utcNow;
                        break;
                }
            }
        }
    }
    
    public IEnumerable<Database.Event> Event => Set<Database.Event>();
    
    public DbSet<Database.Organization> Organization => Set<Database.Organization>();

    public IEnumerable<Database.OrganizationSettings> OrganizationSettings =>
        Set<Database.OrganizationSettings>();

    public DbSet<Database.User> User => Set<Database.User>();
    public IEnumerable<Database.UserSettings> UserSettings =>
        Set<Database.UserSettings>();
    public IEnumerable<Database.Role> Role => Set<Database.Role>();
    public DbSet<Database.Case> Case => Set<Database.Case>(); // For some reason this has to  be a DbSet
    public DbSet<Database.CaseUser> CaseUser => Set<Database.CaseUser>();
    public IEnumerable<Database.ContemporaneousNote> ContemporaneousNote => Set<Database.ContemporaneousNote>();
    public IEnumerable<Database.Tab> Tab => Set<Database.Tab>();
    public DbSet<Database.SharedTab> SharedTab => Set<Database.SharedTab>();
    public IEnumerable<Database.Exhibit> Exhibit => Set<Database.Exhibit>();
    public IEnumerable<Database.Hash> Hash => Set<Database.Hash>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        
        // Organization Configuration Relationship (one to one)
        modelBuilder.Entity<Database.Organization>()
            .HasOne(e => e.Settings)
            .WithOne(e => e.Organization)
            .HasForeignKey<Database.OrganizationSettings>("OrganizationId");
        
        // User settings relationship (one to one) 
        modelBuilder.Entity<Database.User>()
            .HasOne(e => e.Settings)
            .WithOne(e => e.User)
            .HasForeignKey<Database.UserSettings>("UserId");
        
        modelBuilder
            .Entity<Database.Event>()
            .Property(e => e.Created)
            .HasDefaultValueSql("now()");
        
        modelBuilder
            .Entity<Database.Event>()
            .Property(e => e.Updated)
            .HasDefaultValueSql("now()");
        
      
    }
}