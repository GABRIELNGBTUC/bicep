// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Analyzers.Linter.Rules;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Abstractions.TestingHelpers;

namespace Bicep.Core.UnitTests.Diagnostics.LinterRuleTests;

[TestClass]
public class PathMismatchRuleTests : LinterRuleTestsBase
{
    private static readonly ServiceBuilder Services = new ServiceBuilder().WithConfiguration(BicepTestConstants.BuiltInConfigurationWithStableAnalyzers);

    private void CompileAndTest(string text, (string path, string contents)[]? additionalFiles = null, Dictionary<string, string>? invalidImportsMapping = null)
    {
        CompileAndTest(text, LinterRuleTestsBase.OnCompileErrors.IncludeErrors, additionalFiles, invalidImportsMapping);
    }

    private void CompileAndTest(string text, LinterRuleTestsBase.OnCompileErrors onCompileErrors, (string path, string contents)[]? additionalFiles = null, Dictionary<string, string>? invalidImportsMapping = null)
    {
        AssertLinterRuleDiagnostics(PathMismatchRule.Code, text, diags =>
            {
                if (invalidImportsMapping is not null && invalidImportsMapping.Any())
                {
                    var rule = new PathMismatchRule();
                    string[] expectedMessages = invalidImportsMapping.Select(p => rule.GetMessage(p.Key, p.Value)).ToArray();
                    diags.Select(e => e.Message).Should().ContainInOrder(expectedMessages);
                }
                else
                {
                    diags.Should().BeEmpty();
                }
            },
            new Options(onCompileErrors, AdditionalFiles: additionalFiles));
    }

    private static async Task CompileAndTestWithPublishedRegistryModule(string text, string moduleSource)
    {
        var fileSystem = new MockFileSystem();
        var clientFactory = await RegistryHelper.CreateMockRegistryClientWithPublishedModulesAsync(
            fileSystem,
            new RegistryHelper.ModuleToPublish("br:mockregistry.io/test/module1:v1", moduleSource));

        var result = await CompilationHelper.RestoreAndCompile(
            Services.WithContainerRegistryClientFactory(clientFactory),
            text);

        result.Diagnostics.Should().NotContain(diag => diag.Code == PathMismatchRule.Code);
    }

    [TestMethod]
    public void ImportNameInFormattedMessage()
    {
        var ruleToTest = new PathMismatchRule();
        ruleToTest.GetMessage(nameof(ruleToTest), nameof(ImportNameInFormattedMessage)).Should()
            .Be($"File Path \"{nameof(ruleToTest)}\" was defined but \"{nameof(ImportNameInFormattedMessage)}\" was found on disk. This could cause issues on case-sensitive filesystems.");
    }

