using Shared.Kernel.Primitives;

namespace Shared.Cache;

public static class CacheErrors
{
    public static Error LockTimeout(string resource) =>
        new("Cache.LockTimeout", $"Failed to acquire distributed lock for '{resource}' within the allowed timeout.");

    public static Error LockUnavailable(string resource) =>
        new("Cache.LockUnavailable", $"Distributed lock for '{resource}' is unavailable — cache service is down.");
}
