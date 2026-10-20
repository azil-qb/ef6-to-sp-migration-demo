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
        // Added domain sets for account drift reporting. If these entities already
        // exist in the project they will be used; otherwise the new model classes
        // under EFPerformanceDemo.Models provide the necessary shape.
        public DbSet<ClientAccount> ClientAccounts { get; set; } = null!;
        public DbSet<Holding> Holdings { get; set; } = null!;
        public DbSet<ModelPortfolio> ModelPortfolios { get; set; } = null!;
    }
}
