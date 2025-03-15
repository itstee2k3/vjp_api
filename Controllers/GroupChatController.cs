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

        // Tạo nhóm chat mới
        [HttpPost("create")]
        public async Task<IActionResult> CreateGroup([FromBody] GroupChat group)
        {
            _context.GroupChats.Add(group);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Tạo nhóm thành công!" });
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