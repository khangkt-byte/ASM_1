using System;
using System.Collections.Generic;

namespace ASM_1.Models.Food
{
    public class PaymentShareViewModel
    {
        public int PaymentShareId { get; set; }
        public string UserSessionId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal? Percentage { get; set; }
        public IReadOnlyCollection<int> ItemIds { get; set; } = Array.Empty<int>();
        public bool IsCurrentUser { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PaymentSessionSummaryViewModel
    {
        public int? PaymentSessionId { get; set; }
        public string? SplitMode { get; set; }
        public string? SplitModeKey { get; set; }
        public bool IsFinalized { get; set; }
        public int? ParticipantCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public decimal? TotalPercentage { get; set; }
        public IReadOnlyCollection<PaymentShareViewModel> Shares { get; set; } = Array.Empty<PaymentShareViewModel>();
    }

    public class PaymentOrderItemViewModel
    {
        public int OrderItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal LineTotal { get; set; }
        public int Quantity { get; set; }
    }

    public class PaymentInvoiceViewModel
    {
        public int InvoiceId { get; set; }
        public string InvoiceCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal FinalAmount { get; set; }
    }

    public class PaymentInfoViewModel
    {
        public PaymentSessionSummaryViewModel Session { get; set; } = new();
        public IReadOnlyCollection<PaymentOrderItemViewModel> Items { get; set; } = Array.Empty<PaymentOrderItemViewModel>();
        public PaymentInvoiceViewModel? Invoice { get; set; }
        public IReadOnlyCollection<PaymentMethodOptionViewModel> AvailableMethods { get; set; } = Array.Empty<PaymentMethodOptionViewModel>();
    }

    public class PaymentMethodOptionViewModel
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
