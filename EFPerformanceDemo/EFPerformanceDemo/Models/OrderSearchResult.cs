namespace EFPerformanceDemo.Models
{
    /// <summary>
    /// Row model for dbo.sp_SearchOrdersByProductKeyword result set.
    /// Column names must exactly match the stored procedure's SELECT aliases.
    /// </summary>
    public class OrderSearchResult
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal CalculatedTotal { get; set; }
        public string ItemNames { get; set; } = string.Empty;
    }
}
