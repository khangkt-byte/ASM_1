using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ASM_1.Models.Food
{
    public enum PaymentSplitMode
    {
        Full,
        SplitEvenly,
        SplitByPercentage,
        PayOwnItems
    }

    public class PaymentSession
    {
        [Key]
        public int PaymentSessionId { get; set; }

        [Required]
        public int OrderId { get; set; }
        public Order Order { get; set; } = default!;

        [Required]
        public PaymentSplitMode SplitMode { get; set; } = PaymentSplitMode.Full;

        /// <summary>
        ///     Used for split-evenly scenario so we know the intended number of participants.
        /// </summary>
        public int? ParticipantCount { get; set; }

        [MaxLength(200)]
        public string? Notes { get; set; }

        [MaxLength(120)]
        public string? CreatedBySessionId { get; set; }

        public bool IsFinalized { get; set; }

        [StringLength(4000)]
        public string? Metadata { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PaymentShare> Shares { get; set; } = new List<PaymentShare>();
    }

    public class PaymentShare
    {
        [Key]
        public int PaymentShareId { get; set; }

        [Required]
        public int PaymentSessionId { get; set; }
        public PaymentSession PaymentSession { get; set; } = default!;

        [Required, MaxLength(120)]
        public string UserSessionId { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? DisplayName { get; set; }

        [Required, MaxLength(40)]
        public string PaymentMethod { get; set; } = "cash";

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        /// <summary>
        ///     Stored for percentage split mode.
        /// </summary>
        public decimal? Percentage { get; set; }

        [MaxLength(4000)]
        public string? Metadata { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
