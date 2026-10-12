using System.Diagnostics;
using EFPerformanceDemo.DBContext;
using EFPerformanceDemo.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EFPerformanceDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EFActionController : ControllerBase
    {
        private readonly AppDbContext _context;
        public EFActionController(AppDbContext context) 
        {
            _context = context;
        }

        [HttpPost("seed")]
        public async Task<IActionResult> SeedData()
        {
            if (await _context.Orders.AnyAsync())
                return Ok("Data already seeded.");

            var random = new Random();

            // Seed 1,000 Customers
            var customers = Enumerable.Range(1, 1000).Select(c => new Customer
            {
                FullName = $"Customer {c}",
                RewardPoints = 0
            }).ToList();
            await _context.Customers.AddRangeAsync(customers);

            // Seed 50 Inventories
            var inventories = Enumerable.Range(1, 50).Select(i => new Inventory
            {
                ProductName = $"Product {i}",
                StockQuantity = 10000
            }).ToList();
            await _context.Inventories.AddRangeAsync(inventories);

            await _context.SaveChangesAsync();

            // Seed 5,000 Orders with 4 Items each (20,000 OrderItems total)
            var orders = new List<Order>();
            for (int i = 1; i <= 5000; i++)
            {
                var order = new Order
                {
                    CustomerId = customers[random.Next(customers.Count)].Id,
                    CategoryId = random.Next(1, 5),
                    IsProcessed = false,
                    OrderItems = Enumerable.Range(1, 4).Select(_ => new OrderItem
                    {
                        InventoryId = inventories[random.Next(inventories.Count)].Id,
                        Quantity = random.Next(1, 5),
                        UnitPrice = (decimal)(random.Next(10, 200) + 0.99)
                    }).ToList()
                };
                orders.Add(order);
            }

            await _context.Orders.AddRangeAsync(orders);
            await _context.SaveChangesAsync();

            return Ok("Seeded Customers, Inventories, 5,000 Orders, and 20,000 OrderItems!");
        }

        [HttpPost("apply-discount-ef")]
        public async Task<IActionResult> ApplyDiscountEF([FromQuery] int categoryId, [FromQuery] decimal discountPercent)
        {
            var watch = Stopwatch.StartNew();

            // 1. Fetching thousands of records over network into memory
            var orders = await _context.Orders
                .Include(o => o.OrderItems)
                .Where(o => o.CategoryId == categoryId && !o.IsProcessed)
                .ToListAsync();

            if (!orders.Any())
            {
                return Ok(new { Message = "No unprocessed orders found for this category." });
            }

            // 2. Modifying objects in C# memory
            foreach (var order in orders)
            {
                foreach (var item in order.OrderItems)
                {
                    item.UnitPrice -= item.UnitPrice * (discountPercent / 100m);
                }
                order.IsProcessed = true;
            }

            // 3. EF tracks state and executes thousands of individual UPDATE statements
            await _context.SaveChangesAsync();

            watch.Stop();

            return Ok(new
            {
                Method = "Naive EF Core LINQ",
                OrdersProcessed = orders.Count,
                TimeTakenMs = watch.ElapsedMilliseconds
            });
        }

        [HttpPost("apply-complex-discount-ef")]
        public async Task<IActionResult> ApplyComplexDiscountEF([FromQuery] int categoryId, [FromQuery] decimal discountPercent)
        {
            var watch = Stopwatch.StartNew();

            // 1. Fetch ONLY the order IDs (1 query)
            var orderIds = await _context.Orders
                .Where(o => o.CategoryId == categoryId && !o.IsProcessed)
                .Select(o => o.Id)
                .ToListAsync();

            if (!orderIds.Any())
                return Ok(new { Message = "No unprocessed orders found. Run /api/orders/reset first." });

            // 2. N+1 KILLER: Query the database inside the loop for EVERY SINGLE ORDER!
            // If there are 1,250 orders, this executes 1,250 SEPARATE SQL queries over the network.
            foreach (var id in orderIds)
            {
                var order = await _context.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.OrderItems)
                        .ThenInclude(i => i.Inventory)
                    .FirstAsync(o => o.Id == id);

                decimal orderTotal = 0;

                foreach (var item in order.OrderItems)
                {
                    item.UnitPrice -= item.UnitPrice * (discountPercent / 100m);
                    item.Inventory.StockQuantity -= item.Quantity;
                    orderTotal += item.UnitPrice * item.Quantity;
                }

                order.Customer.RewardPoints += (int)(orderTotal / 10);
                order.IsProcessed = true;

                _context.OrderAuditLogs.Add(new OrderAuditLog
                {
                    OrderId = order.Id,
                    LogMessage = $"Processed discount of {discountPercent}% on Category {categoryId}. Total: ${orderTotal:F2}"
                });
            }

            // 3. Force EF Core to run Change Tracking on all objects & save in bulk
            await _context.SaveChangesAsync();

            watch.Stop();

            return Ok(new
            {
                Method = "Guaranteed Slow N+1 EF Core LINQ",
                OrdersProcessed = orderIds.Count,
                TimeTakenSeconds = watch.Elapsed.TotalSeconds,
                TimeTakenMs = watch.ElapsedMilliseconds
            });
        }

        [HttpPost("apply-discount-sp")]
        public async Task<IActionResult> ApplyDiscountSP([FromQuery] int categoryId, [FromQuery] decimal discountPercent)
        {
            var watch = Stopwatch.StartNew();

            // Executes all updates, inserts, and transaction logic on the SQL Server side in a single request
            int rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC dbo.sp_ApplyCategoryDiscount @CategoryId={categoryId}, @DiscountPercent={discountPercent}"
            );

            watch.Stop();

            return Ok(new
            {
                Method = "Optimized Stored Procedure",
                TimeTakenMs = watch.ElapsedMilliseconds,
                TimeTakenSeconds = watch.Elapsed.TotalSeconds
            });
        }
    }
}
