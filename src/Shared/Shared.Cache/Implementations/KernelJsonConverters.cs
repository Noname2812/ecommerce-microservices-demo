using Shared.Kernel.Primitives;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Cache.Implementations;

// ── Result<T> ───────────────────────────────────────────────────────────────

internal sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Result<>);

    public override JsonConverter? CreateConverter(Type t, JsonSerializerOptions _)
    {
        var valueType = t.GetGenericArguments()[0];
        return (JsonConverter?)Activator.CreateInstance(
            typeof(ResultJsonConverter<>).MakeGenericType(valueType));
    }
}

internal sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
{
    public override Result<T>? Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var isSuccess = root.GetProperty("isSuccess").GetBoolean();

        if (isSuccess)
        {
            var value = root.TryGetProperty("value", out var vp)
                ? JsonSerializer.Deserialize<T>(vp.GetRawText(), options)
                : default;
            return Result.Success(value!);
        }

        var ep = root.GetProperty("error");
        var code = ep.GetProperty("code").GetString() ?? string.Empty;
        var msg  = ep.GetProperty("message").GetString() ?? string.Empty;
        return Result.Failure<T>(new Error(code, msg));
    }

    public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isSuccess", value.IsSuccess);

        if (value.IsSuccess)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value.Value, options);
            writer.WriteNull("error");
        }
        else
        {
            writer.WriteNull("value");
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteString("code", value.Error.Code);
            writer.WriteString("message", value.Error.Message);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}

// ── PageResult<T> ────────────────────────────────────────────────────────────

internal sealed class PageResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(PageResult<>);

    public override JsonConverter? CreateConverter(Type t, JsonSerializerOptions _)
    {
        var itemType = t.GetGenericArguments()[0];
        return (JsonConverter?)Activator.CreateInstance(
            typeof(PageResultJsonConverter<>).MakeGenericType(itemType));
    }
}

internal sealed class PageResultJsonConverter<T> : JsonConverter<PageResult<T>>
{
    public override PageResult<T>? Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var items      = JsonSerializer.Deserialize<List<T>>(root.GetProperty("items").GetRawText(), options) ?? [];
        var pageIndex  = root.GetProperty("pageIndex").GetInt32();
        var pageSize   = root.GetProperty("pageSize").GetInt32();
        var totalCount = root.GetProperty("totalCount").GetInt32();

        return PageResult<T>.Create(items, pageIndex, pageSize, totalCount);
    }

    public override void Write(Utf8JsonWriter writer, PageResult<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("items");
        JsonSerializer.Serialize(writer, value.Items, options);
        writer.WriteNumber("pageIndex", value.PageIndex);
        writer.WriteNumber("pageSize", value.PageSize);
        writer.WriteNumber("totalCount", value.TotalCount);
        writer.WriteEndObject();
    }
}
