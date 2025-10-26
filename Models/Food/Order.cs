using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ASM_1.Models.Food
{
    public static class OrderStatus
    {
        public const string Pending = "PENDING";
        public const string Confirmed = "CONFIRMED";
        public const string InKitchen = "IN_KITCHEN";
        public const string Ready = "READY";
        public const string Served = "SERVED";
        public const string RequestedBill = "REQUESTED_BILL";
        public const string Paid = "PAID";
        public const string Cancelled = "CANCELLED";

        public static IReadOnlyCollection<string> All { get; } = new[]
        {
            Pending,
            Confirmed,
            InKitchen,
            Ready,
            Served,
            RequestedBill,
            Paid,
            Cancelled
        };
    }

    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        [Required, StringLength(30)]
        public string OrderCode { get; set; } = string.Empty;

        public int? TableId { get; set; }

        [StringLength(20)]
        public string? TableCode { get; set; }

        [StringLength(100)]
        public string? TableName { get; set; }

        [StringLength(120)]
        public string? CustomerSessionId { get; set; }

        [Required, StringLength(30)]
        public string Status { get; set; } = OrderStatus.Pending;

        [StringLength(30)]
        public string PaymentMethod { get; set; } = "cod";

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FinalAmount { get; set; }

        public string? Note { get; set; }

        public DateTime PlacedAt { get; set; } = DateTime.UtcNow;

        public Table? Table { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}