    [DataRow(@"
            import * as mod from './myModule.bicep'
            param password string
            var sum = 1 + 3
            output sub int = sum
            output str string = mod.getString()
            ",
        "MyModule.bicep",
        @"
            @export()
        func getString() string => 'exported'
        ",
        "myModule.bicep")]
    [DataRow(@"
            import * as config from './configs/settings.bicep'
            output env string = config.environment
            ",
        "configs/Settings.bicep",
        @"
            @export()
        var environment = 'production'
        ",
        "configs/settings.bicep")]
    [DataRow(@"
            import * as helpers from '../shared/helpers.bicep'
            var result = helpers.format()
            output formatted string = result
            ",
        "../shared/Helpers.bicep",
        @"
            @export()
        func format() string => 'formatted'
        ",
        "../shared/helpers.bicep")]
    [DataRow(@"
            import * as types from './types/Common.bicep'
            param value types.CustomType
            ",
        "types/common.bicep",
        @"
            @export()
        type CustomType = { name: string, id: int }
        ",
        "types/Common.bicep")]
    [DataRow(@"
            import * as vars from './variables.bicep'
            output tags object = vars.commonTags
            ",
        "Variables.bicep",
        @"
            @export()
        var commonTags = { Environment: 'Dev', Owner: 'Team' }
        ",
        "variables.bicep")]
    [DataRow(@"
            import * as network from './networking/Subnets/../../../shared/network.bicep'
            output vnet object = network.vnetConfig
            ",
        "../shared/Network.bicep",
        @"
            @export()
        var vnetConfig = { addressPrefix: '10.0.0.0/16' }
        ",
        "../shared/network.bicep")]
    [DataRow(@"
            import * as config from './Configs/module.bicep'
            output env string = config.environment
            ",
        "configs/module.bicep",
        @"
            @export()
        var environment = 'production'
        ",
        "Configs/module.bicep")]
    [DataRow(@"
            import {getString} from './myModule.bicep'
            output str string = getString()
            ",
        "MyModule.bicep",
        @"
            @export()
        func getString() string => 'exported'
        ",
        "myModule.bicep")]
    [DataRow(@"
            import {environment, location} from './configs/settings.bicep'
            output env string = environment
            output loc string = location
            ",
        "configs/Settings.bicep",
        @"
            @export()
        var environment = 'production'

            @export()
        var location = 'westus'
        ",
        "configs/settings.bicep")]
    [TestMethod]
    public void TestRule(string text, string importFileName, string importFileText, string invalidImport)
    {
        var additionalFiles = new[] { (importFileName, importFileText) };
        CompileAndTest(text, additionalFiles, new Dictionary<string, string> { { invalidImport, importFileName } });
    }

    [DataRow(@"
            module myMod './myModule.bicep' = {}
            ",
        "MyModule.bicep",
        @"
            output str string = 'exported'
        ",
        "myModule.bicep")]
    [TestMethod]
    public void TestRuleOnModule(string text, string moduleFileName, string moduleFileText, string invalidModules)
    {
        var additionalFiles = new[] { (moduleFileName, moduleFileText) };
        CompileAndTest(text, additionalFiles, new Dictionary<string, string> { { invalidModules, moduleFileName } });
    }

