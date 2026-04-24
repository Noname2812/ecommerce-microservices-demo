namespace Shared.Kernel.Domain;

public interface IUserTracking
{
    Guid CreatedBy { get; set; }
    Guid ModifiedBy { get; set; }
}
