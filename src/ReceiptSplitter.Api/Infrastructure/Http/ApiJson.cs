using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReceiptSplitter.Api.Infrastructure.Http;

/// <summary>One set of JSON rules for request bodies, responses and stored bills.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        // A missing required field or a null where none is allowed is a 400, not a half-built object.
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        // Lets the "op" discriminator sit anywhere in a PATCH body.
        options.AllowOutOfOrderMetadataProperties = true;
        return options;
    }
}
