using System.ComponentModel.DataAnnotations;

namespace vjp_api.Models;

public class GroupChat
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Avatar { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public virtual ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
    public virtual ICollection<GroupMessage> GroupMessages { get; set; } = new List<GroupMessage>();
}