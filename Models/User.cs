using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace vjp_api.Models;

public class User : IdentityUser
{
    public string? FullName { get; set; } 
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Avatar { get; set; }
    public bool IsGoogleAccount { get; set; } = false; // Đánh dấu tài khoản Google

    [InverseProperty("Sender")]
    public virtual ICollection<ChatMessage> ChatMessagesSent { get; set; } = new List<ChatMessage>();

    [InverseProperty("Receiver")]
    public virtual ICollection<ChatMessage> ChatMessagesReceived { get; set; } = new List<ChatMessage>();
    public virtual ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}

