// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Bicep.Types.Concrete;
using Azure.Bicep.Types.Index;
using Bicep.Core.Registry.Catalog.Implementation.PrivateRegistries;
using Bicep.Core.UnitTests.Mock;
using Bicep.Local.Extension.Types;
using Bicep.Local.Extension.Types.Attributes;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static Microsoft.WindowsAzure.ResourceStack.Common.Utilities.FastActivator;

namespace Bicep.Local.Extension.UnitTests.TypesTests;

[TestClass]
public class TypeDefinitionBuilderTests
{
    private record TestUnsupportedProperty(DateTime When);

    private record SimpleResource(string Name = "", string AnotherString = "");

    private static TypeSettings CreateTypeSettings() =>
        new("TestSettings", "2025-01-01", true, new Azure.Bicep.Types.CrossFileTypeReference("index.json", 0));

    private TypeFactory CreateTypeFactory() => new([]);

    #region Constructor Tests

    [TestMethod]
    public void Constructor_Throws_On_Empty_TypeToTypeBaseMap()
    {
        var settings = CreateTypeSettings();
        var typeFactory = CreateTypeFactory();
        var typeProvider = StrictMock.Of<ITypeProvider>().Object;
        var emptyMap = new Dictionary<Type, Func<TypeBase>>();

        Action act = () => new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, null, typeFactory, typeProvider, emptyMap);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Constructor_Succeeds_With_Valid_Arguments()
    {
        var factory = new TypeFactory([]);
        var typeProvider = new Mock<ITypeProvider>(MockBehavior.Strict).Object;
        var map = ImmutableDictionary<Type, Func<TypeBase>>.Empty.Add(typeof(string), () => new StringType());

        var generator = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, null, factory, typeProvider, map);

