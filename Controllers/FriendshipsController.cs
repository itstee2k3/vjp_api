using vjp_api.Data;
using vjp_api.Dtos;
using vjp_api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace vjp_api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[Authorize]
[ApiController]
[Route("api/friendships")]
public class FriendshipsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<ChatHub> _hubContext;

    public FriendshipsController(ApplicationDbContext context, IHubContext<ChatHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost("request")]
    public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestDto dto)
    {
        var requesterId = GetUserId();
        var receiverId = dto.ReceiverId;

        // Validate request
        if (requesterId == receiverId)
        {
            return BadRequest("Cannot send a friend request to yourself.");
        }

        var receiverExists = await _context.Users.AnyAsync(u => u.Id == receiverId);
        if (!receiverExists)
        {
            return NotFound("Receiver user not found.");
        }

        // Check if friendship already exists or request is pending
        var existingFriendship = await _context.Friendships
            .FirstOrDefaultAsync(f =>
                (f.UserRequesterId == requesterId && f.UserReceiverId == receiverId) ||
                (f.UserRequesterId == receiverId && f.UserReceiverId == requesterId));

        if (existingFriendship != null)
        {
            if (existingFriendship.Status == FriendshipStatus.Accepted)
            {
                return BadRequest("You are already friends with this user.");
            }
            if (existingFriendship.Status == FriendshipStatus.Pending)
            {
                // Check who sent the pending request
                if (existingFriendship.UserRequesterId == requesterId) {
                     return BadRequest("Friend request already sent.");
                } else {
                     return BadRequest("This user has already sent you a friend request. Please accept or reject it.");
                }
            }
             // If Rejected or Blocked, potentially allow sending a new request depending on logic
             // For now, let's treat any existing record (other than accepted/pending) as preventing a new request
             // Or simply remove rejected/cancelled requests? Let's assume removing for now.
             // If the existing status requires specific handling (e.g. was rejected), add logic here.
        }


        // Create new friendship request
        var newFriendship = new Friendship
        {
            UserRequesterId = requesterId,
            UserReceiverId = receiverId,
            Status = FriendshipStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        _context.Friendships.Add(newFriendship);
        await _context.SaveChangesAsync();

        // Send notification to the receiver
        var requester = await _context.Users.FindAsync(requesterId);
        if (requester != null) 
        {
            var notificationData = new 
            {
                type = "FriendRequestReceived",
                friendshipId = newFriendship.Id, // Send the ID of the new friendship record
                requesterId = requesterId,
                requesterFullName = requester.FullName ?? "N/A",
                requestedAt = newFriendship.RequestedAt
            };
            // Use User ID for SignalR Group
            await _hubContext.Clients.Group(receiverId).SendAsync("ReceiveFriendNotification", notificationData);
        }

        return Ok(new { message = "Friend request sent successfully." });
    }

    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<FriendRequestDto>>> GetPendingRequests()
    {
        var receiverId = GetUserId();
        var requests = await _context.Friendships
            .Include(f => f.Requester)
            .Where(f => f.UserReceiverId == receiverId && f.Status == FriendshipStatus.Pending)
            .Select(f => new FriendRequestDto
            {
                FriendshipId = f.Id,
                RequesterId = f.UserRequesterId,
                RequesterFullName = f.Requester != null ? f.Requester.FullName ?? "N/A" : "N/A",
                RequestedAt = f.RequestedAt
            })
            .ToListAsync();
        return Ok(requests);
    }

    [HttpPost("{friendshipId}/accept")]
    public async Task<IActionResult> AcceptRequest(int friendshipId)
    {
        var receiverId = GetUserId();
        var result = await _context.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.UserReceiverId == receiverId);
         if (result == null) return NotFound("Friendship not found.");
        if (result.Status != FriendshipStatus.Pending) return BadRequest("Friendship status is not pending.");

        result.Status = FriendshipStatus.Accepted;
        await _context.SaveChangesAsync();

        // Send notification to both users
        var requester = await _context.Users.FindAsync(result.UserRequesterId);
        var receiver = await _context.Users.FindAsync(result.UserReceiverId);

        if (requester != null && receiver != null) 
        {
            var notificationData = new 
            {
                type = "FriendRequestAccepted",
                friendshipId = result.Id,
                friendId = requester.Id, // For the receiver's notification
                friendFullName = requester.FullName ?? "N/A", // For the receiver's notification
                respondedAt = result.RespondedAt
            };
            // Notify receiver (who accepted)
            await _hubContext.Clients.Group(receiver.Id).SendAsync("ReceiveFriendNotification", notificationData);

            // Notify requester
            notificationData = new 
            {
                type = "FriendRequestAccepted",
                friendshipId = result.Id,
                friendId = receiver.Id, // For the requester's notification
                friendFullName = receiver.FullName ?? "N/A", // For the requester's notification
                respondedAt = result.RespondedAt
            };
            await _hubContext.Clients.Group(requester.Id).SendAsync("ReceiveFriendNotification", notificationData);
        }

        return Ok(new { message = "Friend request accepted." });
    }

    [HttpPost("{friendshipId}/reject")]
    public async Task<IActionResult> RejectRequest(int friendshipId)
    {
         var userId = GetUserId(); // Người dùng hiện tại có thể là người nhận hoặc người gửi muốn hủy
        var result = await _context.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.UserReceiverId == userId);
         if (result == null) return NotFound("Friendship not found.");
        if (result.Status != FriendshipStatus.Pending) return BadRequest("Friendship status is not pending.");

        result.Status = FriendshipStatus.Rejected;
        await _context.SaveChangesAsync();

        // Send notification to the *other* party
        bool wasRejected = result.UserReceiverId == userId;
        string otherPartyId = wasRejected ? result.UserRequesterId : result.UserReceiverId;
        string notificationType = wasRejected ? "FriendRequestRejected" : "FriendRequestCancelled";

        var notificationData = new 
        {
            type = notificationType,
            friendshipId = result.Id, // ID of the removed request
            // Send the ID of the user who initiated the action
            actorId = userId 
        };
        await _hubContext.Clients.Group(otherPartyId).SendAsync("ReceiveFriendNotification", notificationData);

        // Determine the message based on who initiated the action
        string message = wasRejected 
            ? "Friend request rejected." 
            : "Friend request cancelled.";

        return Ok(new { message = message });
    }

     // Hoặc dùng DELETE
    [HttpDelete("{friendshipId}")]
    public async Task<IActionResult> DeleteFriendship(int friendshipId)
    {
        var userId = GetUserId();
        var result = await _context.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.UserReceiverId == userId);
         if (result == null) return NotFound("Friendship not found.");
        if (result.Status != FriendshipStatus.Accepted) return BadRequest("Friendship status is not accepted.");

        _context.Friendships.Remove(result);
        await _context.SaveChangesAsync();

        // TODO: Optionally send a notification via SignalR

        return NoContent(); // 204 No Content là phù hợp cho DELETE thành công
    }

    [HttpGet("friends")]
    public async Task<ActionResult<IEnumerable<FriendDto>>> GetFriends()
    {
        var userId = GetUserId();
        var friends = await _context.Friendships
            .Include(f => f.Requester) // Include Requester to access FullName
            .Include(f => f.Receiver) // Include Receiver to access FullName
            .Where(f => f.Status == FriendshipStatus.Accepted && 
                       (f.UserRequesterId == userId || f.UserReceiverId == userId))
            .Select(f => new FriendDto
            {
                FriendshipId = f.Id,
                FriendId = f.UserRequesterId == userId ? f.UserReceiverId : f.UserRequesterId,
                FriendFullName = f.UserRequesterId == userId 
                    ? (f.Receiver != null ? f.Receiver.FullName ?? "N/A" : "N/A") 
                    : (f.Requester != null ? f.Requester.FullName ?? "N/A" : "N/A")
            })
            .ToListAsync();
        return Ok(friends);
    }

    // Có thể cần thêm endpoint để hủy kết bạn (Unfriend)
    [HttpPost("unfriend/{friendId}")]
    public async Task<IActionResult> Unfriend(string friendId)
    {
        var userId = GetUserId();
        var result = await _context.Friendships
            .FirstOrDefaultAsync(f => (f.UserRequesterId == userId && f.UserReceiverId == friendId) ||
                                       (f.UserRequesterId == friendId && f.UserReceiverId == userId));
         if (result == null) return NotFound("Friendship not found.");
        if (result.Status != FriendshipStatus.Accepted) return BadRequest("Friendship status is not accepted.");

        _context.Friendships.Remove(result);
        await _context.SaveChangesAsync();

        // Send notification to the user who was unfriended
        var notificationData = new 
        {
            type = "FriendshipRemoved",
            friendshipId = result.Id, // ID of the removed friendship
            // ID of the user who initiated the unfriend action
            actorId = userId 
        };
        await _hubContext.Clients.Group(friendId).SendAsync("ReceiveFriendNotification", notificationData);

        return NoContent(); // 204 No Content is appropriate for successful DELETE
    }
}