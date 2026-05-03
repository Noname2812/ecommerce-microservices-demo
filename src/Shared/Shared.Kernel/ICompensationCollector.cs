namespace Shared.Kernel
{
    public interface ICompensationCollector
    {
        void Register(Func<CancellationToken, Task> compensation, string reason);
        IReadOnlyList<RegisteredCompensation> GetAll();

        void Clear();
    }

    public record RegisteredCompensation(
        Func<CancellationToken, Task> Action,
        string Reason
    );
}
