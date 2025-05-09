// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Bicep.Core.Diagnostics;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using Bicep.Core.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Bicep.Core.IntegrationTests
{
    [TestClass]
    public class ParentPropertyResourceTests
    {
        [TestMethod]
        public void Parent_property_formats_names_and_dependsOn_correctly()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource vnet 'Microsoft.Network/virtualNetworks@2020-06-01' = {
  location: resourceGroup().location
  name: 'myVnet'
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/20'
      ]
    }
  }
}

resource subnet1 'Microsoft.Network/virtualNetworks/subnets@2020-06-01' = {
  parent: vnet
  name: 'subnet1'
  properties: {
    addressPrefix: '10.0.0.0/24'
  }
}

resource subnet2 'Microsoft.Network/virtualNetworks/subnets@2020-06-01' = {
  parent: vnet
  name: 'subnet2'
  properties: {
    addressPrefix: '10.0.1.0/24'
  }
}

output subnet1prefix string = subnet1.properties.addressPrefix
output subnet1name string = subnet1.name
output subnet1type string = subnet1.type
output subnet1id string = subnet1.id
");

            using (new AssertionScope())
            {
                diags.ExcludingLinterDiagnostics().Should().BeEmpty();

                template.Should().HaveValueAtPath("$.resources[0].name", "myVnet");
                template.Should().NotHaveValueAtPath("$.resources[0].dependsOn");

                template.Should().HaveValueAtPath("$.resources[1].name", "[format('{0}/{1}', 'myVnet', 'subnet1')]");
                template.Should().HaveValueAtPath("$.resources[1].dependsOn", new JArray { "[resourceId('Microsoft.Network/virtualNetworks', 'myVnet')]" });

                template.Should().HaveValueAtPath("$.resources[2].name", "[format('{0}/{1}', 'myVnet', 'subnet2')]");
                template.Should().HaveValueAtPath("$.resources[2].dependsOn", new JArray { "[resourceId('Microsoft.Network/virtualNetworks', 'myVnet')]" });

                template.Should().HaveValueAtPath("$.outputs['subnet1prefix'].value", "[reference(resourceId('Microsoft.Network/virtualNetworks/subnets', 'myVnet', 'subnet1'), '2020-06-01').addressPrefix]");
                template.Should().HaveValueAtPath("$.outputs['subnet1name'].value", "subnet1");
                template.Should().HaveValueAtPath("$.outputs['subnet1type'].value", "Microsoft.Network/virtualNetworks/subnets");
                template.Should().HaveValueAtPath("$.outputs['subnet1id'].value", "[resourceId('Microsoft.Network/virtualNetworks/subnets', 'myVnet', 'subnet1')]");
            }
        }

        [TestMethod]
        public void Parent_property_works_with_extension_resources()
        {
            var result = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' = {
  name: 'res1'
}

resource res1child 'Microsoft.Rp1/resource1/child1@2020-06-01' = {
  parent: res1
  name: 'child1'
}

resource res2 'Microsoft.Rp2/resource2@2020-06-01' = {
  scope: res1child
  name: 'res2'
}

resource res2child 'Microsoft.Rp2/resource2/child2@2020-06-01' = {
  parent: res2
  name: 'child2'
}

