using Shared.Kernel.Primitives;

namespace Shared.Cache;

public static class CacheErrors
{
    public static Error LockTimeout(string resource) =>
        new("Cache.LockTimeout", $"Failed to acquire distributed lock for '{resource}' within the allowed timeout.");
}
