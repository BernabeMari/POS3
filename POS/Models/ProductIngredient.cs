using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POS.Models
{
    public class ProductIngredient
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int PageElementId { get; set; }
        
        [ForeignKey("PageElementId")]
        public PageElement PageElement { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string IngredientName { get; set; } = string.Empty;
        
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }
        
        [Required]
        [MaxLength(20)]
        public string Unit { get; set; } = "g"; // Default unit is grams
        
        [MaxLength(255)]
        public string Notes { get; set; } = string.Empty;
    }
} 