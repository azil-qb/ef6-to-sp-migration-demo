namespace EFPerformanceDemo.Models
{
    public class Inventory
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
    }
}
