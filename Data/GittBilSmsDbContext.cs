using GittBilSmsCore.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace GittBilSmsCore.Data
{
    public class GittBilSmsDbContext : DbContext
    {
        public GittBilSmsDbContext(DbContextOptions<GittBilSmsDbContext> options) : base(options) { }

        public DbSet<Company> Companies { get; set; }
        public DbSet<CompanyUser> Users { get; set; }
        public DbSet<Api> Apis { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Directory> Directories { get; set; }
        public DbSet<DirectoryNumber> DirectoryNumbers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Company>()
                .HasMany(c => c.Users)
                .WithOne(u => u.Company)
                .HasForeignKey(u => u.CompanyId);
        }
    }
   
}
