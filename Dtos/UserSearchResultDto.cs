using vjp_api.Models;

namespace vjp_api.Dtos;

public class UserSearchResultDto
{
    public string Id { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string? AvatarUrl { get; set; }
    
    public FriendshipStatus? FriendshipStatus { get; set; } // Thêm trạng thái quan hệ (có thể null nếu không có qh)

    public bool? IsRequestSentByCurrentUser { get; set; } // Thêm để phân biệt pending_sent/pending_received
}