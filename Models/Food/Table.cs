using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ASM_1.Models.Food
{
    public class Table
    {
        [Key]
        public int TableId { get; set; }

        [Required, StringLength(50)]
        public string TableName { get; set; } = string.Empty;

        public int SeatCount { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Available";

        [Column(TypeName = "decimal(5,2)")]
        public decimal? DynamicPriceFactor { get; set; }

        public DateTime? DynamicPriceValidUntil { get; set; }

        [StringLength(150)]
        public string? DynamicPriceLabel { get; set; }

        // Navigation
        public ICollection<TableInvoice>? TableInvoices { get; set; }
    }
}
