using System.ComponentModel.DataAnnotations;

namespace vjp_api.Models;

public class RefreshToken
{
    [Key]
    public int Id { get; set; }
    public string Token { get; set; }
    public string UserId { get; set; }
    public DateTime ExpiryDate { get; set; }
        
    public virtual User User { get; set; }
}