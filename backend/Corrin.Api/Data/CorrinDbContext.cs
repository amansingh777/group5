using System.Text.RegularExpressions;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Data;

public class CorrinDbContext : DbContext
{
    public CorrinDbContext(DbContextOptions<CorrinDbContext> options) : base(options) { }

    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<JobType> JobTypes => Set<JobType>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Engineer> Engineers => Set<Engineer>();
    public DbSet<EngineerSkill> EngineerSkills => Set<EngineerSkill>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobNote> JobNotes => Set<JobNote>();
    public DbSet<JobPart> JobParts => Set<JobPart>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<JobRequest> JobRequests => Set<JobRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<EngineerSkill>().HasKey(x => new { x.EngineerId, x.SkillId });
        b.Entity<Engineer>().HasMany(e => e.Skills).WithOne().HasForeignKey(s => s.EngineerId);
        b.Entity<Engineer>().Property(e => e.Phone).HasMaxLength(20);
        b.Entity<Customer>().Property(c => c.Phone).HasMaxLength(20);
        b.Entity<Customer>().Property(c => c.Postcode).HasMaxLength(10);
        b.Entity<Customer>().Property(c => c.AccountRef).HasMaxLength(12);
        b.Entity<Customer>().HasIndex(c => c.AccountRef).IsUnique();
        b.Entity<Job>().HasIndex(j => j.JobRef).IsUnique();
        b.Entity<Part>().Property(p => p.UnitCost).HasPrecision(8, 2);
        b.Entity<Shift>().Ignore(s => s.RosteredHours);

        foreach (var entity in b.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnake(entity.ClrType.Name) + (entity.ClrType.Name.EndsWith("ch") ? "es" : "s"));
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(ToSnake(prop.Name));
        }
    }

    private static string ToSnake(string name) =>
        Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();
}
