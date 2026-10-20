namespace EFPerformanceDemo.Models
{
    public class AccountDriftDto
    {
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal TotalPortfolioValue { get; set; }
        public decimal CurrentCashWeight { get; set; }
        public decimal TargetCashWeight { get; set; }
        public decimal AbsoluteDrift { get; set; }
        public System.DateTime? LastRebalanceDate { get; set; }
    }
}
