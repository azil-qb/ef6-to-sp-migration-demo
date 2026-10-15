using System.Collections.Generic;
using System.Data.Entity;
using EFPerformanceDemo.Models;

namespace EFPerformanceDemo.DBContext
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(string connectionString) : base(connectionString) { }

        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<Inventory> Inventories { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;
        public DbSet<OrderAuditLog> OrderAuditLogs { get; set; } = null!;
    }
}
