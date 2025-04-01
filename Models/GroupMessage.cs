using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vjp_api.Models;

public class GroupMessage
{
    [Key]
    public int Id { get; set; }

    public string Type { get; set; } = "text";
    public string? ImageUrl { get; set; }
    public string Content { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    
    [ForeignKey("Sender")]
    public string SenderId { get; set; }
    public virtual User Sender { get; set; }

    [ForeignKey("GroupChat")]
    public int GroupChatId { get; set; }
    public virtual GroupChat GroupChat { get; set; }
}