output res2childprop string = res2child.properties.someProp
output res2childname string = res2child.name
output res2childtype string = res2child.type
output res2childid string = res2child.id
");

            result.Diagnostics.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

            result.Template.Should().HaveValueAtPath("$.resources[0].name", "res1");
            result.Template.Should().NotHaveValueAtPath("$.resources[0].dependsOn");

            result.Template.Should().HaveValueAtPath("$.resources[1].name", "[format('{0}/{1}', 'res1', 'child1')]");
            result.Template.Should().HaveValueAtPath("$.resources[1].dependsOn", new JArray { "[resourceId('Microsoft.Rp1/resource1', 'res1')]" });

            result.Template.Should().HaveValueAtPath("$.resources[2].name", "res2");
            result.Template.Should().HaveValueAtPath("$.resources[2].dependsOn", new JArray { "[resourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1')]" });

            result.Template.Should().HaveValueAtPath("$.resources[3].name", "[format('{0}/{1}', 'res2', 'child2')]");
            result.Template.Should().HaveValueAtPath("$.resources[3].dependsOn", new JArray { "[extensionResourceId(resourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1'), 'Microsoft.Rp2/resource2', 'res2')]" });

            result.Template.Should().HaveValueAtPath("$.outputs['res2childprop'].value", "[reference(extensionResourceId(resourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1'), 'Microsoft.Rp2/resource2/child2', 'res2', 'child2'), '2020-06-01').someProp]");
            result.Template.Should().HaveValueAtPath("$.outputs['res2childname'].value", "child2");
            result.Template.Should().HaveValueAtPath("$.outputs['res2childtype'].value", "Microsoft.Rp2/resource2/child2");
            result.Template.Should().HaveValueAtPath("$.outputs['res2childid'].value", "[extensionResourceId(resourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1'), 'Microsoft.Rp2/resource2/child2', 'res2', 'child2')]");
        }

        [TestMethod]
        public void Parent_property_works_with_extension_resource_collections()
        {
            var result = CompilationHelper.Compile(@"
resource res 'Microsoft.Rp1/resource1@2020-06-01' = [for i in range(0, 10): {
  name: 'res${i}'
}]

resource resChild 'Microsoft.Rp1/resource1/child1@2020-06-01' = [for j in range(10, 10): {
  parent: res[j - 10]
  name: 'child${j - 10}'
}]

resource otherRes 'Microsoft.Rp2/resource2@2020-06-01' = [for k in range(20, 10): {
  scope: resChild[k - 20]
  name: 'otherRes${k - 20}'
}]

resource otherResChild 'Microsoft.Rp2/resource2/child2@2020-06-01' = [for l in range(30, 10): {
  parent: otherRes[l - 30]
  name: 'otherResChild${l - 30}'
}]

output otherRes2childName string = otherResChild[2].name
output otherRes2childType string = otherResChild[2].type
output otherRes2childId string = otherResChild[2].id
");

            result.Diagnostics.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

            var compiled = result.Template;
            var evaluated = TemplateEvaluator.Evaluate(compiled).ToJToken();

            compiled.Should().HaveValueAtPath("$.outputs['otherRes2childName'].value", "[format('otherResChild{0}', sub(range(30, 10)[2], 30))]");
            evaluated.Should().HaveValueAtPath("$.outputs['otherRes2childName'].value", "otherResChild2");
            compiled.Should().HaveValueAtPath("$.outputs['otherRes2childType'].value", "Microsoft.Rp2/resource2/child2");
            evaluated.Should().HaveValueAtPath("$.outputs['otherRes2childType'].value", "Microsoft.Rp2/resource2/child2");
            compiled.Should().HaveValueAtPath("$.outputs['otherRes2childId'].value", "[extensionResourceId(resourceId('Microsoft.Rp1/resource1/child1', format('res{0}', range(0, 10)[sub(range(10, 10)[sub(range(30, 10)[2], 30)], 10)]), format('child{0}', sub(range(10, 10)[sub(range(30, 10)[2], 30)], 10))), 'Microsoft.Rp2/resource2/child2', format('otherRes{0}', sub(range(20, 10)[sub(range(30, 10)[2], 30)], 20)), format('otherResChild{0}', sub(range(30, 10)[2], 30)))]");
            evaluated.Should().HaveValueAtPath("$.outputs['otherRes2childId'].value", $"/subscriptions/{Guid.Empty}/resourceGroups/DummyResourceGroup/providers/Microsoft.Rp1/resource1/res2/child1/child2/providers/Microsoft.Rp2/resource2/otherRes2/child2/otherResChild2");
        }

        [TestMethod]
        public void Parent_property_works_with_existing_resources()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' existing = {
  name: 'res1'
}

resource child1 'Microsoft.Rp1/resource1/child1@2020-06-01' = {
  parent: res1
  name: 'child1'
}

