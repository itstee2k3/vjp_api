namespace vjp_api.Dtos;

public class FriendRequestDto
{
    public int FriendshipId { get; set; }
    public string RequesterId { get; set; }
    public string RequesterFullName { get; set; } // Changed from RequesterName
    public DateTime RequestedAt { get; set; } // Changed from RequestTime for consistency with model
}