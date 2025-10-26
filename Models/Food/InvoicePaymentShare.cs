using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ASM_1.Models.Food
{
    public class InvoicePaymentShare
    {
        [Key]
        public int InvoicePaymentShareId { get; set; }

        [Required]
        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = default!;

        [Required, MaxLength(80)]
        public string ParticipantId { get; set; } = string.Empty;

        [MaxLength(120)]
        public string? DisplayName { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required, MaxLength(40)]
        public string PaymentMethod { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string SplitMode { get; set; } = string.Empty;

        [Column(TypeName = "decimal(5,2)")]
        public decimal? Percentage { get; set; }

        [MaxLength(400)]
        public string? MetaJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
