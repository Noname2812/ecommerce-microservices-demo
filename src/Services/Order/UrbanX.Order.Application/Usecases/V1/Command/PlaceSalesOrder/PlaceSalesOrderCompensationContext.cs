namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

public sealed class PlaceSalesOrderCompensationContext
{
    public string? SaleQuotaKey { get; set; }
    public Guid? SaleCampaignId { get; set; }
    public Guid? SaleUserId { get; set; }
    public int SaleReservedQty { get; set; }

    public bool HasSaleAllocation =>
        SaleQuotaKey is not null && SaleCampaignId.HasValue;
}
