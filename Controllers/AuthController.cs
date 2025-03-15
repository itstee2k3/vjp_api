using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DnsClient;
using vjp_api.Models;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Authorization;
using MimeKit;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using vjp_api.Data;

namespace vjp_api.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ApplicationDbContext _context;

        public AuthController(UserManager<User> userManager, SignInManager<User> signInManager, IConfiguration configuration, IMemoryCache cache, ApplicationDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _cache = cache;
            _context = context;
        }

        // 🔹 Đăng ký tài khoản mới
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel model, [FromServices] IEmailSender emailSender)
        {
            if (!ModelState.IsValid) 
                return BadRequest(ModelState);

            Console.WriteLine($"📧 Kiểm tra đăng ký với email: {model.Email}");

            // Kiểm tra email có hợp lệ không
            if (!await IsValidEmailAsync(model.Email))
            {
                return BadRequest(new { message = "Email không hợp lệ hoặc không tồn tại!" });
            }

            // Kiểm tra xem email đã tồn tại chưa
            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                return BadRequest(new { message = "Email đã được sử dụng!" });
            }

            // Tạo token xác nhận email
            string token = Guid.NewGuid().ToString();
            string key = $"pending_registration:{model.Email}";

            // Lưu vào Redis (Chỉ lưu trong 30 phút)
            var cacheData = new
            {
                FullName = model.FullName,
                Email = model.Email,
                Password = model.Password,
                Token = token
            };

            _cache.Set(key, cacheData, TimeSpan.FromMinutes(30));

            var appUrl = $"{Request.Scheme}://{Request.Host.Value}";

            // Gửi email xác nhận
            var confirmLink = $"{appUrl}/api/auth/confirm-email?email={model.Email}&token={token}";
            await emailSender.SendEmailAsync(model.Email, "Xác nhận tài khoản", $"<a href='{confirmLink}'>Xác thực Email</a>");
            Console.WriteLine($"📧 Link xác nhận email: {confirmLink}");

            return Ok(new { message = "Đăng ký thành công! Vui lòng kiểm tra email để xác nhận tài khoản." });
        }
        
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] TokenModel model)
        {
            try
            {
                // Lấy userId từ token
                var userId = User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    Console.WriteLine("Không tìm thấy userId trong token");
                    return Unauthorized(new { message = "Người dùng chưa xác thực!" });
                }

                Console.WriteLine($"Đang đăng xuất: UserId={userId}, RefreshToken={model.RefreshToken}");

                // Kiểm tra model
                if (model == null || string.IsNullOrEmpty(model.RefreshToken))
                {
                    Console.WriteLine("RefreshToken không được gửi lên hoặc rỗng");
                    return BadRequest(new { message = "RefreshToken không được gửi lên" });
                }

                // Xóa Refresh Token từ database
                var refreshToken = await _context.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == model.RefreshToken && rt.UserId == userId);
                
                if (refreshToken != null)
                {
                    Console.WriteLine($"Đã tìm thấy refresh token, ID={refreshToken.Id}");
                    _context.RefreshTokens.Remove(refreshToken);
                    await _context.SaveChangesAsync();
                    Console.WriteLine("Đã xóa refresh token thành công");
                }
                else
                {
                    // Thử tìm token mà không cần khớp userId
                    var anyToken = await _context.RefreshTokens
                        .FirstOrDefaultAsync(rt => rt.Token == model.RefreshToken);
                        
                    if (anyToken != null)
                    {
                        Console.WriteLine($"Tìm thấy token nhưng không khớp userId. TokenUserId={anyToken.UserId}, CurrentUserId={userId}");
                    }
                    else
                    {
                        Console.WriteLine("Không tìm thấy refresh token trong database");
                    }
                }

                return Ok(new { message = "Đăng xuất thành công!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi đăng xuất: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Lỗi server khi đăng xuất" });
            }
        }

        
        // 🔹 Xác nhận email
        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(string email, string token)
        {
            string key = $"pending_registration:{email}";
            var cacheData = _cache.Get<dynamic>(key);

            if (cacheData == null || cacheData.Token != token)
            {
                return BadRequest(new { message = "Liên kết xác nhận không hợp lệ hoặc đã hết hạn!" });
            }

            // Tạo tài khoản trong database
            var user = new User
            {
                UserName = email,
                Email = email,
                FullName = cacheData.FullName,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, cacheData.Password);
            if (!result.Succeeded) 
                return BadRequest(result.Errors);

            // Xóa dữ liệu tạm khỏi Redis
            _cache.Remove(key);

            return Ok(new { message = "Xác thực email thành công! Bạn có thể đăng nhập ngay bây giờ." });
        }


        // 🔹 Đăng nhập bằng email (chỉ cho phép nếu email đã xác nhận)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Unauthorized(new { message = "Email không tồn tại!" });

            if (!await _userManager.IsEmailConfirmedAsync(user))
                return Unauthorized(new { message = "Bạn cần xác nhận email trước khi đăng nhập!" });

            if (!await _userManager.CheckPasswordAsync(user, model.Password))
                return Unauthorized(new { message = "Sai mật khẩu!" });

            // Tạo Access Token
            var accessToken = GenerateJwtToken(user);

            // Tạo Refresh Token
            var refreshToken = GenerateRefreshToken();
            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                UserId = user.Id,
                ExpiryDate = DateTime.UtcNow.AddDays(7)
            };
            
            _context.RefreshTokens.Add(refreshTokenEntity);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Đăng nhập thành công!",
                accessToken,
                refreshToken
            });
        }

        // 🔹 Đăng nhập bằng Google
        [HttpPost("login-google")]
        public async Task<IActionResult> LoginWithGoogle([FromBody] GoogleLoginModel model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                user = new User
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName,
                    Avatar = model.Avatar,
                    IsGoogleAccount = true,
                    EmailConfirmed = true // Google không cần xác thực email
                };
                await _userManager.CreateAsync(user);
            }

            // Trả về JWT Token
            var token = GenerateJwtToken(user);
            return Ok(new { message = "Đăng nhập Google thành công!", token });
        }

        // 🔹 Hàm tạo JWT Token
        private string GenerateJwtToken(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Id),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(Convert.ToDouble(_configuration["Jwt:ExpireMinutes"])),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        
        private async Task<bool> IsValidEmailAsync(string email)
        {
            try
            {
                var emailHost = email.Split('@').Last();
                var lookup = new LookupClient();
                var result = await lookup.QueryAsync(emailHost, QueryType.MX);

                bool isValid = result.Answers.MxRecords().Any();
                Console.WriteLine($"🔍 IsValidEmailAsync: {email} → {isValid}");
                return isValid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi kiểm tra MX Record: {ex.Message}");
                return false;
            }
        }
        

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] TokenModel model)
        {
            if (string.IsNullOrEmpty(model.RefreshToken))
                return BadRequest(new { message = "Refresh Token không hợp lệ!" });

            // Kiểm tra Refresh Token trong database
            var refreshTokenEntity = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == model.RefreshToken && rt.ExpiryDate > DateTime.UtcNow);
        
            if (refreshTokenEntity == null)
                return Unauthorized(new { message = "Refresh Token không hợp lệ hoặc đã hết hạn!" });

            // Lấy thông tin user
            var user = await _userManager.FindByIdAsync(refreshTokenEntity.UserId);
            if (user == null)
                return Unauthorized(new { message = "Người dùng không tồn tại!" });

            // Tạo Access Token mới
            var newAccessToken = GenerateJwtToken(user);

            return Ok(new
            {
                accessToken = newAccessToken,
                refreshToken = model.RefreshToken
            });
        }

        // Hàm tạo Refresh Token
        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
            }
            return Convert.ToBase64String(randomNumber);
        }

        [HttpGet("check-token/{token}")]
        public async Task<IActionResult> CheckToken(string token)
        {
            var refreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == token);
            
            if (refreshToken == null)
                return NotFound(new { message = "Token không tồn tại" });
            
            return Ok(new { 
                id = refreshToken.Id,
                userId = refreshToken.UserId,
                expiryDate = refreshToken.ExpiryDate
            });
        }

    }
}
