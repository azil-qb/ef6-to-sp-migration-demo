using System.Diagnostics;
using System.Data.Entity;
using EFPerformanceDemo.DBContext;
using EFPerformanceDemo.Models;
using EFPerformanceDemo.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace EFPerformanceDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EFActionController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IOrderSearchRepository _orderSearchRepository;
        public EFActionController(AppDbContext context, IOrderSearchRepository orderSearchRepository) 
        {
            _context = context;
            _orderSearchRepository = orderSearchRepository;
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
            _context.Customers.AddRange(customers);

            // Seed 50 Inventories
            var inventories = Enumerable.Range(1, 50).Select(i => new Inventory
            {
                ProductName = $"Product {i}",
                StockQuantity = 10000
            }).ToList();
            _context.Inventories.AddRange(inventories);

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

            _context.Orders.AddRange(orders);
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
                    .Include(o => o.OrderItems.Select(i => i.Inventory))
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

        [HttpPost("apply-complex-discount-sp")]
        public async Task<IActionResult> ApplyComplexDiscountSP([FromQuery] int categoryId, [FromQuery] decimal discountPercent)
        {
            var watch = Stopwatch.StartNew();

            // Cheap, index-friendly count purely for reporting purposes.
            var orderCount = await _context.Orders
                .Where(o => o.CategoryId == categoryId && !o.IsProcessed)
                .CountAsync();

            if (orderCount == 0)
                return Ok(new { Message = "No unprocessed orders found. Run /api/orders/reset first." });

            // Single set-based round-trip. The stored procedure performs the
            // discount, stock decrement, reward-point accrual, audit logging,
            // and processed-flag update entirely in SQL Server, avoiding the
            // N+1 per-order queries and per-row change tracking of the EF version.
            await _context.Database.ExecuteSqlCommandAsync(
                "EXEC dbo.sp_ApplyComplexCategoryDiscount @CategoryId, @DiscountPercent",
                new SqlParameter("@CategoryId", categoryId),
                new SqlParameter("@DiscountPercent", discountPercent));

            watch.Stop();

            return Ok(new
            {
                Method = "Optimized Stored Procedure (set-based, 1 round-trip)",
                OrdersProcessed = orderCount,
                TimeTakenSeconds = watch.Elapsed.TotalSeconds,
                TimeTakenMs = watch.ElapsedMilliseconds
            });
        }

        [HttpGet("search-cartesian-explosion-ef")]
        public async Task<IActionResult> SearchCartesianExplosionEF([FromQuery] string keyword)
        {
            var watch = Stopwatch.StartNew();

            // 1. Cartesian Explosion: Joining multiple collection navigations creates exponential duplicate rows
            // SQL Server sends 200,000+ rows over the network for 5,000 orders!
            var orders = await _context.Orders
                .Include(o => o.Customer)
                .Include(o => o.OrderItems.Select(i => i.Inventory))
                .Where(o => o.OrderItems.Any(i => i.Inventory.ProductName.Contains(keyword)))
                .ToListAsync(); // EF Core spends several seconds materializing & deduplicating entities in C# memory

            // 2. Heavy C# In-Memory Aggregation across child collections
            var result = new List<object>();
            foreach (var order in orders)
            {
                // Force CPU-heavy memory processing per entity
                decimal total = 0;
                foreach (var item in order.OrderItems)
                {
                    total += item.UnitPrice * item.Quantity;
                }

                result.Add(new
                {
                    order.Id,
                    CustomerName = order.Customer.FullName,
                    CalculatedTotal = total,
                    ItemNames = string.Join(", ", order.OrderItems.Select(i => i.Inventory.ProductName))
                });
            }

            watch.Stop();

            return Ok(new
            {
                Method = "Cartesian Explosion EF LINQ",
                ResultCount = result.Count,
                TimeTakenMs = watch.ElapsedMilliseconds,
                TimeTakenSeconds = watch.Elapsed.TotalSeconds
            });
        }

        [HttpGet("search-cartesian-explosion-sp")]
        public async Task<IActionResult> SearchCartesianExplosionSP([FromQuery] string keyword)
        {
            var watch = Stopwatch.StartNew();

            // Data access delegated to the repository: the stored procedure pre-filters
            // to distinct matching OrderIds, then aggregates (SUM/STRING_AGG) in SQL in
            // a single set-based round-trip. No cartesian duplication crosses the network,
            // no EF change tracking, no per-order in-memory aggregation.
            var result = await _orderSearchRepository.SearchOrdersByKeywordAsync(keyword);

            watch.Stop();

            return Ok(new
            {
                Method = "Optimized Stored Procedure (set-based aggregation, 1 round-trip)",
                ResultCount = result.Count,
                TimeTakenMs = watch.ElapsedMilliseconds,
                TimeTakenSeconds = watch.Elapsed.TotalSeconds
            });
        }
    

        [HttpPost("seed-heavy-data")]
        public async Task<IActionResult> SeedHeavyData([FromQuery] int count = 25000)
        {
            var watch = Stopwatch.StartNew();
            var random = new Random();

            // Fetch existing customer and inventory IDs to link against
            var customerIds = await _context.Customers.Select(c => c.Id).ToListAsync();
            var inventoryIds = await _context.Inventories.Select(i => i.Id).ToListAsync();

            if (!customerIds.Any() || !inventoryIds.Any())
            {
                return BadRequest("Please run /api/orders/seed first to create base Customers and Inventories.");
            }

            int batchSize = 2500; // Save in chunks of 2,500 to prevent memory spikes during seed
            int totalAdded = 0;

            for (int b = 0; b < count; b += batchSize)
            {
                var orders = new List<Order>();
                var logs = new List<OrderAuditLog>();

                for (int i = 0; i < batchSize; i++)
                {
                    var order = new Order
                    {
                        CustomerId = customerIds[random.Next(customerIds.Count)],
                        CategoryId = random.Next(1, 5),
                        IsProcessed = false,
                        CreatedAt = DateTime.UtcNow.AddDays(-random.Next(1, 365)),
                        OrderItems = Enumerable.Range(1, 4).Select(_ => new OrderItem
                        {
                            InventoryId = 34,//inventoryIds[random.Next(inventoryIds.Count)],
                            Quantity = random.Next(1, 10),
                            UnitPrice = (decimal)(random.Next(15, 300) + 0.99)
                        }).ToList()
                    };

                    orders.Add(order);
                }

                _context.Orders.AddRange(orders);
                await _context.SaveChangesAsync(); // Flushes batch to DB

                // Generate Audit Logs for newly created orders
                foreach (var o in orders)
                {
                    logs.Add(new OrderAuditLog
                    {
                        OrderId = o.Id,
                        LogMessage = $"Bulk seeded order for Category {o.CategoryId}.",
                        ProcessedAt = DateTime.UtcNow
                    });
                }

                _context.OrderAuditLogs.AddRange(logs);
                await _context.SaveChangesAsync();

                totalAdded += batchSize;
            }

            watch.Stop();

            int totalOrdersCount = await _context.Orders.CountAsync();
            int totalItemsCount = await _context.OrderItems.CountAsync();

            return Ok(new
            {
                Message = $"Successfully inserted {totalAdded} new orders!",
                TotalOrdersInDB = totalOrdersCount,
                TotalOrderItemsInDB = totalItemsCount,
                TimeTakenSeconds = watch.Elapsed.TotalSeconds
            });
        }

        [HttpGet("get-orders-nplusone-read-ef")]
        public async Task<IActionResult> GetOrdersNPlusOneReadEF([FromQuery] int categoryId)
        {
            var watch = Stopwatch.StartNew();

            // Data access (including the N+1 query loop) delegated to the repository.
            var (response, queryCount) = await _orderSearchRepository.GetOrdersNPlusOneAsync(categoryId);

            watch.Stop();

            return Ok(new
            {
                Method = "N+1 Read Queries Loop (2,000 Orders)",
                TotalDatabaseQueriesExecuted = queryCount, // 1 + 2000 + 2000 = 4,001 SQL queries!
                ResultCount = response.Count,
                TimeTakenMs = watch.ElapsedMilliseconds,
                TimeTakenSeconds = watch.Elapsed.TotalSeconds
            });
        }

    }
}
