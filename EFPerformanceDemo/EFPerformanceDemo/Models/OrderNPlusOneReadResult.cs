namespace EFPerformanceDemo.Models
{
    /// <summary>
    /// Row shape for the N+1 read demo (GetOrdersNPlusOneAsync), matching the
    /// original controller's anonymous-type output exactly.
    /// </summary>
    public class OrderNPlusOneReadResult
    {
        public int Id { get; set; }
        public string? CustomerName { get; set; }
        public int ItemCount { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
