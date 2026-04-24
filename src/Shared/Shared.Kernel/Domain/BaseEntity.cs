namespace Shared.Kernel.Domain;

public abstract class BaseEntity<TKey>
{
    public TKey Id { get; set; } = default!;
}
