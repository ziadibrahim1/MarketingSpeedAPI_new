using MarketingSpeedAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Data

{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }
        public DbSet<Country> Countries { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<TermsAndConditions> TermsAndConditions { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<conversation_messages> conversation_messages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // العلاقة بين Country و Cities
            modelBuilder.Entity<Country>()
                .HasMany(c => c.Cities)
                .WithOne()
                .HasForeignKey(c => c.CountryId);
            // Conversation
            modelBuilder.Entity<Conversation>()
                .HasMany(c => c.conversation_messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId);

            base.OnModelCreating(modelBuilder);
        }

    }
}

