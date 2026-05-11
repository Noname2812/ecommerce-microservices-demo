using System.Data;
using Dapper;

namespace UrbanX.Catalog.Persistence.DependencyInjection;

internal sealed class StringArrayTypeHandler : SqlMapper.TypeHandler<string[]>
{
    public static readonly StringArrayTypeHandler Instance = new();

    public override void SetValue(IDbDataParameter parameter, string[]? value)
        => parameter.Value = value ?? Array.Empty<string>();

    public override string[] Parse(object value) =>
        value is string[] arr ? arr : [];
}
