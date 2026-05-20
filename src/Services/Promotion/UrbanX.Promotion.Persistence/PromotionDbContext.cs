using MassTransit;
using Microsoft.EntityFrameworkCore;
using UrbanX.Promotion.Domain.Models;

namespace UrbanX.Promotion.Persistence;

public sealed class PromotionDbContext(DbContextOptions<PromotionDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Models.Promotion> Promotions => Set<Domain.Models.Promotion>();
    public DbSet<VoucherCode> VoucherCodes => Set<VoucherCode>();
    public DbSet<FlashSaleItem> FlashSaleItems => Set<FlashSaleItem>();
    public DbSet<PromotionUsage> PromotionUsages => Set<PromotionUsage>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponClaim> CouponClaims => Set<CouponClaim>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();

        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
