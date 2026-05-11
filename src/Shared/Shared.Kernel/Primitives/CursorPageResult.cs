namespace Shared.Kernel.Primitives;

public sealed class CursorPageResult<T>
{
    public List<T> Items { get; }
    public string? NextCursor { get; }
    public bool HasMore => NextCursor is not null;

    private CursorPageResult(List<T> items, string? nextCursor)
    {
        Items = items;
        NextCursor = nextCursor;
    }

    public static CursorPageResult<T> Create(List<T> items, string? nextCursor)
        => new(items, nextCursor);
}
