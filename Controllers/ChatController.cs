using vjp_api.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vjp_api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory; // Thêm namespace này
using Microsoft.Extensions.Logging;
using vjp_api.Dtos; // Thêm namespace này

namespace vjp_api.Controllers
{
    [Route("api/chat")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ChatController> _logger; // Thêm biến này

        public ChatController(
            ApplicationDbContext context, 
            IHubContext<ChatHub> hubContext,
            IMemoryCache cache,
            ILogger<ChatController> logger) // Thêm tham số này
        {
            _context = context;
            _hubContext = hubContext;
            _cache = cache;
            _logger = logger; // Khởi tạo biến
        }

        // Gửi tin nhắn cá nhân
        // Trong ChatController.cs
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatMessageDto dto)
        {
            try
            {
                // Lấy SenderId từ token
                var senderId = User.Identity.Name;
                if (string.IsNullOrEmpty(senderId))
                {
                    return Unauthorized("User not authenticated");
                }
                
                // Log dữ liệu nhận được
                _logger.LogInformation($"Received message request with Content={dto.Content}, ReceiverId={dto.ReceiverId}");
                
                // Kiểm tra người nhận có tồn tại
                if (string.IsNullOrEmpty(dto.ReceiverId) || await _context.Users.FindAsync(dto.ReceiverId) == null)
                    return NotFound("Không tìm thấy người nhận");
                
                // Kiểm tra nội dung tin nhắn
                if (string.IsNullOrEmpty(dto.Content))
                    return BadRequest("Nội dung tin nhắn không được để trống");
                
                // Tạo đối tượng ChatMessage từ DTO
                var model = new ChatMessage
                {
                    SenderId = senderId,
                    ReceiverId = dto.ReceiverId,
                    Content = dto.Content,
                    IsRead = dto.IsRead,
                    SentAt = DateTime.Now
                };
                
                // Thêm vào cơ sở dữ liệu
                _context.ChatMessages.Add(model);
                await _context.SaveChangesAsync();
                
                // Tạo đối tượng đơn giản để gửi qua SignalR
                var messageToSend = new
                {
                    id = model.Id,
                    senderId = model.SenderId,
                    receiverId = model.ReceiverId,
                    content = model.Content,
                    sentAt = model.SentAt,
                    isRead = model.IsRead
                };
                
                // Gửi tin nhắn qua SignalR đến người nhận
                await _hubContext.Clients.Group(model.ReceiverId).SendAsync("ReceiveMessage", messageToSend);
                
                // Gửi tin nhắn qua SignalR đến người gửi (để đồng bộ trên tất cả thiết bị)
                await _hubContext.Clients.Group(senderId).SendAsync("ReceiveMessage", messageToSend);
                
                return Ok(new { message = "Gửi tin nhắn thành công!", id = model.Id });
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

            // Thêm caching để tăng hiệu suất
            // var cacheKey = $"chat_history_{userIdCurrent}_{userId}_{page}_{pageSize}";
            // if (_cache.TryGetValue(cacheKey, out List<ChatMessage> cachedMessages))
            // {
            //     return Ok(cachedMessages);
            // }

            var messages = await _context.ChatMessages
                .Where(m => (m.SenderId == userIdCurrent && m.ReceiverId == userId) ||
                            (m.SenderId == userId && m.ReceiverId == userIdCurrent))
                .OrderByDescending(m => m.SentAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Lưu vào cache trong 1 phút
            // _cache.Set(cacheKey, messages, TimeSpan.FromMinutes(1));

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
        
        // Trong ChatController.cs
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
