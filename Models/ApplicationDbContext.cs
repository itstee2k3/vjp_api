using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using vjp_api.Models;

namespace vjp_api.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<UserGroup> UserGroups { get; set; }
        public DbSet<GroupChat> GroupChats { get; set; }
        public DbSet<GroupMessage> GroupMessages { get; set; }

        public DbSet<RefreshToken> RefreshTokens { get; set; }

        public DbSet<Friendship> Friendships { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình mối quan hệ giữa ChatMessage và User
            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.Sender)
                .WithMany(u => u.ChatMessagesSent)
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.Receiver)
                .WithMany(u => u.ChatMessagesReceived)
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);
            
            // Đảm bảo một cặp UserRequesterId và UserReceiverId là duy nhất
            modelBuilder.Entity<Friendship>()
                .HasIndex(f => new { f.UserRequesterId, f.UserReceiverId })
                .IsUnique();

            // Cấu hình quan hệ với ApplicationUser
            modelBuilder.Entity<Friendship>()
                .HasOne(f => f.Requester)
                .WithMany() // Hoặc WithMany(u => u.SentFriendRequests) nếu bạn thêm collection vào ApplicationUser
                .HasForeignKey(f => f.UserRequesterId)
                .OnDelete(DeleteBehavior.Restrict); // Hoặc Cascade tùy logic

            modelBuilder.Entity<Friendship>()
                .HasOne(f => f.Receiver)
                .WithMany() // Hoặc WithMany(u => u.ReceivedFriendRequests)
                .HasForeignKey(f => f.UserReceiverId)
                .OnDelete(DeleteBehavior.Restrict); // Hoặc Cascade tùy logic
        }
    }
} 