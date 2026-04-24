namespace Shared.Kernel.Domain;

public interface IDateTracking
{
    DateTimeOffset CreatedDate { get; set; }
    DateTimeOffset ModifiedDate { get; set; }
}
