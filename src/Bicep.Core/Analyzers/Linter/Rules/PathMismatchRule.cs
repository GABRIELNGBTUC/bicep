// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Diagnostics;
using Bicep.Core.Modules;
using Bicep.Core.Semantics;
using Bicep.Core.SourceGraph;
using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.IO.Abstraction;

namespace Bicep.Core.Analyzers.Linter.Rules
{
    // Does not handle detection of unused exports when using wildcard imports. Only if the alias is unused.
    public sealed class PathMismatchRule : LinterRuleBase
    {
        public new const string Code = "path-mismatch";

        public PathMismatchRule() : base(
            code: Code,
            description: CoreResources.PathMismatchRuleDescription,
            LinterRuleCategory.PotentialCodeIssues
        )
        {
        }

        public override string FormatMessage(params object[] values)
        {
            return string.Format(CoreResources.PathMismatchRuleMessageFormat, values);
        }

        override public IEnumerable<IDiagnostic> AnalyzeInternal(SemanticModel model, DiagnosticLevel diagnosticLevel)
        {
            var invertedBindings = model.Binder.Bindings.ToLookup(kvp => kvp.Value, kvp => kvp.Key);

            // VariableAccessSyntax or ImportedFunctionSymbol indicates a reference to the import
            var imports = model.Root.ImportedSymbols
                .Where(sym => sym.NameSource.IsValid).ToList();

            var wildcardSymbols = model.Root.WildcardImports
                .Where(sym => sym.NameSource.IsValid).ToList();

            var modules = model.Root.ModuleDeclarations
                .Where(sym => sym.NameSource.IsValid).ToList();

            if(!wildcardSymbols.Any() && !imports.Any() && !modules.Any())
            {
            }

            var rootIoUri = model.Root.Context.SourceFile.FileHandle.Uri;

            foreach (var sym in imports)
            {

                if (sym.SourceModel is SemanticModel sm  && sym.EnclosingDeclaration.FromClause is CompileTimeImportFromClauseSyntax fromClause)
                {
                    if (sym.Context.SourceFileLookup.ArtifactLookup.Any(a => a.Key == sym.EnclosingDeclaration  && a.Value.Reference is not LocalModuleReference))
                    {
                        continue;
                    }
                    foreach (var diagnostic in GetPathMismatchDiagnostics(sm.SourceFile.FileHandle, rootIoUri, diagnosticLevel, fromClause.Path.Span))
                    {
                        yield return diagnostic;
                    }
                }
            }

            foreach (var sym in wildcardSymbols)
            {
                if (sym.Context.SourceFileLookup.ArtifactLookup.Any(a => a.Key == sym.EnclosingDeclaration && a.Value.Reference is not LocalModuleReference))
                {
                    continue;
                }
                if (sym.SourceModel is SemanticModel sm && sym.EnclosingDeclaration.FromClause is CompileTimeImportFromClauseSyntax fromClause)
                {
                    foreach (var diagnostic in GetPathMismatchDiagnostics(sm.SourceFile.FileHandle, rootIoUri, diagnosticLevel, fromClause.Path.Span))
                    {
                        yield return diagnostic;
                    }
                }

            }

            foreach (var sym in modules)
            {
                if (sym.Context.SourceFileLookup.ArtifactLookup.Any(a => a.Key == sym.DeclaringModule  && a.Value.Reference is not LocalModuleReference))
                {
                    continue;
                }
                var moduleHandle = sym.Context.SourceFile.FileHandle.GetParent().GetFile(sym.DeclaringModule.Path.ToString().Replace("'", string.Empty).Trim());
                foreach (var diagnostic in GetPathMismatchDiagnostics(moduleHandle, rootIoUri, diagnosticLevel, sym.DeclaringModule.Span))
                {
                    yield return diagnostic;
                }
            }


        }

        private IEnumerable<IDiagnostic> GetPathMismatchDiagnostics(IFileHandle sourceFileHandle, IOUri rootIoUri, DiagnosticLevel diagnosticLevel, TextSpan span)
        {
            var parentHandle = sourceFileHandle.GetParent();
            var bicepResolvedPathRelative = sourceFileHandle.Uri.GetPathRelativeTo(rootIoUri);
            var fileHandles = parentHandle.EnumerateFiles().ToArray();

            foreach (var fileHandle in fileHandles)
            {
                var onDiskRelativePath = fileHandle.Uri.GetPathRelativeTo(rootIoUri);
                var caseInsensitiveMatch = onDiskRelativePath.Equals(
                    bicepResolvedPathRelative,
                    StringComparison.OrdinalIgnoreCase);

                if (caseInsensitiveMatch is true)
                {
                    var caseSensitiveMatch = onDiskRelativePath.Equals(
                        bicepResolvedPathRelative,
                        StringComparison.Ordinal);

                    if (caseSensitiveMatch is false)
                    {
                        yield return CreateDiagnosticForSpan(diagnosticLevel, span, bicepResolvedPathRelative, onDiskRelativePath);
                    }
                }
            }
        }


    }
}
