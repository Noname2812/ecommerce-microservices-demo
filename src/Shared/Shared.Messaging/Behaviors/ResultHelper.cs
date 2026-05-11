using Shared.Kernel.Primitives;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Shared.Messaging.Behaviors;

internal static class ResultHelper
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> _factories = new();

    internal static TResponse MakeFailure<TResponse>(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var factory = _factories.GetOrAdd(typeof(TResponse), static t =>
            {
                var inner = t.GetGenericArguments()[0];
                var method = typeof(Result)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
                    .MakeGenericMethod(inner);
                var param = Expression.Parameter(typeof(Error));
                var call = Expression.Call(method, param);
                return Expression.Lambda<Func<Error, object>>(
                    Expression.Convert(call, typeof(object)), param).Compile();
            });
            return (TResponse)factory(error);
        }

        throw new InvalidOperationException(
            $"Cannot create failure for {typeof(TResponse).Name} — not a Result type.");
    }
}
