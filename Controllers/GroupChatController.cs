using System.ComponentModel.DataAnnotations;
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
    public class GroupChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<GroupChatController> _logger;
        
        public GroupChatController(
            ApplicationDbContext context,
            IHubContext<ChatHub> hubContext,
            ILogger<GroupChatController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
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