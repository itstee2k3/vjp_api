using Microsoft.AspNetCore.Mvc;
using vjp_api.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using vjp_api.Data;

namespace vjp_api.Controllers
{
    [Route("api/group")]
    [ApiController]
    public class GroupChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public GroupChatController(ApplicationDbContext context)
        {
            _context = context;
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
        public async Task<IActionResult> SendGroupMessage([FromBody] GroupMessage message)
        {
            // Kiểm tra người dùng có trong nhóm không
            var userInGroup = await _context.UserGroups
                .AnyAsync(ug => ug.GroupChatId == message.GroupChatId && 
                               ug.UserId == User.Identity.Name);
                               
            if (!userInGroup)
                return Forbid("Bạn không phải thành viên của nhóm này");

            _context.GroupMessages.Add(message);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Gửi tin nhắn nhóm thành công!" });
        }

        // Lấy tin nhắn của nhóm
        [HttpGet("messages/{groupId}")]
        public async Task<IActionResult> GetGroupMessages(int groupId)
        {
            var messages = await _context.GroupMessages
                .Where(m => m.GroupChatId == groupId)
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            return Ok(messages);
        }
    }
}
public class CreateGroupRequest
{
    public string Name { get; set; }
    public string? Avatar { get; set; }
    public List<string> MemberIds { get; set; }
}