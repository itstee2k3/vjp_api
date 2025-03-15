using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using vjp_api.Data;
using vjp_api.Models;

namespace vjp_api.Controllers
{
    // [Authorize]
    [Route("api/user")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserController> _logger;

        public UserController(ApplicationDbContext context, ILogger<UserController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/user
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var currentUserId = User.Identity?.Name;
                _logger.LogInformation($"Current User ID: {currentUserId}");

                if (string.IsNullOrEmpty(currentUserId))
                {
                    return Unauthorized(new { message = "Token không hợp lệ hoặc đã hết hạn" });
                }

                var users = await _context.Users
                    .Where(u => u.Id != currentUserId)
                    .Select(u => new
                    {
                        u.Id,
                        u.Email,
                        u.FullName,
                        IsOnline = true,
                        LastSeen = DateTime.UtcNow
                    })
                    .OrderBy(u => u.FullName)
                    .ToListAsync();

                return Ok(new
                {
                    message = "Lấy danh sách người dùng thành công",
                    users = users
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in GetUsers: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi server khi lấy danh sách người dùng" });
            }
        }
    }
}