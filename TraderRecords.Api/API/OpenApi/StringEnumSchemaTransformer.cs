using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace TraderRecords.Api.API.OpenApi;

/// <summary>
/// The API serialises enums as names, but the default OpenAPI generator can
/// describe a response-only enum as an integer. Keep the document aligned with
/// the public JSON contract.
/// </summary>
public sealed class StringEnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var enumType = context.JsonTypeInfo.Type;

        if (!enumType.IsEnum)
        {
            return Task.CompletedTask;
        }

        schema.Type = "string";
        schema.Format = null;
        schema.Enum = Enum.GetNames(enumType)
            .Select(name => (IOpenApiAny)new OpenApiString(name))
            .ToList();

        return Task.CompletedTask;
    }
}
