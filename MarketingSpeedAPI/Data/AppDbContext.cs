using MarketingSpeedAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace MarketingSpeedAPI.Data

{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }
        public DbSet<Country> Countries { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<TermsAndConditions> TermsAndConditions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // العلاقة بين Country و Cities
            modelBuilder.Entity<Country>()
                .HasMany(c => c.Cities)
                .WithOne()
                .HasForeignKey(c => c.CountryId);
        }

    }
}

