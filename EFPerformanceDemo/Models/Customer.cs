namespace EFPerformanceDemo.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public int RewardPoints { get; set; }
        public List<Order> Orders { get; set; } = new();
    }
}