    [DataRow(@"
            import * as mod from './myModule.bicep'
            output str string = mod.getString()
            ",
        "myModule.bicep",
        @"
            @export()
        func getString() string => 'exported'
        ")]
    [DataRow(@"
            import * as network from './networking/subnets/../../shared/network.bicep'
            output vnet object = network.vnetConfig
            ",
        "shared/network.bicep",
        @"
            @export()
        var vnetConfig = { addressPrefix: '10.0.0.0/16' }
        ")]
    [TestMethod]
    public void TestRuleNotRanWhenPathMatchesExactly(string text, string importFileName, string importFileText)
    {
        var additionalFiles = new[] { (importFileName, importFileText) };
        CompileAndTest(text, additionalFiles);
    }

    [TestMethod]
    public void TestRuleReportsMultipleMismatchedImports()
    {
        var text = @"
            import * as first from './modules/firstmodule.bicep'
            import * as second from './shared/secondmodule.bicep'
            output firstValue string = first.getFirst()
            output secondValue string = second.getSecond()
            ";
        var additionalFiles = new[]
        {
            ("modules/FirstModule.bicep", @"
                @export()
            func getFirst() string => 'first'
            "),
            ("shared/SecondModule.bicep", @"
                @export()
            func getSecond() string => 'second'
            "),
        };

        CompileAndTest(text, additionalFiles, new Dictionary<string, string>
        {
            { "modules/firstmodule.bicep", "modules/FirstModule.bicep" },
            { "shared/secondmodule.bicep", "shared/SecondModule.bicep" },
        });
    }

    [TestMethod]
    public void TestRuleReportsOnlyInvalidImport()
    {
        var text = @"
            import * as valid from './shared/valid.bicep'
            import * as invalid from './shared/invalidmodule.bicep'
            output validValue string = valid.getValid()
            output invalidValue string = invalid.getInvalid()
            ";
        var additionalFiles = new[]
        {
            ("shared/valid.bicep", @"
                @export()
            func getValid() string => 'valid'
            "),
            ("shared/InvalidModule.bicep", @"
                @export()
            func getInvalid() string => 'invalid'
            "),
        };

        CompileAndTest(text, additionalFiles, new Dictionary<string, string>
        {
            { "shared/invalidmodule.bicep", "shared/InvalidModule.bicep" },
        });
    }

    [TestMethod]
    public void TestRuleRanForUnusedWildcardImport()
    {
        var text = @"
            import * as mod from './myModule.bicep'
            output result string = 'test'
            ";
        var additionalFiles = new[]
        {
            ("MyModule.bicep", @"
                @export()
            func getString() string => 'exported'
            "),
        };

        CompileAndTest(text, additionalFiles, new Dictionary<string, string>
        {
            { "myModule.bicep", "MyModule.bicep" },
        });
    }

    [DataRow(@"
            import * as mod from 'ts:00000000-0000-0000-0000-000000000000/test-rg/test-ts:v1'
            output result string = 'test'
            ")]
    [DataRow(@"
            import {storageName} from 'ts:00000000-0000-0000-0000-000000000000/test-rg/test-ts:v1'
            output result string = 'test'
            ")]
    [TestMethod]
    public void TestRuleNotRanForRegistryImports(string text)
    {
        AssertLinterRuleDiagnostics(
            PathMismatchRule.Code,
            text,
            diags => diags.Should().NotContain(diag => diag.Code == PathMismatchRule.Code),
            new Options(LinterRuleTestsBase.OnCompileErrors.IncludeErrors));
    }

    [DataRow(@"
            module myMod 'ts:00000000-0000-0000-0000-000000000000/test-rg/test-ts:v1' = {
              name: 'myMod'
            }
            ")]
    [TestMethod]
    public void TestRuleNotRanForRegistryModules(string text)
    {
        AssertLinterRuleDiagnostics(
            PathMismatchRule.Code,
            text,
            diags => diags.Should().NotContain(diag => diag.Code == PathMismatchRule.Code),
            new Options(LinterRuleTestsBase.OnCompileErrors.IncludeErrors));
    }

    [DataRow(@"
            import * as mod from 'br:mockregistry.io/test/module1:v1'
            output result string = mod.storageName
            ",
        @"
            @export()
            var storageName = 'test'
            ")]
    [DataRow(@"
            import {storageName} from 'br:mockregistry.io/test/module1:v1'
            output result string = storageName
            ",
        @"
            @export()
            var storageName = 'test'
            ")]
    [TestMethod]
    public async Task TestRuleNotRanForBicepRegistryImports(string text, string moduleSource)
    {
        await CompileAndTestWithPublishedRegistryModule(text, moduleSource);
    }

    [DataRow(@"
            module myMod 'br:mockregistry.io/test/module1:v1' = {
              name: 'myMod'
            }
            ",
        @"
            output storageName string = 'test'
            ")]
    [TestMethod]
    public async Task TestRuleNotRanForBicepRegistryModules(string text, string moduleSource)
    {
        await CompileAndTestWithPublishedRegistryModule(text, moduleSource);
    }

    [DataRow(@"
            import * as mod from './myModule.bicep'
            param password string
            var sum = 1 + 3
            output sub int = sum
            output str string = mod.getString()
            ",
        "MyModule.bicep",
        @"
            @export()
        func getString() string => 'exported'
        ",
        "myModule.bicep")]
    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
    public void TestRuleNotRanWhenFileDoesNotExist(string text, string importFileName, string importFileText, string invalidImport)
    {
        AssertLinterRuleDiagnostics(
            PathMismatchRule.Code,
            text,
            diags => diags.Should().NotContain(diag => diag.Code == PathMismatchRule.Code),
            new Options(LinterRuleTestsBase.OnCompileErrors.IncludeErrors));
    }



}
