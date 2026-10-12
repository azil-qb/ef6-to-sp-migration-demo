using System.Collections.Generic;
using System.Reflection.Emit;
using EFPerformanceDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace EFPerformanceDemo.DBContext
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Inventory> Inventories => Set<Inventory>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<OrderAuditLog> OrderAuditLogs => Set<OrderAuditLog>();
    }
}
