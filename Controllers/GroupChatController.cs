using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using vjp_api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using vjp_api.Controllers;
using vjp_api.Data;


namespace vjp_api.Controllers
{
    [Route("api/group")]
    [ApiController]
    [Authorize] 
    public class GroupChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<GroupChatController> _logger;
        private readonly IWebHostEnvironment _environment;
        
        public GroupChatController(
            ApplicationDbContext context,
            IHubContext<ChatHub> hubContext,
            ILogger<GroupChatController> logger,
            IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
            _environment = webHostEnvironment; // <<< Assign it here
        }

        [HttpGet]
        public async Task<IActionResult> GetMyGroups()
        {
            var userId = User.Identity.Name;
            var groups = await _context.UserGroups
                .Where(ug => ug.UserId == userId)
                .Include(ug => ug.GroupChat)
                .Select(ug => new {
                    ug.GroupChat.Id,
                    ug.GroupChat.Name,
                    ug.GroupChat.Avatar,
                    ug.GroupChat.CreatedAt,
                    MemberCount = ug.GroupChat.UserGroups.Count,
                    IsAdmin = ug.IsAdmin
                })
                .ToListAsync();
            
            return Ok(groups);
        }
        
        // Tạo nhóm chat mới
        [HttpPost("create")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
        {
            try 
            {
                var userId = User.Identity.Name;
    
                var group = new GroupChat
                {
                    Name = request.Name,
                    Avatar = request.Avatar
                };

                // Thêm người tạo nhóm là admin
                group.UserGroups.Add(new UserGroup 
                { 
                    UserId = userId,
                    IsAdmin = true 
                });

                // Thêm các thành viên khác
                foreach (var memberId in request.MemberIds)
                {
                    if (memberId != userId)
                    {
                        group.UserGroups.Add(new UserGroup 
                        { 
                            UserId = memberId,
                            IsAdmin = false
                        });
                    }
                }

                _context.GroupChats.Add(group);
                await _context.SaveChangesAsync();
    
                // Trả về response format phù hợp thay vì trả về toàn bộ object group
                return Ok(new {
                    success = true,
                    group = new {
                        id = group.Id,
                        name = group.Name,
                        avatar = group.Avatar,
                        createdAt = group.CreatedAt,
                        memberCount = group.UserGroups.Count,
                        isAdmin = true // Người tạo luôn là admin
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new {
                    success = false,
                    message = "Không thể tạo nhóm: " + ex.Message
                });
            }
        }

        // Lấy thông tin thành viên nhóm
        [HttpGet("{groupId}/members")]
        public async Task<IActionResult> GetGroupMembers(int groupId)
        {
            var members = await _context.UserGroups
                .Where(ug => ug.GroupChatId == groupId)
                .Include(ug => ug.User)
                .Select(ug => new {
                    ug.User.Id,
                    ug.User.FullName,
                    ug.User.Avatar,
                    ug.IsAdmin,
                    ug.JoinedAt
                })
                .ToListAsync();
            
            return Ok(members);
        }

        // Gửi tin nhắn vào nhóm
        [HttpPost("send")]
        public async Task<IActionResult> SendGroupMessage([FromBody] SendGroupMessageDto dto)
        {
            try
            {
                var userId = User.Identity.Name;

                // Kiểm tra người dùng có trong nhóm không
                var userInGroup = await _context.UserGroups
                    .AnyAsync(ug => ug.GroupChatId == dto.GroupChatId && 
                                   ug.UserId == userId);
                                   
                if (!userInGroup)
                    return Forbid("Bạn không phải thành viên của nhóm này");

                // Xác định loại tin nhắn (text hoặc image)
                string type = dto.Type ?? "text";
                string? imageUrl = dto.ImageUrl;
                string content = dto.Content;
                
                // Kiểm tra nếu nội dung là base64 image
                if (content.StartsWith("data:image"))
                {
                    type = "image";
                    imageUrl = content;
                    content = "[Hình ảnh]"; // Đặt nội dung mặc định cho tin nhắn hình ảnh
                }

                // Create new GroupMessage with user ID from auth token
                var message = new GroupMessage
                {
                    SenderId = userId,
                    GroupChatId = dto.GroupChatId,
                    Content = content,
                    SentAt = DateTime.Now,
                    Type = type,
                    ImageUrl = imageUrl
                };
                
                _context.GroupMessages.Add(message);
                await _context.SaveChangesAsync();
                
                // Tạo đối tượng để gửi qua SignalR
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
                
                
                string groupSignalRName = $"group_{dto.GroupChatId}";
                await _hubContext.Clients.Group(groupSignalRName)
                    .SendAsync("ReceiveGroupMessage", messageToSend);
                
                return Ok(new { 
                    message = "Gửi tin nhắn nhóm thành công!", 
                    id = message.Id,
                    sentAt = message.SentAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending group message: {ex.Message}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Lấy tin nhắn của nhóm
        [HttpGet("messages/{groupId}")]
        public async Task<IActionResult> GetGroupMessages(int groupId, int page = 1, int pageSize = 20)
        {
            var query = _context.GroupMessages
                .Where(m => m.GroupChatId == groupId)
                .OrderByDescending(m => m.SentAt);

            var totalCount = await query.CountAsync();

            var messages = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var hasMore = (page * pageSize) < totalCount;

            return Ok(new
            {
                data = messages,
                hasMore = hasMore,
                currentPage = page,
                pageSize = pageSize
            });
        }
        
        [HttpPost("{groupId}/avatar")]
        public async Task<IActionResult> UploadGroupImage(int groupId, [FromForm] IFormFile file)
        {
            try
            {
                var userId = User.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User not authenticated");
                }

                // Kiểm tra quyền admin của người dùng trong nhóm
                var userGroup = await _context.UserGroups
                    .FirstOrDefaultAsync(ug => ug.GroupChatId == groupId && ug.UserId == userId);
                
                if (userGroup == null)
                {
                    return NotFound("You are not a member of this group.");
                }

                if (!userGroup.IsAdmin)
                {
                    return Forbid("Only group admins can change the group image.");
                }

                // Validate file
                if (file == null || file.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (file.Length > 5 * 1024 * 1024) // 5MB limit
                {
                    return BadRequest("File size exceeds limit (5MB)");
                }

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest("Invalid file type. Only JPG, PNG, and GIF are allowed.");
                }

                // Create group_avatars directory if it doesn't exist
                string uploadsFolder = Path.Combine(_environment.ContentRootPath, "uploads", "group_avatars");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Generate unique filename with group ID prefix
                string uniqueFileName = $"{groupId}_{Guid.NewGuid()}{extension}";
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Save file
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                // Update group avatar URL in database
                var group = await _context.GroupChats.FindAsync(groupId);
                if (group == null)
                {
                    // Clean up uploaded file if group not found
                    System.IO.File.Delete(filePath);
                    return NotFound("Group not found");
                }

                // Delete old avatar file if exists
                if (!string.IsNullOrEmpty(group.Avatar))
                {
                    var oldFilePath = Path.Combine(_environment.ContentRootPath, 
                        group.Avatar.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }

                // Update avatar URL to point to group_avatars directory
                string imageUrl = $"/uploads/group_avatars/{uniqueFileName}";
                group.Avatar = imageUrl;
                await _context.SaveChangesAsync();

                // Send SignalR notification to all group members
                string groupSignalRName = $"group_{groupId}";
                await _hubContext.Clients.Group(groupSignalRName).SendAsync("GroupImageUpdated", new
                {
                    groupId = groupId,
                    imageUrl = imageUrl,
                    updatedBy = userId
                });

                return Ok(new { 
                    message = "Group image updated successfully",
                    imageUrl = imageUrl 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading group image: {ex.Message}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
        
        [HttpPut("{groupId}/name")]
        public async Task<IActionResult> UpdateGroupName(int groupId, [FromBody] UpdateGroupNameRequest request)
        {
            try
            {
                var userId = User.Identity?.Name;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User not authenticated");
                }

                // Check if user is an admin of the group
                var userGroup = await _context.UserGroups
                    .FirstOrDefaultAsync(ug => ug.GroupChatId == groupId && ug.UserId == userId);

                if (userGroup == null)
                {
                    return NotFound("You are not a member of this group.");
                }

                if (!userGroup.IsAdmin)
                {
                    return Forbid("Only group admins can change the group name.");
                }

                // Find the group
                var group = await _context.GroupChats.FindAsync(groupId);
                if (group == null)
                {  
                    return NotFound("Group not found");
                }

                // Validate the new name
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest("Group name cannot be empty.");
                }
                
                if (request.Name.Length > 100) // Example length limit
                {
                    return BadRequest("Group name is too long (max 100 characters).");
                }

                // Update the name
                group.Name = request.Name.Trim();
                await _context.SaveChangesAsync();

                // Send SignalR notification to group members
                string groupSignalRName = $"group_{groupId}";
                await _hubContext.Clients.Group(groupSignalRName).SendAsync("GroupNameUpdated", new
                {
                    groupId = groupId,
                    name = group.Name,
                    updatedBy = userId
                });
                
                _logger.LogInformation($"Group name updated successfully for groupId: {groupId} by user: {userId}");

                return Ok(new { message = "Group name updated successfully", name = group.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating group name for groupId: {groupId}");
                return StatusCode(500, "Internal server error while updating group name.");
            }
        }

    }
}
public class CreateGroupRequest
{
    public string Name { get; set; }
    public string? Avatar { get; set; }
    public List<string> MemberIds { get; set; }
}

public class SendGroupMessageDto
{
    [Required]
    public int GroupChatId { get; set; }
    
    [Required]
    public string Content { get; set; }
    
    public string? ImageUrl { get; set; }
    
    public string? Type { get; set; }
}

public class UpdateGroupNameRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
} 