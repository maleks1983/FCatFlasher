namespace FCatFlasher.Models;

public sealed class ProductionBoxState
{
    public int BoxNumber { get; set; } = 1;
    public int BoxQuantity { get; set; }
    public string FirmwareName { get; set; } = string.Empty;
    public List<string> SerialNumbers { get; set; } = new();
}
