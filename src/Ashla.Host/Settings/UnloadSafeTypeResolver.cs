using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Ashla.Host.Settings;

internal sealed class UnloadSafeTypeResolver(Func<Type, bool>? isPluginType = null) : IJsonTypeInfoResolver
{
    private static readonly DefaultJsonTypeInfoResolver Default = new();
    private readonly Func<Type, bool> _isPluginType = isPluginType ?? ComesFromCollectibleAssembly;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        if (!_isPluginType(type)) return Default.GetTypeInfo(type, options);
        if (type.IsEnum || typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            return CollectionOrEnum(type, options);
        return ObjectContract(type, options);
    }

    private static JsonTypeInfo ObjectContract(Type type, JsonSerializerOptions options)
    {
        var info = JsonTypeInfo.CreateJsonTypeInfo(type, options);
        if (type.GetConstructor(Type.EmptyTypes) is { } constructor)
            info.CreateObject = () => constructor.Invoke(null);

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true }) continue;
            if (property.IsDefined(typeof(JsonIgnoreAttribute))) continue;
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                       ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
            var json = info.CreateJsonPropertyInfo(property.PropertyType, name);
            json.Get = property.GetValue;
            if (property.SetMethod is { IsPublic: true }) json.Set = property.SetValue;
            info.Properties.Add(json);
        }
        return info;
    }

    private static JsonTypeInfo CollectionOrEnum(Type type, JsonSerializerOptions options)
    {
        var info = Default.GetTypeInfo(type, options);
        if (info.Kind != JsonTypeInfoKind.None && type.GetConstructor(Type.EmptyTypes) is { } constructor)
            info.CreateObject = () => constructor.Invoke(null);
        return info;
    }

    internal static bool ComesFromCollectibleAssembly(Type type)
    {
        if (type.Assembly.IsCollectible) return true;
        if (type.HasElementType && ComesFromCollectibleAssembly(type.GetElementType()!)) return true;
        return type.IsGenericType && type.GetGenericArguments().Any(ComesFromCollectibleAssembly);
    }
}
