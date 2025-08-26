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
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<conversation_messages> conversation_messages { get; set; }
        public DbSet<SocialAccount> social_accounts { get; set; }
        public DbSet<DirectContact> direct_contacts { get; set; }
        public DbSet<TutorialVideo> tutorial_videos { get; set; }
        public DbSet<VideoCategory> video_categories { get; set; }
        public DbSet<Referral> Referrals { get; set; }
        public DbSet<Suggestion> Suggestions { get; set; }
        public DbSet<suggestion_replies> suggestion_replies { get; set; }
        public DbSet<GroupRequest> group_requests { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<NotificationAttachment> NotificationAttachments { get; set; }
        public DbSet<UserNotification> user_notifications { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TutorialVideo>()
               .HasOne(v => v.Category)
               .WithMany(c => c.Videos)
               .HasForeignKey(v => v.CategoryId)
               .OnDelete(DeleteBehavior.SetNull);
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

            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.Agent)
                .WithMany(a => a.Conversations)
                .HasForeignKey(c => c.AgentId)
                .OnDelete(DeleteBehavior.SetNull);
        }

    }
}

