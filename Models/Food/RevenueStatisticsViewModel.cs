using System;
using System.Collections.Generic;

namespace ASM_1.Models.Food
{
    public class RevenueStatisticsViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public List<RevenuePoint> DailyRevenue { get; set; } = new();
        public List<RevenuePoint> WeeklyRevenue { get; set; } = new();
        public decimal TotalRevenue { get; set; }
        public int TotalInvoices { get; set; }
        public decimal AverageDailyRevenue { get; set; }
        public decimal AverageWeeklyRevenue { get; set; }
    }

    public class RevenuePoint
    {
        public string Label { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal Amount { get; set; }
        public int InvoiceCount { get; set; }
    }
}
