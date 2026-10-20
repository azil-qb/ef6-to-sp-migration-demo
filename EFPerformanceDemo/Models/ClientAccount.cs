using System;
using System.Collections.Generic;

namespace EFPerformanceDemo.Models
{
    public class ClientAccount
    {
        public int Id { get; set; }
        public Guid AdvisorFirmId { get; set; }
        public bool IsActive { get; set; } = true;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public List<Holding> Holdings { get; set; } = new();
        public ModelPortfolio ModelPortfolio { get; set; } = new ModelPortfolio();
        public DateTime? LastRebalanceTime { get; set; }
    }
}
