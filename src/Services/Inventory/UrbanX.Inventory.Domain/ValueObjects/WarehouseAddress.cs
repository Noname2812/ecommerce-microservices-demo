namespace UrbanX.Inventory.Domain.ValueObjects;

public record WarehouseAddress(
    string? Street,
    string? Ward,
    string? District,
    string? City,
    string? Province,
    string? Country,
    string? ZipCode);
