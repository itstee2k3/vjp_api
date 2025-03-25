using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using System;
using vjp_api.Models;
using vjp_api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace vjp_api.Controllers
{
    [Route("api/upload")]
    [ApiController]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<UploadController> _logger;

        public UploadController(
            IWebHostEnvironment environment,
            ApplicationDbContext context,
            IHubContext<ChatHub> hubContext,
            ILogger<UploadController> logger)
        {
            _environment = environment;
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpGet("test")]
        public IActionResult TestUpload()
        {
            return Ok(new { message = "Upload API is available" });
        }

        [HttpPost("image")]
        public async Task<IActionResult> UploadImage([FromForm] ImageUploadDto dto)
        {
            try
            {
                var senderId = User.Identity.Name;
                if (string.IsNullOrEmpty(senderId))
                {
                    return Unauthorized("User not authenticated");
                }

                if (dto.File == null || dto.File.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (string.IsNullOrEmpty(dto.ReceiverId))
                {
                    return BadRequest("ReceiverId is required");
                }

                // Kiểm tra người nhận có tồn tại
                if (await _context.Users.FindAsync(dto.ReceiverId) == null)
                {
                    return NotFound("Không tìm thấy người nhận");
                }

                // Tạo thư mục uploads nếu chưa tồn tại
                string uploadsFolder = Path.Combine(_environment.ContentRootPath, "uploads", "images");
                _logger.LogInformation($"Saving to directory: {uploadsFolder}");
                
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Tạo tên file duy nhất
                string uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(dto.File.FileName)}";
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Lưu file
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.File.CopyToAsync(fileStream);
                }

                // Tạo URL cho hình ảnh
                string imageUrl = $"/uploads/images/{uniqueFileName}";

                // Tạo tin nhắn mới
                var message = new ChatMessage
                {
                    SenderId = senderId,
                    ReceiverId = dto.ReceiverId,
                    Content = dto.Caption ?? "[Hình ảnh]",
                    IsRead = false,
                    SentAt = DateTime.Now,
                    Type = "image",
                    ImageUrl = imageUrl
                };

                // Lưu vào database
                _context.ChatMessages.Add(message);
                await _context.SaveChangesAsync();

                // Gửi tin nhắn qua SignalR
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

                await _hubContext.Clients.Group(message.ReceiverId).SendAsync("ReceiveMessage", messageToSend);
                await _hubContext.Clients.Group(senderId).SendAsync("ReceiveMessage", messageToSend);

                return Ok(new { 
                    message = "Upload thành công", 
                    id = message.Id,
                    imageUrl = imageUrl 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading image: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }

    public class ImageUploadDto
    {
        public IFormFile File { get; set; }
        public string ReceiverId { get; set; }
        public string Caption { get; set; }
    }
}