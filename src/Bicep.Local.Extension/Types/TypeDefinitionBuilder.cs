// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Azure.Bicep.Types;
using Azure.Bicep.Types.Concrete;
using Azure.Bicep.Types.Index;
using Azure.Bicep.Types.Serialization;
using Bicep.Local.Extension.Builder.Models;
using Bicep.Local.Extension.Types.Attributes;
using static Google.Protobuf.Reflection.GeneratedCodeInfo.Types;

namespace Bicep.Local.Extension.Types;

public class TypeDefinitionBuilder : ITypeDefinitionBuilder
{
    private readonly ITypeProvider typeProvider;
    private readonly ImmutableDictionary<Type, TypeBase> builtInTypes = new Dictionary<Type, TypeBase>
    {
        [typeof(string)] = new StringType(),
        [typeof(int)] = new IntegerType(),
        [typeof(bool)] = new BooleanType(),
        [typeof(NullReferenceType)] = new NullType(),
        [typeof(SecureStringReferenceType)] = new StringType(sensitive: true),
    }.ToImmutableDictionary();

    /// <summary>
    /// A placeholder type to represent null in nullable types.
    /// </summary>
    private record NullReferenceType();

    /// <summary>
    /// A placeholder type to represent a secure string.
    /// </summary>
    private record SecureStringReferenceType();

    private readonly Dictionary<Type, ITypeReference> typeCache;
    private readonly ExtensionInfo extensionInfo;
    private readonly TypeFactory factory;

    private const string typesJsonPath = "types.json";

    /// <summary>
    /// Provides functionality to generate Bicep resource type definitions from .NET types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="TypeDefinitionBuilder"/> inspects resource types provided by an <see cref="ITypeProvider"/>,
    /// analyzes their public properties and associated <see cref="TypePropertyAttribute"/> metadata,
    /// and produces a <see cref="TypeDefinition"/> containing serialized type and index metadata
    /// suitable for Bicep extension consumption.
    /// </para>
    /// <para>
    /// The builder supports primitive types (string, int, bool), arrays, nullable enums, and nested complex types.
    /// If a property type cannot be mapped to a supported Bicep type, a <see cref="NotImplementedException"/> is thrown.
    /// </para>
    /// </remarks>
    public TypeDefinitionBuilder(
        ExtensionInfo extensionInfo,
        ITypeProvider typeProvider)
    {
        this.extensionInfo = extensionInfo;
        this.factory = new([]);
        this.typeProvider = typeProvider;
        this.typeCache = [];
    }

    /// <summary>
    /// Generates Bicep resource type definitions based on the types provided by the <see cref="ITypeProvider"/>.
    /// This method inspects the resource types, their properties, and associated attributes to produce
    /// a <see cref="TypeDefinition"/> containing the serialized type and index metadata for use in Bicep extensions.
    /// </summary>
    /// <returns>
    /// A <see cref="TypeDefinition"/> object containing the JSON representations of the resource types and their index.
    /// </returns>
    /// <remarks>
    /// This method will throw a <see cref="NotImplementedException"/> if a property type is encountered that cannot be mapped
    /// to a supported Bicep type (e.g., unsupported primitives or collections).
    /// </remarks>
    public virtual TypeDefinition GenerateTypeDefinition()
    {
        var resourceTypes = typeProvider.GetResourceTypes()
            .Select(x => GenerateResource(x.type, x.attribute))
            .Select(x => x.Type as ResourceType)
            .OfType<ResourceType>()
            .ToDictionary(rt => rt.Name, rt => new CrossFileTypeReference(typesJsonPath, factory.GetIndex(rt)));

        var config = CreateCrossFileTypeReference(typeProvider.ConfigurationType);
        var fallback = CreateCrossFileTypeReference(typeProvider.FallbackType);

        var index = new TypeIndex(
                resources: resourceTypes,
                resourceFunctions: new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<CrossFileTypeReference>>>(),
                [],
                settings: new TypeSettings(name: extensionInfo.Name, version: extensionInfo.Version, isSingleton: extensionInfo.IsSingleton, configurationType: config),
                fallbackResourceType: fallback);

        return new(
            IndexFileContent: GetString(stream => TypeSerializer.SerializeIndex(stream, index)),
            TypeFileContents: new Dictionary<string, string>
            {
                [typesJsonPath] = GetString(stream => TypeSerializer.Serialize(stream, factory.GetTypes())),
            }.ToImmutableDictionary());
    }