output res1childprop string = child1.properties.someProp
output res1childname string = child1.name
output res1childtype string = child1.type
output res1childid string = child1.id
");

            using (new AssertionScope())
            {
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

                // child1
                template.Should().HaveValueAtPath("$.resources[0].name", "[format('{0}/{1}', 'res1', 'child1')]");
                template.Should().NotHaveValueAtPath("$.resources[0].dependsOn");

                template.Should().NotHaveValueAtPath("$.resources[1]");

                template.Should().HaveValueAtPath("$.outputs['res1childprop'].value", "[reference(resourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1'), '2020-06-01').someProp]");
                template.Should().HaveValueAtPath("$.outputs['res1childname'].value", "child1");
                template.Should().HaveValueAtPath("$.outputs['res1childtype'].value", "Microsoft.Rp1/resource1/child1");
                template.Should().HaveValueAtPath("$.outputs['res1childid'].value", "[resourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1')]");
            }
        }

        [TestMethod]
        public void Parent_property_formats_references_correctly_for_existing_resources()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' existing = {
  scope: tenant()
  name: 'res1'
}

resource child1 'Microsoft.Rp1/resource1/child1@2020-06-01' existing = {
  parent: res1
  name: 'child1'
}

output res1childprop string = child1.properties.someProp
output res1childname string = child1.name
output res1childtype string = child1.type
output res1childid string = child1.id
");

            using (new AssertionScope())
            {
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

                template.Should().NotHaveValueAtPath("$.resources[0]");

                template.Should().HaveValueAtPath("$.outputs['res1childprop'].value", "[reference(tenantResourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1'), '2020-06-01').someProp]");
                template.Should().HaveValueAtPath("$.outputs['res1childname'].value", "child1");
                template.Should().HaveValueAtPath("$.outputs['res1childtype'].value", "Microsoft.Rp1/resource1/child1");
                template.Should().HaveValueAtPath("$.outputs['res1childid'].value", "[tenantResourceId('Microsoft.Rp1/resource1/child1', 'res1', 'child1')]");
            }
        }

        [DataTestMethod]
        [DataRow("resourceGroup('other')")]
        [DataRow("subscription()")]
        [DataRow("managementGroup('abcdef')")]
        public void Parent_property_blocks_existing_parents_at_different_scopes(string parentScope)
        {
            var result = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' existing = {
  scope: " + parentScope + @"
  name: 'res1'
}

resource child1 'Microsoft.Rp1/resource1/child1@2020-06-01' = {
  parent: res1
  name: 'child1'
}
");

            result.Diagnostics.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                ("BCP165", DiagnosticLevel.Error, "A resource's computed scope must match that of the Bicep file for it to be deployable. This resource's scope is computed from the \"scope\" property value assigned to ancestor resource \"res1\". You must use modules to deploy resources to a different scope."),
            });
        }

        [TestMethod]
        public void Parent_property_allows_existing_parents_at_tenant_scope()
        {
            var result = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' existing = {
  scope: tenant()
  name: 'res1'
}

resource child1 'Microsoft.Rp1/resource1/child1@2020-06-01' = {
  parent: res1
  name: 'child1'
}
");

            result.Diagnostics.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();
            result.Template.Should().HaveValueAtPath("$.resources[0].scope", "/");
            result.Template.Should().HaveValueAtPath("$.resources[0].name", "[format('{0}/{1}', 'res1', 'child1')]");
        }

        [TestMethod]
        public void Parent_property_blocks_scope_on_child_resources()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' = {
  name: 'res1'
}

resource res2 'Microsoft.Rp2/resource2@2020-06-01' = {
  name: 'res2'
}

resource res2child 'Microsoft.Rp2/resource2/child2@2020-06-01' = {
  scope: res1
  parent: res2
  name: 'child2'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP164", DiagnosticLevel.Error, "A child resource's scope is computed based on the scope of its ancestor resource. This means that using the \"scope\" property on a child resource is unsupported."),
                });
            }
        }

        [TestMethod]
        public void Parent_property_self_cycles_are_blocked()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource vmExt 'Microsoft.Compute/virtualMachines/extensions@2020-06-01' = {
  name: 'vmExt'
  parent: vmExt
  location: 'eastus'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP079", DiagnosticLevel.Error, "This expression is referencing its own declaration, which is not allowed.")
                });
            }
        }

        [TestMethod]
        public void Parent_property_2_cycles_are_blocked()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource vm 'Microsoft.Compute/virtualMachines@2020-06-01' = {
  parent: vmExt
  location: 'eastus'
}

