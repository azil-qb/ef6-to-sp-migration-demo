using System.Data;
using System.Data.Entity;
using Dapper;
using EFPerformanceDemo.DBContext;
using EFPerformanceDemo.Models;

namespace EFPerformanceDemo.Repositories
{
    /// <summary>
    /// ADO.NET-based implementation of <see cref="IOrderSearchRepository"/>.
    /// Executes dbo.sp_SearchOrdersByProductKeyword directly against the
    /// connection managed by <see cref="AppDbContext"/>, keeping the raw
    /// data-access concern out of the controller layer.
    /// </summary>
    public class OrderSearchRepository : IOrderSearchRepository
    {
        private readonly AppDbContext _context;

        public OrderSearchRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<OrderSearchResult>> SearchOrdersByKeywordAsync(string keyword)
        {
            var result = new List<OrderSearchResult>();

            var connection = _context.Database.Connection;
            var wasClosed = connection.State != ConnectionState.Open;

            if (wasClosed)
                await connection.OpenAsync();

            try
            {
                // Single set-based round-trip: the stored procedure pre-filters to
                // distinct matching OrderIds, then aggregates (SUM/STRING_AGG) in SQL.
                // No cartesian duplication crosses the network, no EF change tracking,
                // no per-order in-memory aggregation.
                using var command = connection.CreateCommand();
                command.CommandText = "dbo.sp_SearchOrdersByProductKeyword";
                command.CommandType = CommandType.StoredProcedure;

                var keywordParam = command.CreateParameter();
                keywordParam.ParameterName = "@Keyword";
                keywordParam.Value = (object?)keyword ?? DBNull.Value;
                command.Parameters.Add(keywordParam);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new OrderSearchResult
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                        CalculatedTotal = reader.GetDecimal(reader.GetOrdinal("CalculatedTotal")),
                        ItemNames = reader.IsDBNull(reader.GetOrdinal("ItemNames"))
                            ? string.Empty
                            : reader.GetString(reader.GetOrdinal("ItemNames"))
                    });
                }
            }
            finally
            {
                if (wasClosed)
                    await connection.CloseAsync();
            }

            return result;
        }

        public async Task<(List<OrderNPlusOneReadResult> Rows, int QueryCount)> GetOrdersNPlusOneAsync(int categoryId)
        {
            // 1. Initial Query (1 SQL call): Fetch top 2,000 orders for a category
            var orders = await _context.Orders
                .Where(o => o.CategoryId == categoryId)
                .Take(2000)
                .ToListAsync();

            // 2. The N+1 Loop: Accessing un-included child collections row-by-row
            // This executes N additional separate SQL SELECT queries against the database!
            var response = new List<OrderNPlusOneReadResult>();

            foreach (var order in orders)
            {
                // Query 1..N: Explicitly loading OrderItems for each individual order ID
                var items = await _context.OrderItems
                    .Where(i => i.OrderId == order.Id)
                    .ToListAsync(); // <-- Executed 2,000 times!

                // Query 1..N: Explicitly loading Customer for each individual customer ID
                var customer = await _context.Customers
                    .FirstOrDefaultAsync(c => c.Id == order.CustomerId); // <-- Executed 2,000 times!

                response.Add(new OrderNPlusOneReadResult
                {
                    Id = order.Id,
                    CustomerName = customer?.FullName,
                    ItemCount = items.Count,
                    TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity)
                });
            }

            var queryCount = 1 + (orders.Count * 2); // 1 + 2000 + 2000 = 4,001 SQL queries!

            return (response, queryCount);
        }

    }
}
