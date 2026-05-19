using System.ComponentModel.DataAnnotations;

namespace UrbanX.Order.Application.DependencyInjection.Options;

public sealed class OrderTicketCacheOptions
{
    public const string SectionName = "Order:TicketCache";

    [Range(1, 3600)]
    public int TerminalTtlSeconds { get; init; } = 300;

    [Range(1, 60)]
    public int NonTerminalTtlSeconds { get; init; } = 2;
}