resource vmExt 'Microsoft.Compute/virtualMachines/extensions@2020-06-01' = {
  parent: vm
  location: 'eastus'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP080", DiagnosticLevel.Error, "The expression is involved in a cycle (\"vmExt\" -> \"vm\")."),
                  ("BCP080", DiagnosticLevel.Error, "The expression is involved in a cycle (\"vm\" -> \"vmExt\").")
                });
            }
        }

        [TestMethod]
        public void Parent_property_blocks_invalid_child_resources()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' = {
  name: 'res1'
}

resource res2 'Microsoft.Rp2/resource2/child2@2020-06-01' = {
  parent: res1
  name: 'res2'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP036", DiagnosticLevel.Error, "The property \"parent\" expected a value of type \"Microsoft.Rp2/resource2\" but the provided value is of type \"Microsoft.Rp1/resource1@2020-06-01\"."),
                });
            }
        }

        [TestMethod]
        public void Parent_property_blocks_non_resource_reference()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource res1 '${true}' = {
  name: 'res1'
}

resource res2 'Microsoft.Rp1/resource1/child2@2020-06-01' = {
  parent: res1
  name: 'res2'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP047", DiagnosticLevel.Error, "String interpolation is unsupported for specifying the resource type."),
                  ("BCP062", DiagnosticLevel.Error, "The referenced declaration with name \"res1\" is not valid."),
                });
            }
        }

        [TestMethod]
        public void Parent_property_detects_invalid_child_resource_literal_names()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' = {
  name: 'res1'
}

resource res2 'Microsoft.Rp1/resource1/child2@2020-06-01' = {
  parent: res1
  name: 'res1/res2'
}

