namespace Shared.Outbox.Abstractions
{
    /// <summary>
    /// Appends compensation integration payloads to <see cref="CompensationOutboxMessage"/> within the same EF transaction as aggregate changes.
    /// Does not call <c>SaveChanges</c>.
    /// </summary>
    public interface ICompensationOutboxWriter
    {
        /// <summary>Uses <see cref="object.GetType"/>'s assembly-qualified name as the stored type.</summary>
        Task AddAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// <paramref name="type"/> must be resolvable via <see cref="System.Type.GetType(string)"/> at relay time (assembly-qualified recommended).
        /// </summary>
        Task AddAsync(string type, object payload, CancellationToken cancellationToken = default);
    }
}
