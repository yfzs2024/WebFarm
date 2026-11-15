namespace WebFarm.Models;
public class Crop
{
    public int Id { get; set; }
    public string CropName { get; set; } = string.Empty;
    public int SeedCost { get; set; }
    public int MatureTime { get; set; } = 1;
}