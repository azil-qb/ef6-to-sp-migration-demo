namespace EFPerformanceDemo.Models
{
    public class Holding
    {
        public int Id { get; set; }
        public int ClientAccountId { get; set; }
        public string AssetClass { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal CurrentPrice { get; set; }

        public ClientAccount? ClientAccount { get; set; }
    }
}
