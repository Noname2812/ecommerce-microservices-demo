namespace Shared.Application
{
    public interface ICompensationCollector
    {
        void Register(Func<CancellationToken, Task> compensation, string reason);
        IReadOnlyList<RegisteredCompensation> GetAll();
    }

    public record RegisteredCompensation(
        Func<CancellationToken, Task> Action,
        string Reason
    );
}
