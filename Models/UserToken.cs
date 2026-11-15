namespace WebFarm.Models;
public class UserToken
{
    public int Id { get; set; }
    public DateTime ExpiresAt  { get; set; }
    public string Token { get; set; } = string.Empty;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}