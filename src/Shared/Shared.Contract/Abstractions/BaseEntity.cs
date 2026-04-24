namespace Shared.Contract.Abstractions
{
    public abstract class BaseEntity<TKey>
    {
        public TKey Id { get; set; }
    }
}
