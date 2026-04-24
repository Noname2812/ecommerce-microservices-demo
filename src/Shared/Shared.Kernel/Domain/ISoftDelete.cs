namespace Shared.Kernel.Domain;

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedDate { get; set; }

    void Undo()
    {
        IsDeleted = false;
        DeletedDate = null;
    }
}
