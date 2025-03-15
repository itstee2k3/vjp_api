using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vjp_api.Models;

public class UserGroup
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("User")]
    public string UserId { get; set; }
    public virtual User User { get; set; }

    [ForeignKey("GroupChat")]
    public int GroupChatId { get; set; }
    public virtual GroupChat GroupChat { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsAdmin { get; set; } = false;
}