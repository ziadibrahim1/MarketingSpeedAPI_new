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
        public DbSet<Package> Packages { get; set; }
        public DbSet<PackageFeature> PackageFeatures { get; set; }
        public DbSet<PackageLog> PackageLogs { get; set; }
        public DbSet<PackageCategory> PackageCategories { get; set; }
        public DbSet<UserSubscription> UserSubscriptions { get; set; }
        public DbSet<LeftGroup> LeftGroups { get; set; }
        public DbSet<SubscriptionUsage> subscription_usage { get; set; }
        public DbSet<UserAccount> user_accounts { get; set; }
        public DbSet<Message> Messages { get; set; } = null!;
        public DbSet<MessageLog> message_logs { get; set; } = null!;
        public DbSet<UserImage> UserImages { get; set; }
        public DbSet<MessageAttachment> MessageAttachments { get; set; } = null!;
        public DbSet<MarketingMessage> marketing_messages { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<OurGroupsCountry> categories { get; set; }
        public DbSet<OurGroupsCategory> countries { get; set; }
        public DbSet<CompanyGroup> company_groups { get; set; }
        public DbSet<BlockedChat> blocked_chats { get; set; }
        public DbSet<UserJoinedGroup> user_joined_groups { get; set; }
        public DbSet<BlockedGroup> BlockedGroups { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TutorialVideo>()
               .HasOne(v => v.Category)
               .WithMany(c => c.Videos)
               .HasForeignKey(v => v.CategoryId)
               .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Country>()
                .HasMany(c => c.Cities)
                .WithOne()
                .HasForeignKey(c => c.CountryId);

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

            modelBuilder.Entity<Package>()
                .HasMany(p => p.Features)
                .WithOne(f => f.Package)
                .HasForeignKey(f => f.PackageId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Package>()
                .HasMany(p => p.Logs)
                .WithOne(l => l.Package)
                .HasForeignKey(l => l.PackageId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Package>()
               .HasOne(p => p.Category)
               .WithMany(c => c.Packages)
               .HasForeignKey(p => p.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MessageAttachment>()
            .HasOne(ma => ma.Message)
            .WithMany()
            .HasForeignKey(ma => ma.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MessageAttachment>()
                .HasOne(ma => ma.UserImage)
                .WithMany()
                .HasForeignKey(ma => ma.ImageId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Category>()
            .HasMany(c => c.MarketingMessages)
            .WithOne(m => m.Category)
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CompanyGroup>()
            .HasOne(cg => cg.OurGroupsCountry)
            .WithMany(c => c.CompanyGroups)
            .HasForeignKey(cg => cg.CountryId);

            modelBuilder.Entity<CompanyGroup>()
                .HasOne(cg => cg.OurGroupsCategory)
                .WithMany(c => c.CompanyGroups)
                .HasForeignKey(cg => cg.CategoryId);

            modelBuilder.Entity<UserJoinedGroup>(entity =>
            {
                entity.ToTable("user_joined_groups");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.user_id).IsRequired();
                entity.Property(e => e.group_invite_code).IsRequired().HasMaxLength(255);
                entity.Property(e => e.group_name).HasMaxLength(255);
                entity.Property(e => e.joined_at).IsRequired();
                entity.Property(e => e.is_active).IsRequired().HasDefaultValue(true);
            });
        }

    }
}

