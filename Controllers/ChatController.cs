using vjp_api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vjp_api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace vjp_api.Controllers
{
    [Route("api/chat")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<ChatController> _logger;

        public ChatController(
            ApplicationDbContext context, 
            IHubContext<ChatHub> hubContext,
            ILogger<ChatController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        // Gửi tin nhắn cá nhân (văn bản hoặc hình ảnh)
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] dynamic dto)
        {
            try
            {
                // Lấy SenderId từ token
                var senderId = User.Identity.Name;
                if (string.IsNullOrEmpty(senderId))
                {
                    return Unauthorized("User not authenticated");
                }
                
                // Chuyển dynamic thành Dictionary để dễ truy cập
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(dto.ToString());
                
                // Kiểm tra các trường bắt buộc
                if (!data.ContainsKey("receiverId") || string.IsNullOrEmpty(data["receiverId"].ToString()))
                {
                    return BadRequest("ReceiverId is required");
                }
                
                if (!data.ContainsKey("content") || string.IsNullOrEmpty(data["content"].ToString()))
                {
                    return BadRequest("Content is required");
                }
                
                string receiverId = data["receiverId"].ToString();
                string content = data["content"].ToString();
                // bool isRead = data.ContainsKey("isRead") && bool.TryParse(data["isRead"].ToString(), out bool isReadValue) ? isReadValue : false;
                
                // Xác định loại tin nhắn (text hoặc image)
                string type = "text";
                string imageUrl = null;
                
                // Kiểm tra nếu content bắt đầu bằng "data:image" thì đây là tin nhắn hình ảnh
                if (content.StartsWith("data:image"))
                {
                    type = "image";
                    imageUrl = content;
                    content = "[Image]"; // Đặt nội dung mặc định cho tin nhắn hình ảnh
                }
                
                // Kiểm tra người nhận có tồn tại
                if (await _context.Users.FindAsync(receiverId) == null)
                {
                    return NotFound("Không tìm thấy người nhận");
                }
                
                // Tạo đối tượng ChatMessage
                var message = new ChatMessage
                {
                    SenderId = senderId,
                    ReceiverId = receiverId,
                    Content = content,
                    IsRead = false,
                    SentAt = DateTime.Now,
                    Type = type,
                    ImageUrl = imageUrl
                };
                
                // Thêm vào cơ sở dữ liệu
                _context.ChatMessages.Add(message);
                await _context.SaveChangesAsync();
                
                // Tạo đối tượng để gửi qua SignalR
                var messageToSend = new
                {
                    id = message.Id,
                    senderId = message.SenderId,
                    receiverId = message.ReceiverId,
                    content = message.Content,
                    sentAt = message.SentAt,
                    isRead = message.IsRead,
                    type = message.Type,
                    imageUrl = message.ImageUrl
                };
                
                // Gửi tin nhắn qua SignalR
                await _hubContext.Clients.Group(message.ReceiverId).SendAsync("ReceiveMessage", messageToSend);
                await _hubContext.Clients.Group(senderId).SendAsync("ReceiveMessage", messageToSend);
                
                return Ok(new { message = "Gửi tin nhắn thành công!", id = message.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending message: {ex.Message}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Lấy lịch sử tin nhắn giữa hai người dùng
        [HttpGet("history/{userId}")]
        public async Task<IActionResult> GetChatHistory(string userId, int page = 1, int pageSize = 20)
        {
            var userIdCurrent = User.Identity.Name;

            var messages = await _context.ChatMessages
                .Where(m => (m.SenderId == userIdCurrent && m.ReceiverId == userId) ||
                            (m.SenderId == userId && m.ReceiverId == userIdCurrent))
                .OrderByDescending(m => m.SentAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost("mark-as-read/{messageId}")]
        public async Task<IActionResult> MarkAsRead(int messageId)
        {
            var message = await _context.ChatMessages.FindAsync(messageId);
            if (message == null)
                return NotFound();
            
            message.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok();
        }
        
        [HttpGet("history/{userId}/latest")]
        public async Task<IActionResult> GetLatestMessages(string userId, [FromQuery] DateTime since, [FromQuery] int limit = 20)
        {
            var userIdCurrent = User.Identity.Name;

            var messages = await _context.ChatMessages
                .Where(m => ((m.SenderId == userIdCurrent && m.ReceiverId == userId) ||
                             (m.SenderId == userId && m.ReceiverId == userIdCurrent)) &&
                            m.SentAt > since)
                .OrderByDescending(m => m.SentAt)
                .Take(limit)
                .ToListAsync();

            return Ok(messages);
        }
    }
}