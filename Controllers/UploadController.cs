using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using System;
using vjp_api.Models;
using vjp_api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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
        
        [HttpPost("group-image")]
        public async Task<IActionResult> UploadGroupImage([FromForm] GroupImageUploadDto dto)
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

                if (dto.GroupId <= 0)
                {
                    return BadRequest("GroupId is required and must be greater than 0");
                }

                // Kiểm tra người dùng có trong nhóm không
                var userInGroup = await _context.UserGroups
                    .AnyAsync(ug => ug.GroupChatId == dto.GroupId && 
                                   ug.UserId == senderId);
                                   
                if (!userInGroup)
                    return Forbid("Bạn không phải thành viên của nhóm này");

                // Tạo thư mục uploads nếu chưa tồn tại
                string uploadsFolder = Path.Combine(_environment.ContentRootPath, "uploads", "groups");
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
                string imageUrl = $"/uploads/groups/{uniqueFileName}";

                // Tạo tin nhắn mới
                var message = new GroupMessage
                {
                    SenderId = senderId,
                    GroupChatId = dto.GroupId,
                    Content = dto.Caption ?? "[Hình ảnh]",
                    SentAt = DateTime.Now,
                    Type = "image",
                    ImageUrl = imageUrl
                };

                // Lưu vào database
                _context.GroupMessages.Add(message);
                await _context.SaveChangesAsync();

                // Gửi tin nhắn qua SignalR
                var messageToSend = new
                {
                    id = message.Id,
                    senderId = message.SenderId,
                    groupId = message.GroupChatId,
                    content = message.Content,
                    sentAt = message.SentAt,
                    type = message.Type,
                    imageUrl = message.ImageUrl
                };

                // // Lấy tất cả thành viên trong nhóm
                // var groupMembers = await _context.UserGroups
                //     .Where(ug => ug.GroupChatId == dto.GroupId)
                //     .Select(ug => ug.UserId)
                //     .ToListAsync();
                //
                // // Gửi tin nhắn tới tất cả thành viên
                // foreach (var memberId in groupMembers)
                // {
                //     await _hubContext.Clients.Group(memberId).SendAsync("ReceiveGroupMessage", messageToSend);
                // }

                // Gửi tin nhắn qua SignalR đến nhóm
                string groupSignalRName = $"group_{dto.GroupId}";
                await _hubContext.Clients.Group(groupSignalRName)
                    .SendAsync("ReceiveGroupMessage", messageToSend);
                
                return Ok(new { 
                    message = "Upload thành công", 
                    id = message.Id,
                    imageUrl = imageUrl 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading group image: {ex.Message}");
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
    
    public class GroupImageUploadDto
    {
        public IFormFile File { get; set; }
        public int GroupId { get; set; }
        public string Caption { get; set; }
    }
}