resource res3 'Microsoft.Rp1/resource1/child2@2020-06-01' = {
  parent: res1
  name: '${res1.name}/res3'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP170", DiagnosticLevel.Error, "Expected resource name to not contain any \"/\" characters. Child resources with a parent resource reference (via the parent property or via nesting) must not contain a fully-qualified name."),
                  ("BCP170", DiagnosticLevel.Error, "Expected resource name to not contain any \"/\" characters. Child resources with a parent resource reference (via the parent property or via nesting) must not contain a fully-qualified name."),
                });
            }
        }

        [TestMethod]
        public void Top_level_resource_should_have_appropriate_number_of_slashes_in_literal_names()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1@2020-06-01' = {
  name: 'res1/res2'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP169", DiagnosticLevel.Error, "Expected resource name to contain 0 \"/\" character(s). The number of name segments must match the number of segments in the resource type."),
                });
            }

            (template, diags, _) = CompilationHelper.Compile(@"
resource res1 'Microsoft.Rp1/resource1/child2@2020-06-01' = {
  name: 'res1'
}
");

            using (new AssertionScope())
            {
                template.Should().NotHaveValue();
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                  ("BCP169", DiagnosticLevel.Error, "Expected resource name to contain 1 \"/\" character(s). The number of name segments must match the number of segments in the resource type."),
                });
            }
        }

        [TestMethod]
        public void Top_level_resource_should_have_appropriate_number_of_slashes_in_interpolated_names()
        {

            var result = CompilationHelper.Compile(TestTypeHelper.CreateEmptyResourceTypeLoader(),
                ("main.bicep", @"
param p1 string

resource res1 'Microsoft.Rp1/resource1@2020-06-01' = {
  name: '${p1}/res2'
}
"));

            // There are definitely too many '/' characters in the name - we should return an error.
            result.Should().NotGenerateATemplate();
            result.Diagnostics.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().HaveDiagnostics(new[] {
                ("BCP169", DiagnosticLevel.Error, "Expected resource name to contain 0 \"/\" character(s). The number of name segments must match the number of segments in the resource type."),
            });

            result = CompilationHelper.Compile(TestTypeHelper.CreateEmptyResourceTypeLoader(),
                ("main.bicep", @"
param p1 string

resource res1 'Microsoft.Rp1/resource1/child1@2020-06-01' = {
  name: 'a${p1}b'
}
"));

            // The name requires a single '/' character to be valid, but we cannot be sure that 'p1' doesn't contain it - we should not return an error.
            result.Diagnostics.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();
        }

        [TestMethod]
        public void Parent_property_loop_on_children()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
var items = [
  'a'
  'b'
]
resource parent 'My.RP/parentType@2020-01-01' = {
  name: 'parent'
  properties: {}
}

resource child 'My.RP/parentType/childType@2020-01-01' = [for (item, i) in items: {
  parent: parent
  name: 'child${i}'
  properties: {}
}]

output child0Props object = child[0].properties
output child0Name string = child[0].name
");

            using (new AssertionScope())
            {
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

                template.Should().NotHaveValueAtPath("$.resources[0].copy");
                template.Should().HaveValueAtPath("$.resources[0].name", "parent");
                template.Should().NotHaveValueAtPath("$.resources[0].dependsOn");

                template.Should().HaveValueAtPath("$.resources[1].copy", new JObject
                {
                    ["name"] = "child",
                    ["count"] = "[length(variables('items'))]",
                });
                template.Should().HaveValueAtPath("$.resources[1].name", "[format('{0}/{1}', 'parent', format('child{0}', copyIndex()))]");
                template.Should().HaveValueAtPath("$.resources[1].dependsOn", new JArray {
                    "[resourceId('My.RP/parentType', 'parent')]",
                });

                template.Should().HaveValueAtPath("$.outputs['child0Props'].value", "[reference(resourceId('My.RP/parentType/childType', 'parent', format('child{0}', 0)), '2020-01-01')]");
                template.Should().HaveValueAtPath("$.outputs['child0Name'].value", "[format('child{0}', 0)]");
            }
        }

        [TestMethod]
        public void Parent_property_loop_on_parent_and_child()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
var items = [
  'a'
  'b'
]
resource parent 'My.RP/parentType@2020-01-01' = [for item in items: {
  name: 'parent${item}'
  properties: {}
}]

resource child 'My.RP/parentType/childType@2020-01-01' = [for (item, i) in items: {
  parent: parent[i]
  name: 'child${i}'
  properties: {}
}]

output parent0Props object = parent[0].properties
output child0Props object = child[0].properties
output parent0Name string = parent[0].name
output child0Name string = child[0].name
");

            using (new AssertionScope())
            {
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

                template.Should().HaveValueAtPath("$.resources[0].copy", new JObject
                {
                    ["name"] = "parent",
                    ["count"] = "[length(variables('items'))]"
                });
                template.Should().HaveValueAtPath("$.resources[0].name", "[format('parent{0}', variables('items')[copyIndex()])]");
                template.Should().NotHaveValueAtPath("$.resources[0].dependsOn");

                template.Should().HaveValueAtPath("$.resources[1].copy", new JObject
                {
                    ["name"] = "child",
                    ["count"] = "[length(variables('items'))]",
                });
                template.Should().HaveValueAtPath("$.resources[1].name", "[format('{0}/{1}', format('parent{0}', variables('items')[copyIndex()]), format('child{0}', copyIndex()))]");
                template.Should().HaveValueAtPath("$.resources[1].dependsOn", new JArray {
                    "[resourceId('My.RP/parentType', format('parent{0}', variables('items')[copyIndex()]))]",
                });

                template.Should().HaveValueAtPath("$.outputs['parent0Props'].value", "[reference(resourceId('My.RP/parentType', format('parent{0}', variables('items')[0])), '2020-01-01')]");
                template.Should().HaveValueAtPath("$.outputs['child0Props'].value", "[reference(resourceId('My.RP/parentType/childType', format('parent{0}', variables('items')[0]), format('child{0}', 0)), '2020-01-01')]");
                template.Should().HaveValueAtPath("$.outputs['parent0Name'].value", "[format('parent{0}', variables('items')[0])]");
                template.Should().HaveValueAtPath("$.outputs['child0Name'].value", "[format('child{0}', 0)]");
            }
        }

        [TestMethod]
        public void Parent_property_loop_on_parent_individual_child()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
var items = [
  'a'
  'b'
]
resource parent 'My.RP/parentType@2020-01-01' = [for item in items: {
  name: 'parent${item}'
  properties: {}
}]

resource child 'My.RP/parentType/childType@2020-01-01' = {
  parent: parent[0]
  name: 'child'
  properties: {}
}

output childProps object = child.properties
output childName string = child.name
");

            using (new AssertionScope())
            {
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

                template.Should().HaveValueAtPath("$.resources[0].copy", new JObject
                {
                    ["name"] = "parent",
                    ["count"] = "[length(variables('items'))]"
                });
                template.Should().HaveValueAtPath("$.resources[0].name", "[format('parent{0}', variables('items')[copyIndex()])]");
                template.Should().NotHaveValueAtPath("$.resources[0].dependsOn");

                template.Should().NotHaveValueAtPath("$.resources[1].copy");
                template.Should().HaveValueAtPath("$.resources[1].name", "[format('{0}/{1}', format('parent{0}', variables('items')[0]), 'child')]");
                template.Should().HaveValueAtPath("$.resources[1].dependsOn", new JArray {
                    "[resourceId('My.RP/parentType', format('parent{0}', variables('items')[0]))]",
                });

                template.Should().HaveValueAtPath("$.outputs['childProps'].value", "[reference(resourceId('My.RP/parentType/childType', format('parent{0}', variables('items')[0]), 'child'), '2020-01-01')]");
                template.Should().HaveValueAtPath("$.outputs['childName'].value", "child");
            }
        }

        [TestMethod]
        public void Parent_property_loop_on_parent_and_child_with_offset()
        {
            var (template, diags, _) = CompilationHelper.Compile(@"
var items = [
  'a'
  'b'
]
resource parent 'My.RP/parentType@2020-01-01' = [for item in items: {
  name: 'parent${item}'
  properties: {}
}]

resource child 'My.RP/parentType/childType@2020-01-01' = [for (item, i) in items: {
  parent: parent[(i + 1) % length(items)]
  name: 'child${item}'
  properties: {}
}]

output parent0Props object = parent[0].properties
output child0Props object = child[0].properties
output parent0Name string = parent[0].name
output child0Name string = child[0].name
");

            using (new AssertionScope())
            {
                diags.ExcludingLinterDiagnostics().ExcludingMissingTypes().Should().BeEmpty();

                template.Should().HaveValueAtPath("$.resources[0].copy", new JObject
                {
                    ["name"] = "parent",
                    ["count"] = "[length(variables('items'))]"
                });
                template.Should().HaveValueAtPath("$.resources[0].name", "[format('parent{0}', variables('items')[copyIndex()])]");
                template.Should().NotHaveValueAtPath("$.resources[0].dependsOn");

                template.Should().HaveValueAtPath("$.resources[1].copy", new JObject
                {
                    ["name"] = "child",
                    ["count"] = "[length(variables('items'))]",
                });
                template.Should().HaveValueAtPath("$.resources[1].name", "[format('{0}/{1}', format('parent{0}', variables('items')[mod(add(copyIndex(), 1), length(variables('items')))]), format('child{0}', variables('items')[copyIndex()]))]");
                template.Should().HaveValueAtPath("$.resources[1].dependsOn", new JArray {
                    "[resourceId('My.RP/parentType', format('parent{0}', variables('items')[mod(add(copyIndex(), 1), length(variables('items')))]))]",
                });

                template.Should().HaveValueAtPath("$.outputs['parent0Props'].value", "[reference(resourceId('My.RP/parentType', format('parent{0}', variables('items')[0])), '2020-01-01')]");
                template.Should().HaveValueAtPath("$.outputs['child0Props'].value", "[reference(resourceId('My.RP/parentType/childType', format('parent{0}', variables('items')[mod(add(0, 1), length(variables('items')))]), format('child{0}', variables('items')[0])), '2020-01-01')]");
                template.Should().HaveValueAtPath("$.outputs['parent0Name'].value", "[format('parent{0}', variables('items')[0])]");
                template.Should().HaveValueAtPath("$.outputs['child0Name'].value", "[format('child{0}', variables('items')[0])]");
            }
        }
    }
}
