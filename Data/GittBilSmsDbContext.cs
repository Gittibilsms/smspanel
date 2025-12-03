using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace GittBilSmsCore.Data
{
    public class GittBilSmsDbContext : IdentityDbContext<User, IdentityRole<int>, int>
    {
        public GittBilSmsDbContext(DbContextOptions<GittBilSmsDbContext> options) : base(options) { }

        public DbSet<Company> Companies { get; set; }
      //  public DbSet<User> Users { get; set; } // Unified admin + company users
        public DbSet<Api> Apis { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<PhoneDirectory> Directories { get; set; }
        public DbSet<DirectoryNumber> DirectoryNumbers { get; set; }
        public DbSet<Pricing> Pricing { get; set; }
        public DbSet<CreditTransaction> CreditTransactions { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<BlacklistNumber> BlacklistNumbers { get; set; }
        public DbSet<BannedNumber> BannedNumbers { get; set; }
        public DbSet<OrderAction> OrderActions { get; set; }
        public DbSet<BalanceHistory> BalanceHistory { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        //public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<Notifications> Notifications { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<TicketResponse> TicketResponses { get; set; }
        public DbSet<TempUpload> TempUploads { get; set; }
        public DbSet<ApiCallLog> ApiCallLogs { get; set; }

        public DbSet<LoginHistory> LoginHistories { get; set; }

        public DbSet<OrderRecipient> OrderRecipients { get; set; }
        public DbSet<TelegramMessage> TelegramMessages => Set<TelegramMessage>();
        public DbSet<HistoryLog> HistoryLogs => Set<HistoryLog>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Company>()
                .HasMany(c => c.Users)
                .WithOne(u => u.Company)
                .HasForeignKey(u => u.CompanyId);

            modelBuilder.Entity<UserRole>()
                .HasKey(ur => new { ur.UserId, ur.RoleId });

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId);

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId);

        
            modelBuilder.Entity<RolePermission>()
                .HasKey(rp => new { rp.RoleId, rp.Module });

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId);

            //modelBuilder.Entity<RolePermission>()
            //    .HasOne(rp => rp.Permission)
            //    .WithMany(p => p.RolePermissions);

            modelBuilder.Entity<Ticket>()
                 .HasOne(t => t.CreatedByUser)
                 .WithMany()
                 .HasForeignKey(t => t.CreatedByUserId)
                 .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Ticket>()
                .HasOne(t => t.AssignedAdmin)
                .WithMany()
                .HasForeignKey(t => t.AssignedTo)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<TicketResponse>()
                .HasOne(tr => tr.Ticket)
                .WithMany(t => t.TicketResponses)
                .HasForeignKey(tr => tr.TicketId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<TicketResponse>()
                .HasOne(tr => tr.Responder)
                .WithMany()
                .HasForeignKey(tr => tr.ResponderId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<TicketResponse>()
                .HasOne(tr => tr.RespondedByUser)
                .WithMany()
                .HasForeignKey(tr => tr.RespondedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<BlacklistNumber>()
            .HasOne(b => b.Company)
            .WithMany()
            .HasForeignKey(b => b.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

           modelBuilder.Entity<BlacklistNumber>()
            .HasOne(b => b.CreatedByUser)
            .WithMany()
            .HasForeignKey(b => b.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Order>()
            .HasOne(o => o.CreatedByUser)
            .WithMany()
            .HasForeignKey(o => o.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Api>().ToTable("Apis");


            modelBuilder.Entity<PhoneDirectory>().ToTable("Directories");

            modelBuilder.Entity<PhoneDirectory>()
                .HasMany(d => d.DirectoryNumbers)
                .WithOne(n => n.Directory)
                .HasForeignKey(n => n.DirectoryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ApiCallLog>()
              .HasOne(a => a.Order)
              .WithMany(o => o.ApiCalls)
              .HasForeignKey(a => a.OrderId)
              .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<OrderRecipient>()
             .HasOne(r => r.Order)
             .WithMany(o => o.Recipients)
             .HasForeignKey(r => r.OrderId)
             .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TelegramMessage>(e =>
            {
                e.ToTable("TelegramMessage");
                e.HasKey(x => x.Id);
                e.Property(x => x.Direction).HasConversion<byte>();
                e.Property(x => x.Body).IsRequired();
                e.Property(x => x.Status).HasMaxLength(50).IsRequired();
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => new { x.ChatId, x.TelegramMessageId });
                e.HasIndex(x => x.UserId);
                e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            });
             
        }
    }
   
}
