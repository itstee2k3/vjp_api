using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vjp_api.Models;
public class ChatMessage
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public string SenderId { get; set; }
    
    [ForeignKey(nameof(SenderId))]
    public virtual User? Sender { get; set; }

    [Required] 
    public string ReceiverId { get; set; }
    
    [ForeignKey(nameof(ReceiverId))]
    public virtual User? Receiver { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Content { get; set; }
    
    public DateTime SentAt { get; set; } = DateTime.Now;
    public bool IsRead { get; set; } = false;
    
    public string? ImageUrl { get; set; }
    public string Type { get; set; } = "text"; // "text" hoặc "image"
}