using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Shared.Messaging.Behaviors;

internal static class TemplateResolver
{
    private static readonly Regex _placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

    // Key = (RequestType, propertyName) — bounded by the number of distinct request types,
    // so ConcurrentDictionary growth is safe here.
    private static readonly ConcurrentDictionary<(Type, string), Func<object, string?>> _getters = new();

    internal static string Resolve<TRequest>(string template, TRequest request)
        where TRequest : notnull
    {
        return _placeholder.Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            var getter = _getters.GetOrAdd((typeof(TRequest), name), static key =>
            {
                var (type, propName) = key;
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop is null) return _ => null;

                // Compile: (object obj) => ((TRequest)obj).Property.ToString()
                // Calls the type's own ToString() override (e.g. Guid, int) — avoids boxing.
                var param = Expression.Parameter(typeof(object));
                var cast = Expression.Convert(param, type);
                var access = Expression.Property(cast, prop);
                var toStringMethod = prop.PropertyType.GetMethod(nameof(ToString), Type.EmptyTypes)
                    ?? typeof(object).GetMethod(nameof(ToString))!;
                var call = Expression.Call(access, toStringMethod);
                return Expression.Lambda<Func<object, string?>>(call, param).Compile();
            });
            return getter(request) ?? m.Value;
        });
    }
}
