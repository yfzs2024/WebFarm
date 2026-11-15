namespace WebFarm.Models;
public class FarmLand
{
    public int Id { get; set; }
    public int Index { get; set; }
    public int Scale { get; set; } = 1;
    public DateTime? PlantedDate { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int? CropId { get; set; }
    public Crop? Crop { get; set; }
}