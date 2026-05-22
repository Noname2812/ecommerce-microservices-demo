using MassTransit;
using System.Text.RegularExpressions;

namespace Shared.Messaging.Fomatters
{
    public class KebabCaseEntityNameFormatter : IEntityNameFormatter
    {
        public string FormatEntityName<T>()
        {
            // Shared.Contract.Messaging.Catalog.ProductCreatedV1
            // → product-created-v1
            var name = typeof(T).Name;
            return ToKebabCase(name);
        }

        private static string ToKebabCase(string name)
        {
            // ProductCreatedV1 → product-created-v1
            return Regex.Replace(name, "(?<!^)([A-Z])", "-$1").ToLower();
        }
    }
}
