﻿namespace LighthouseNotesServer.Data;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options)
        : base(options)
    {
    }

    public DbSet<Database.Organization> Organization => Set<Database.Organization>();

    public IEnumerable<Database.OrganizationConfiguration> OrganizationConfiguration =>
        Set<Database.OrganizationConfiguration>();

    public DbSet<Database.User> User => Set<Database.User>();
    public DbSet<Database.Case> Case => Set<Database.Case>();
    public DbSet<Database.CaseUser> CaseUser => Set<Database.CaseUser>();
    public IEnumerable<Database.Tab> Tab => Set<Database.Tab>();
    public DbSet<Database.Role> Role => Set<Database.Role>();
    public IEnumerable<Database.SharedTab> SharedTab => Set<Database.SharedTab>();
    public IEnumerable<Database.Hash> Hash => Set<Database.Hash>();
    public IEnumerable<Database.Exhibit> Exhibit => Set<Database.Exhibit>();
    public IEnumerable<Database.ExhibitUser> ExhibitUser => Set<Database.ExhibitUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        
        // Organization Configuration Relationship
        modelBuilder.Entity<Database.Organization>()
            .HasOne(e => e.Configuration)
            .WithOne(e => e.Organization)
            .HasForeignKey<Database.OrganizationConfiguration>("OrganizationId");
    }
}