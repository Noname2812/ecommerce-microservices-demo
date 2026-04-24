namespace Shared.Contract.Abstractions
{
    public interface IUserTracking
    {
        Guid CreatedBy { get; set; }
        Guid ModifiedBy { get; set; }
    }
}
