using vjp_api.Models;

namespace vjp_api.Dtos;

public class FriendDto
{
    public int FriendshipId { get; set; } // Added for easier reference/deletion if needed
    public string FriendId { get; set; }
    public string FriendFullName { get; set; } // Changed from FriendName

}