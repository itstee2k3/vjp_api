// Thêm vào Models/ChatMessageDto.cs
namespace vjp_api.Dtos
{
    public class ChatMessageDto
    {
        public string Content { get; set; }
        public string ReceiverId { get; set; }
        public bool IsRead { get; set; }
    }
}