namespace WebFarm.Models;
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int Cash { get; set; }
    // Relation Property
    public List<FarmLand> FarmLands { get; set; } = [];
}