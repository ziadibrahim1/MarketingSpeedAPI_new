using MarketingSpeedAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace MarketingSpeedAPI.Data

{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }
        public DbSet<CountriesAndCities> CountriesAndCities { get; set; }
        public DbSet<TermsAndConditions> TermsAndConditions { get; set; }

    }
}

