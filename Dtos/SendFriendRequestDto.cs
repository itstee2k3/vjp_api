using System.ComponentModel.DataAnnotations;

namespace vjp_api.Dtos;

public class SendFriendRequestDto
{
    [Required]
    public string ReceiverId { get; set; }
}