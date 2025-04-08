namespace vjp_api.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Friendship
{
    [Key]
    public int Id { get; set; } // Hoặc dùng Guid nếu bạn thích

    [Required]
    public string UserRequesterId { get; set; } // Foreign key đến ApplicationUser

    [Required]
    public string UserReceiverId { get; set; } // Foreign key đến ApplicationUser

    [Required]
    public FriendshipStatus Status { get; set; } // Sử dụng Enum

    public DateTime RequestedAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    [ForeignKey(nameof(UserRequesterId))]
    public virtual User? Requester { get; set; }

    [ForeignKey(nameof(UserReceiverId))]
    public virtual User? Receiver { get; set; }
}

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Blocked = 3 // Có thể thêm trạng thái block nếu cần
}