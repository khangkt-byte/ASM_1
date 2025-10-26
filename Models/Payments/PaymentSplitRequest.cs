using System.ComponentModel.DataAnnotations;

namespace ASM_1.Models.Payments
{
    public class PaymentSplitRequest
    {
        [Required]
        public string Mode { get; set; } = "full";

        public List<PaymentSplitParticipant> Participants { get; set; } = new();
    }

    public class PaymentSplitParticipant
    {
        [Required]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string? Name { get; set; }

        public string? PaymentMethod { get; set; }

        public decimal? Percentage { get; set; }

        public bool? PaysRemaining { get; set; }

        public List<PaymentSplitItemSelection>? Items { get; set; }
    }

    public class PaymentSplitItemSelection
    {
        public int CartItemId { get; set; }

        public int Quantity { get; set; }
    }
}
