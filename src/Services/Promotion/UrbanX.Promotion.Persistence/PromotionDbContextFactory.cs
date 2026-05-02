using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrbanX.Promotion.Persistence;

public sealed class PromotionDbContextFactory : IDesignTimeDbContextFactory<PromotionDbContext>
{
    public PromotionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__promotiondb")
            ?? "Host=localhost;Port=5432;Database=urbanx_promotion;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PromotionDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PromotionDbContext(options);
    }
}
