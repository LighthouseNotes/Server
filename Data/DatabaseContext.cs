namespace LighthouseNotesServer.Data;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options)
        : base(options)
    {
    }

    public DbSet<Database.Organization> Organization => Set<Database.Organization>();

    public DbSet<Database.OrganizationConfiguration> OrganizationConfiguration =>
        Set<Database.OrganizationConfiguration>();

    public DbSet<Database.User> User => Set<Database.User>();
    public DbSet<Database.Case> Case => Set<Database.Case>();
    public DbSet<Database.CaseUser> CaseUser => Set<Database.CaseUser>();
    public DbSet<Database.Tab> Tab => Set<Database.Tab>();
    public DbSet<Database.Role> Role => Set<Database.Role>();
    public DbSet<Database.SharedTab> SharedTab => Set<Database.SharedTab>();
    public DbSet<Database.Hash> Hash => Set<Database.Hash>();
    public DbSet<Database.Exhibit> Exhibit => Set<Database.Exhibit>();
    public DbSet<Database.ExhibitUser> ExhibitUser => Set<Database.ExhibitUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Create a ICU Collation
        modelBuilder.HasCollation("NOCASE", "en", "icu", false);

        // Use ICU Collation on Organization Name
        modelBuilder.Entity<Database.Organization>().Property(c => c.Name)
            .UseCollation("NOCASE");

        // Use ICU Collation on Organization Dispaly Name
        modelBuilder.Entity<Database.Organization>().Property(c => c.DisplayName)
            .UseCollation("NOCASE");

        // Organization Configuration Relationship
        modelBuilder.Entity<Database.Organization>()
            .HasOne(e => e.Configuration)
            .WithOne(e => e.Organization)
            .HasForeignKey<Database.OrganizationConfiguration>("OrganizationId");
    }
}