    private CrossFileTypeReference? CreateCrossFileTypeReference(Type? type)
    {
        if (type is not null)
        {
            var configReference = GenerateForRecord(type);
            return new CrossFileTypeReference(typesJsonPath, factory.GetIndex(configReference.Type));
        }

        return null;
    }

    private ITypeReference GenerateResource(Type type, ResourceTypeAttribute attribute)
        => AddType(type, new ResourceType(
            name: attribute.FullName,
            body: GenerateForType(type, null) ?? throw new NotImplementedException($"Unsupported resource body type: '{type}'"),
            functions: null,
            writableScopes_in: ScopeType.All,
            readableScopes_in: ScopeType.All));

    private ITypeReference AddType(Type type, TypeBase bicepType, bool doNotCache = false)
    {
        var result = factory.GetReference(factory.Create(() => bicepType));
        if (!doNotCache)
        {
            typeCache[type] = result;
        }
        return result;
    }

    private ITypeReference? GenerateForType(Type type, TypePropertyAttribute? annotation)
    {
        if (type == typeof(string) && annotation?.IsSecure == true)
        {
            // Use a placeholder type to differentiate against non-secure strings.
            type = typeof(SecureStringReferenceType);
        }

        if (typeCache.TryGetValue(type, out var cachedValue))
        {
            return cachedValue;
        }

        if (builtInTypes.TryGetValue(type, out var bicepType))
        {
            return AddType(type, bicepType);
        }

        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
            type.GetGenericArguments()[0] is { } innerType)
        {
            if (GenerateForType(typeof(NullReferenceType), null) is { } nullType &&
                GenerateForType(innerType, annotation) is { } innerBicepType)
            {
                return AddType(type, new UnionType([nullType, innerBicepType]));
            }

            return null;
        }

        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            Type? keyType = null;
            Type? valueType = null;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                keyType = type.GetGenericArguments()[0];
                valueType = type.GetGenericArguments()[1];
            }
            else if (type.GetInterfaces()
                .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDictionary<,>)) is { } dictType)
            {
                keyType = dictType.GetGenericArguments()[0];
                valueType = dictType.GetGenericArguments()[1];
            }

            if (keyType is null ||
                valueType is null ||
                keyType != typeof(string) ||
                GenerateForType(valueType, null) is not { } valueTypeReference)
            {
                throw new NotImplementedException($"Unsupported dictionary type: '{type}'");
            }

            return AddType(type, new ObjectType(
                $"Dictionary<string, {valueType.Name}>",
                properties: ImmutableDictionary<string, ObjectTypeProperty>.Empty,
                additionalProperties: valueTypeReference));
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            Type? elementType = null;

            if (type.IsArray)
            {
                elementType = type.GetElementType();
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = type.GetGenericArguments()[0];
            }
            else if (type.GetInterfaces()
                .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>)) is { } enumerableType)
            {
                elementType = enumerableType.GetGenericArguments()[0];
            }

            if (elementType is null)
            {
                throw new NotImplementedException($"Unsupported collection type: '{type}'");
            }

            if (GenerateForType(elementType, annotation) is not { } elementTypeReference)
            {
                throw new NotImplementedException($"Unsupported element type: '{elementType}'");
            }

            return AddType(type, new ArrayType(elementTypeReference));
        }

        if (type.IsClass && type.GetCustomAttribute<JsonPolymorphicAttribute>() is { } polymorphicAttribute
            && type.GetCustomAttributes<JsonDerivedTypeAttribute>() is { } derivedTypesAttribute)
        {
            var discriminatorType = GenerateForDiscriminatedType(type, polymorphicAttribute, derivedTypesAttribute);
            var discriminatorTypeReference = AddOrGetReference(discriminatorType);
            typeCache[type] = discriminatorTypeReference;

            return discriminatorTypeReference;
        }

        if (type.IsClass)
        {
            return GenerateForRecord(type);
        }

        if (type.IsEnum)
        {
            var enumMembers = type.GetEnumNames()
                .Select(x => factory.Create(() => new StringLiteralType(x)))
                .Select(x => factory.GetReference(x))
                .ToImmutableArray();

            return AddType(type, new UnionType(enumMembers));
        }

        return null;
    }

    private ITypeReference GenerateForRecord(Type type, bool cacheInTypeCache = true)
    {
        var typeProperties = new Dictionary<string, ObjectTypeProperty>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var annotation = property.GetCustomAttributes<TypePropertyAttribute>(true).FirstOrDefault();
            var propertyType = property.PropertyType;

            if (GenerateForType(propertyType, annotation) is not { } typeReference)
            {
                throw new NotImplementedException($"Property '{property.Name}' references unsupported type: '{propertyType}'");
            }

            typeProperties[CamelCase(property.Name)] = new ObjectTypeProperty(
                typeReference,
                annotation?.Flags ?? ObjectTypePropertyFlags.None,
                annotation?.Description);
        }

        return AddType(type, new ObjectType(
            $"{type.Name}",
            typeProperties,
            null), doNotCache: !cacheInTypeCache);
    }

    private string GetString(Action<Stream> streamWriteFunc)
    {
        using var memoryStream = new MemoryStream();
        streamWriteFunc(memoryStream);

        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    private TypeBase GenerateForDiscriminatedType(Type type,
        JsonPolymorphicAttribute polymorphicAttribute, IEnumerable<JsonDerivedTypeAttribute> derivedTypeAttributes)
    {
        // Build the base object shape without caching it under the polymorphic CLR type.
        var baseProperties = GenerateForRecord(type, cacheInTypeCache: false).Type as ObjectType;
        var discriminatorName = polymorphicAttribute.TypeDiscriminatorPropertyName;
        if (discriminatorName is null)
        {
            throw new InvalidOperationException($"Discriminator name for type {type} cannot be null.");
        }
        var childTypesDictionary = new Dictionary<string, ITypeReference>();

        foreach (var derivedType in derivedTypeAttributes)
        {
            string? typeDiscriminator = derivedType.TypeDiscriminator?.ToString();
            if(type.GetProperties().Any(p => p.Name == discriminatorName))
            {
                throw new InvalidOperationException($"The discriminator property '{discriminatorName}' cannot be defined in the base type '{type}'. It is reserved for the polymorphic type system.");
            }
            if (typeDiscriminator is null)
            {
                throw new ArgumentNullException(nameof(derivedType.TypeDiscriminator),
                    "The type discriminator property from JsonDerivedTypeAttribute cannot be null.");
            }
            typeCache.TryAdd(derivedType.DerivedType,
                GenerateForRecord(derivedType.DerivedType));
            typeCache.TryGetValue(derivedType.DerivedType, out var discriminatedTypeProperties);
            if (discriminatedTypeProperties is null)
            {
                throw new InvalidOperationException($"Discriminated type {derivedType.DerivedType} cannot be null.");
            }
            var concreteDiscriminatedTypeProperties = (ObjectType)discriminatedTypeProperties.Type;
            var discriminatorTypeReference =
                AddOrGetReference(new StringLiteralType(typeDiscriminator));
            var newProperties = concreteDiscriminatedTypeProperties.Properties
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            foreach (var basePropertyName in baseProperties!.Properties.Keys)
            {
                if (!string.Equals(basePropertyName, discriminatorName, StringComparison.Ordinal))
                {
                    newProperties.Remove(basePropertyName);
                }
            }

            newProperties[discriminatorName] = new ObjectTypeProperty(
                discriminatorTypeReference,
                ObjectTypePropertyFlags.Required,
                "The discriminator for derived types.");

            var newObjectType = new ObjectType(concreteDiscriminatedTypeProperties.Name,
                newProperties
                    .ToImmutableDictionary(),
                concreteDiscriminatedTypeProperties.AdditionalProperties);

            childTypesDictionary.Add(derivedType.DerivedType.Name, AddOrGetReference(newObjectType));
        }
        return new DiscriminatedObjectType(
            type.Name,
            discriminatorName,
            baseProperties!.Properties,
            childTypesDictionary);
    }

    private ITypeReference AddOrGetReference(TypeBase type)
    {
        try
        {
            var typeBase = factory.Create(() => type);
            return factory.GetReference(typeBase);
        }
        catch (ArgumentException)
        {
            return factory.GetReference(type);
        }
    }

    private static string CamelCase(string input)
        => $"{input[..1].ToLowerInvariant()}{input[1..]}";
}
