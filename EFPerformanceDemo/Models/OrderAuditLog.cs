namespace EFPerformanceDemo.Models
{
    public class OrderAuditLog
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string LogMessage { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
