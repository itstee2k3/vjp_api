// ChatHub.cs

using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using vjp_api.Data;

public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly ApplicationDbContext _context;

    public ChatHub(ApplicationDbContext context, ILogger<ChatHub> logger)
    {
        _context = context;
        _logger = logger;
    }
    
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User.Identity.Name;
        if (!string.IsNullOrEmpty(userId))
        {
            // Thêm user vào group riêng để nhận tin nhắn cá nhân
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            _logger.LogInformation($"User {userId} connected with connection ID {Context.ConnectionId}");

            // Đăng ký user vào các nhóm chat mà họ là thành viên
            var userGroups = await _context.UserGroups
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.GroupChatId)
                .ToListAsync();

            foreach (var groupId in userGroups)
            {
                var groupName = $"group_{groupId}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                _logger.LogInformation($"User {userId} added to group {groupName}");
            }
        }

        await base.OnConnectedAsync();
    }
    
    public async Task JoinRoom(string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        _logger.LogInformation($"User {userId} joined room with connection ID {Context.ConnectionId}");
    }
    
    // ChatHub.cs
    public async Task SendMessage(object messageData)
    {
        try
        {
            // Chuyển đổi messageData thành đối tượng có thể truy cập
            var message = JObject.FromObject(messageData);
    
            _logger.LogInformation($"Received message data: {message}");
    
            // Kiểm tra xem messageData có phải là đối tượng không
            if (message.Type != JTokenType.Object)
            {
                _logger.LogError($"Invalid message data: not an object. Type: {message.Type}");
                return;
            }
    
            string senderId = message["senderId"]?.ToString();
            string receiverId = message["receiverId"]?.ToString();
            string content = message["content"]?.ToString();
            string type = message["type"]?.ToString() ?? "text";
            string imageUrl = message["imageUrl"]?.ToString();
    
            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId))
            {
                _logger.LogError($"SendMessage: senderId or receiverId is null or empty. Message data: {message}");
                return;
            }
    
            if (string.IsNullOrEmpty(content))
            {
                _logger.LogError($"SendMessage: content is null or empty. Message data: {message}");
                return;
            }
    
            _logger.LogInformation($"Sending message from {senderId} to {receiverId}: {content}, Type: {type}");
    
            // Gửi tin nhắn đến người nhận
            await Clients.Group(receiverId).SendAsync("ReceiveMessage", messageData);
            _logger.LogInformation($"Message sent to receiver group: {receiverId}");
    
            // Gửi tin nhắn đến người gửi (để đồng bộ trên tất cả thiết bị)
            await Clients.Group(senderId).SendAsync("ReceiveMessage", messageData);
            _logger.LogInformation($"Message sent to sender group: {senderId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error sending message: {ex.Message}");
            _logger.LogError($"Stack trace: {ex.StackTrace}");
        }
    }
    
    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var userId = Context.User.Identity.Name;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
            _logger.LogInformation($"User {userId} disconnected");

            // Loại bỏ khỏi tất cả nhóm chat
            var userGroups = await _context.UserGroups
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.GroupChatId)
                .ToListAsync();

            foreach (var groupId in userGroups)
            {
                var groupName = $"group_{groupId}";
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
    
    public async Task SendTypingStatus(string senderId, string receiverId, bool isTyping)
    {
        try
        {
            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId))
            {
                _logger.LogError("SendTypingStatus: senderId or receiverId is null or empty");
                return;
            }

            _logger.LogInformation($"User {senderId} is {(isTyping ? "typing" : "not typing")} to {receiverId}");

            // Gửi trạng thái đang nhập đến người nhận
            await Clients.Group(receiverId).SendAsync("ReceiveTypingStatus", senderId, isTyping);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error sending typing status: {ex.Message}");
        }
    }
    
    // Phương thức cho client gọi để gửi tin nhắn vào nhóm
    public async Task SendMessageToGroup(int groupId, string message)
    {
        var userId = Context.User.Identity.Name;
        if (string.IsNullOrEmpty(userId)) return;

        // Kiểm tra xem người dùng có phải là thành viên của nhóm không
        var isMember = await _context.UserGroups
            .AnyAsync(ug => ug.GroupChatId == groupId && ug.UserId == userId);

        if (!isMember) return;

        // Gửi tin nhắn đến tất cả người trong nhóm
        string groupName = $"group_{groupId}";
        await Clients.Group(groupName).SendAsync("ReceiveGroupMessage", new
        {
            senderId = userId,
            groupId = groupId,
            content = message,
            sentAt = DateTime.Now
        });
    }
}