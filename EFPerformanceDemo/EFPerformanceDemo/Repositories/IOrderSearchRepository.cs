using EFPerformanceDemo.Models;
using EFPerformanceDemo.Models;

namespace EFPerformanceDemo.Repositories
{
    /// <summary>
    /// Data-access abstraction for order search operations backed by stored procedures.
    /// </summary>
    public interface IOrderSearchRepository
    {
        /// <summary>
        /// Executes dbo.sp_SearchOrdersByProductKeyword and returns one aggregated
        /// row per matching order (no cartesian duplication).
        /// </summary>
        Task<List<OrderSearchResult>> SearchOrdersByKeywordAsync(string keyword);

        /// <summary>
        /// Fetches up to 2,000 orders for the given category, then loads each
        /// order's OrderItems and Customer individually (N+1 pattern), matching
        /// the original GetOrdersNPlusOneReadEF behavior exactly.
        /// </summary>
        Task<(List<OrderNPlusOneReadResult> Rows, int QueryCount)> GetOrdersNPlusOneAsync(int categoryId);
    }
}