        generator.Should().NotBeNull();
    }

    #endregion Constructor Tests


    [TestMethod]
    public void GenerateTypeDefinition_Returns_Empty_When_TypeProvider_Has_No_Types()
    {
        var settings = CreateTypeSettings();
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([]);

        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, null, factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();
        result.IndexFileContent.Should().NotBeNullOrEmpty();
        result.TypeFileContents.Values.Single().Should().NotBeNullOrEmpty();
        result.TypeFileContents.Values.Single().Should().Contain("[]", because: "the types JSON should be and empty array '[]' when no resource types are generated");
    }

    [TestMethod]
    public void GenerateTypeDefinition_Emits_Resource_When_TypeProvider_Has_Types()
    {
        var settings = CreateTypeSettings();
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new("SimpleResource"))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, null, factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();
        result.IndexFileContent.Should().Contain("SimpleResource");
        result.TypeFileContents.Values.Single().Should().Contain("SimpleResource");
        result.TypeFileContents.Values.Single().Should().Contain("name", because: "the property should be present in the resource type definition");
    }

    [TestMethod]
    public void GenerateTypeDefinition_Throws_On_Unsupported_Property_Type()
    {
        var map = new Dictionary<Type, Func<TypeBase>>
        {
            { typeof(string), () => new StringType() }
        }.ToImmutableDictionary();

        var settings = CreateTypeSettings();
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(TestUnsupportedProperty), new("TestUnsupportedProperty"))]);

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, null, factory, typeProviderMock.Object, map);

        Action act = () => builder.GenerateTypeDefinition();
        act.Should().Throw<NotImplementedException>();
    }

    private record ArrayResource(string[] Items);

    private record EnumerableResource(IEnumerable<string> Items);

    [TestMethod]
    public void GenerateTypeDefinition_Emits_ArrayType_For_ArrayProperty()
    {
        var settings = CreateTypeSettings();
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(ArrayResource), new("ArrayResource"))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, null, factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();
        result.TypeFileContents.Values.Single().Should().Contain("ArrayResource");
        result.TypeFileContents.Values.Single().Should().Contain("items", because: "the array property should be present in the resource type definition");
    }

    [TestMethod]
    public void GenerateTypeDefinition_Emits_ArrayType_For_IEnumerableProperty()
    {
        var settings = CreateTypeSettings();
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(EnumerableResource), new("EnumerableResource"))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, null, factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();
        result.TypeFileContents.Values.Single().Should().Contain("EnumerableResource");
        result.TypeFileContents.Values.Single().Should().Contain("items", because: "the enumerable property should be present in the resource type definition");
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "configurationType"),
     JsonDerivedType(typeof(BarConfiguration), "bar"),
     JsonDerivedType(typeof(BazConfiguration), "baz")]
    private record DerivedConfiguration(
        string Foo
    );

    private record BarConfiguration(
        string Bar);

    private record BazConfiguration(
        string Baz);

    [TestMethod]
    public void GenerateTypeDefinition_Emits_DiscriminatorType_For_JsonPolymorphic_DerivedType_Attributes()
    {
        var settings = CreateTypeSettings();
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new(nameof(SimpleResource)))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, typeof(DerivedConfiguration), factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();
        result.TypeFileContents.Values.Single().Should().Contain(nameof(DerivedConfiguration));
        result.TypeFileContents.Values.Single().Should().Contain(nameof(BazConfiguration));
        result.TypeFileContents.Values.Single().Should().Contain(nameof(BarConfiguration));
        result.TypeFileContents.Values.Single().Should().Contain("baseProperties", because: "the base properties should be present in the resource type definition");
        result.TypeFileContents.Values.Single().Should().Contain("elements", because: "a discriminated type should posses inheriting types");
    }

    private record BasicType(string value);

    private record BasicTypeDictionaryContainer
    {
        public required Dictionary<string, BasicType> Items { get; set; }
    }

    private record IntegerDictionaryContainer
    {
        public required Dictionary<string, int> Items { get; set; }
    }

    private record StringDictionaryContainer
    {
        [BicepStringPattern("SomePattern")] public required Dictionary<string, string> Items { get; set; }
    }

    [TestMethod]
    [DataRow(typeof(BasicTypeDictionaryContainer), nameof(BasicType))]
    [DataRow(typeof(IntegerDictionaryContainer), nameof(Int32))]
    [DataRow(typeof(StringDictionaryContainer), nameof(String))]
    public void GenerateTypeDefinition_Emits_DictionaryType(Type typeToSerialize, string nameOfDictionaryValueType)
    {
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new(nameof(SimpleResource)))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, typeToSerialize, factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();

        var jsonDocument = JsonDocument.Parse(result.TypeFileContents.Values.Single());

        var dictionaryIsSerialized = jsonDocument.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("$type").GetString() == nameof(ObjectType)
                      && e.GetProperty("name").GetString()?.Contains("Dictionary") == true
                      && e.GetProperty("name").GetString()?.Contains(nameOfDictionaryValueType) == true);

        dictionaryIsSerialized.Should().BeTrue();
    }

    [TestMethod]
    public void GenerateTypeDefinition_Emits_DictionaryType_WithStringPattern()
    {
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new(nameof(SimpleResource)))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, typeof(StringDictionaryContainer), factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();

        var jsonDocument = JsonDocument.Parse(result.TypeFileContents.Values.Single());

        var customizedStringExists = jsonDocument.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("$type").GetString() == nameof(StringType)
                      && e.GetProperty("pattern").ValueKind == JsonValueKind.String
                      && e.GetProperty("pattern").GetString() == "SomePattern");

        customizedStringExists.Should().BeTrue();
    }

    private record StringWithAttributes(string Foo)
    {
        [MaxLength(10)]
        [MinLength(5)]
        [BicepStringPattern("SomePattern")]
        public required string Foo { get; set; } = Foo;
    }

    [TestMethod]
    [DataRow(10, 5, "SomePattern", typeof(StringWithAttributes))]
    public void GenerateTypeDefinition_Emits_String_WithAttributes(int maxLength, int minLength, string regexPattern, Type typeToSerialize)
    {
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new(nameof(SimpleResource)))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, typeToSerialize, factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();

        var jsonDocument = JsonDocument.Parse(result.TypeFileContents.Values.Single());

        var customizedStringExists = jsonDocument.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("$type").GetString() == nameof(StringType)
                      && e.GetProperty("minLength").ValueKind == JsonValueKind.Number && e.GetProperty("minLength").GetInt32() == minLength
                      && e.GetProperty("maxLength").ValueKind == JsonValueKind.Number && e.GetProperty("maxLength").GetInt32() == maxLength
                      && e.GetProperty("pattern").ValueKind == JsonValueKind.String && e.GetProperty("pattern").GetString() == regexPattern);

        customizedStringExists.Should().BeTrue();
    }

    private record NullableInteger(
        [property: TypeProperty(null, isNullable: true)]
        int? NullableIntegerProperty);

    private record NullableBool(
        bool? NullableBoolProperty);

    private record NullableString(
        [property: TypeProperty(null, isNullable: true)]
        string? NullableStringProperty);

    private record NullableObject(
        [property: TypeProperty(null, isNullable: true)]
        ArrayResource? NullableObjectProperty);

    [TestMethod]
    [DataRow(typeof(NullableInteger))]
    [DataRow(typeof(NullableBool))]
    [DataRow(typeof(NullableString))]
    [DataRow(typeof(NullableObject))]
    public void GenerateTypeDefinition_Emits_Nullable_Types(Type nullableType)
    {
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new(nameof(SimpleResource)))]);
        var map = new Dictionary<Type, Func<TypeBase>>
        {
            { typeof(string), () => new StringType() },
            { typeof(int), () => new IntegerType() },
            { typeof(bool), () => new BooleanType() },
        };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, nullableType, factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();

        var jsonDocument = JsonDocument.Parse(result.TypeFileContents.Values.Single());

        var unionTypeExists = jsonDocument.RootElement.EnumerateArray()
            .Any(e => e.EnumerateObject()
                .Any(o => o.NameEquals("$type")
                          && o.Value.GetString() == nameof(UnionType)));

        unionTypeExists.Should().BeTrue();
    }

    public enum OperationType
    {
        Uppercase,
        Lowercase,
        Reverse,
    }

    public record EnumContainer(
        [property: TypeProperty(null, isNullable: true)]
        OperationType? Operation);

    [TestMethod]
    public void GenerateTypeDefinition_Emits_Nullable_Enum_Types()
    {
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new(nameof(SimpleResource)))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() }, { typeof(int), () => new IntegerType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, typeof(EnumContainer), factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();

        var jsonDocument = JsonDocument.Parse(result.TypeFileContents.Values.Single());

        var stringLiteralCount = jsonDocument.RootElement.EnumerateArray()
            .Count(e => e.EnumerateObject()
                .Any(o => o.NameEquals("$type")
                          && o.Value.GetString() == nameof(StringLiteralType)));

        var enumElementsCount = Enum.GetNames(typeof(OperationType)).Length;

        stringLiteralCount.Should().Be(enumElementsCount);

        var nullableTypeExists = jsonDocument.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("$type").GetString() == nameof(NullType));

        nullableTypeExists.Should().Be(true);
    }

    public record ArrayObjectItem(string foo);

    public record ArrayObjectContainer(ArrayObjectItem[] fooItems);

    [TestMethod]
    public void GenerateTypeDefinition_Emits_Object_Array_Types()
    {
        var factory = CreateTypeFactory();
        var typeProviderMock = StrictMock.Of<ITypeProvider>();
        typeProviderMock.Setup(tp => tp.GetResourceTypes(true)).Returns([(typeof(SimpleResource), new(nameof(SimpleResource)))]);
        var map = new Dictionary<Type, Func<TypeBase>> { { typeof(string), () => new StringType() }, { typeof(int), () => new IntegerType() } };

        var builder = new TypeDefinitionBuilder("TestSettings", "2025-01-01", true, typeof(ArrayObjectContainer), factory, typeProviderMock.Object, map);

        var result = builder.GenerateTypeDefinition();

        result.Should().NotBeNull();

        var jsonDocument = JsonDocument.Parse(result.TypeFileContents.Values.Single());

        var arrayIsSerialized = jsonDocument.RootElement.EnumerateArray()
            .Any(e => e.EnumerateObject()
                .Any(o => o.NameEquals("name")
                          && o.Value.GetString() == nameof(ArrayObjectContainer)));

        arrayIsSerialized.Should().BeTrue();
    }
}
