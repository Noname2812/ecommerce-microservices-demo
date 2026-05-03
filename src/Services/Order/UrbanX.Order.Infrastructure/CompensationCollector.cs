using Shared.Kernel;

namespace UrbanX.Order.Infrastructure
{
    public class CompensationCollector : ICompensationCollector
    {
        private readonly List<RegisteredCompensation> _compensations = [];

        public void Register(
            Func<CancellationToken, Task> compensation,
            string reason)
        {
            _compensations.Add(new RegisteredCompensation(compensation, reason));
        }

        public IReadOnlyList<RegisteredCompensation> GetAll()
            => _compensations.AsReadOnly();

        public void Clear()
        {
            _compensations.Clear();
        }
    }
}
