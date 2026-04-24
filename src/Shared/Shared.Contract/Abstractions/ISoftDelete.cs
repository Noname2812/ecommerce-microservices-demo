namespace Shared.Contract.Abstractions
{
    public interface ISoftDelete
    {
        public bool IsDeleted { get; set; }
        DateTimeOffset? DeletedDate { get; set; }
        public void Undo()
        {
            IsDeleted = false;
            DeletedDate = null;
        }
    }
}
