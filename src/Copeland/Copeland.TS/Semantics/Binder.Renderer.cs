using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Copeland.TS.Mir;
using Copeland.TS.Semantics.Bound;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private bool IsCopelandPresentation => _projectTypes.HasFlag(CopelandProjectTypeSet.CopelandViews);

        private bool IsVuePresentation => _projectTypes.HasFlag(CopelandProjectTypeSet.VueComponents);

        private bool UsesDefaultSlots => IsCopelandPresentation
            ? presentationAdapter?.Children == MirPresentationChildren.DefaultSlot
            : IsVuePresentation;

        private bool HasComponentPresentation => _projectTypes.HasFlag(CopelandProjectTypeSet.ReactComponents)
            || IsVuePresentation || IsCopelandPresentation;

        private TypeSymbol PresentationNodeType
        {
            get
            {
                if (IsCopelandPresentation) return ViewTypeSymbol.Instance;
                if (IsVuePresentation) return VueNodeTypeSymbol.Instance;
                return ReactNodeTypeSymbol.Instance;
            }
        }

        private string PresentationFactoryName
        {
            get
            {
                if (IsCopelandPresentation) return presentationAdapter?.ElementExport ?? "<unconfigured>";
                return IsVuePresentation ? "h" : "createElement";
            }
        }

        private string PresentationPackage
        {
            get
            {
                if (IsCopelandPresentation) return presentationAdapter?.ElementModule ?? "<unconfigured>";
                return IsVuePresentation ? "vue" : "react";
            }
        }

        private string PresentationAdapterIdentity
        {
            get
            {
                if (IsCopelandPresentation) return presentationAdapter?.Identity ?? "<unconfigured>";
                return IsVuePresentation ? "vue" : "react";
            }
        }
        private NpmFunctionSymbol? _presentationFactory;

        private void ValidatePresentationAdapter()
        {
            if (IsCopelandPresentation)
            {
                if (_projectTypes.HasFlag(CopelandProjectTypeSet.ReactComponents)
                    || _projectTypes.HasFlag(CopelandProjectTypeSet.VueComponents))
                {
                    _diagnostics.Report("COPE-VIEW-0001",
                        "CopelandViews selects its adapter through PresentationAdapter. Remove framework-owned component project types from this target.", 0, 1, _sourcePath);
                }
                if (presentationAdapter is null)
                {
                    _diagnostics.Report("COPE-VIEW-0002", "CopelandViews requires an explicit PresentationAdapter target contract.", 0, 1, _sourcePath);
                }
                else if (string.IsNullOrWhiteSpace(presentationAdapter.Identity)
                    || string.IsNullOrWhiteSpace(presentationAdapter.ElementModule)
                    || string.IsNullOrWhiteSpace(presentationAdapter.ElementExport)
                    || string.IsNullOrWhiteSpace(presentationAdapter.ClassProperty)
                    || string.IsNullOrWhiteSpace(presentationAdapter.LabelForProperty)
                    || !Enum.IsDefined(presentationAdapter.Children)
                    || !Enum.IsDefined(presentationAdapter.CommitProtocol))
                {
                    _diagnostics.Report("COPE-VIEW-0002", "PresentationAdapter requires nonempty identity, factory and property conventions, and declared child/commit protocols.", 0, 1, _sourcePath);
                }
            }
            else if (presentationAdapter is not null)
            {
                _diagnostics.Report("COPE-VIEW-0001", "PresentationAdapter requires the CopelandViews project type.", 0, 1, _sourcePath);
            }
        }

        private bool TryGetPresentationFactory(out NpmFunctionSymbol factory)
        {
            if (IsCopelandPresentation)
            {
                _presentationFactory ??= _npmImports.Select(import => import.Function).FirstOrDefault(function =>
                    function.Name == "__cope_view_element"
                    && function.PackageName == PresentationPackage
                    && function.ExportName == PresentationFactoryName);
                if (_presentationFactory is not null)
                {
                    factory = _presentationFactory;
                    return true;
                }
                if (presentationAdapter is not null
                    && _npmResolver.TryGetPackage(presentationAdapter.ElementModule, out CopelandNpmPackageContract? package)
                    && package is { IsMaterialized: true, IsAvailableToJavaScript: true }
                    && package.Exports.Any(export => export.ExportName == presentationAdapter.ElementExport))
                {
                    factory = new NpmFunctionSymbol("__cope_view_element", package.PackageName, package.Version,
                        presentationAdapter.ElementExport, [], ViewTypeSymbol.Instance, null, false, true, false);
                    _presentationFactory = factory;
                    _npmImports.Add(new BoundNpmImport(factory));
                    return true;
                }
                _diagnostics.Report("COPE-VIEW-0003", "The presentation adapter factory requires a declared export in a materialized JavaScript npm contract.", 0, 1, _sourcePath);
                factory = null!;
                return false;
            }
            if (_scope.TryLookup(PresentationFactoryName, out Symbol? symbol)
                && symbol is NpmFunctionSymbol imported
                && imported.PackageName == PresentationPackage
                && imported.ExportName == PresentationFactoryName)
            {
                factory = imported;
                return true;
            }
            factory = null!;
            return false;
        }

        private bool ValidateComponentRenderer(NpmComponentSymbol component, SyntaxToken anchor)
        {
            if (component.PresentationHost && (!IsCopelandPresentation
                || component.DefaultSlot || component.Members.Count != 0
                || !component.Properties.Any(property => property.Name == "hostKey"
                    && property.Type == PrimitiveTypeSymbol.String && property.IsRequired)
                || component.Properties.Any(property => property.Name == "children"
                    || property.Name.StartsWith("__cope_", StringComparison.Ordinal)
                    || ContainsPresentationValue(property.Type, new HashSet<TypeSymbol>()))))
            {
                Report("COPE-VIEW-HOST-0001",
                    "Presentation hosts require CopelandViews, a required string hostKey, and data/event props without slots, compound members, renderer nodes, nested callbacks, or reserved __cope_ props.", anchor);
                return false;
            }
            string selected = PresentationAdapterIdentity;
            if (component.Renderer == selected) return true;
            Report("COPE-COMPONENT-RENDERER-0001",
                $"Component '{component.Name}' declares renderer '{component.Renderer}', but this presentation uses '{selected}'. Select a matching component contract; crossing frameworks requires an explicit host boundary.", anchor);
            return false;
        }

        private static bool ContainsPresentationValue(TypeSymbol type, HashSet<TypeSymbol> visited, bool nested = false)
        {
            if (nested && type is CallableTypeSymbol) return true;
            if (!visited.Add(type)) return false;
            return type switch
            {
                ViewTypeSymbol or ReactNodeTypeSymbol or VueNodeTypeSymbol or ReactRootTypeSymbol => true,
                ArrayTypeSymbol array => ContainsPresentationValue(array.ElementType, visited, true),
                CallableTypeSymbol callable => nested || callable.ReturnType != PrimitiveTypeSymbol.Void
                    || callable.Parameters.Any(parameter => ContainsPresentationValue(parameter.Type, visited, true)),
                RecordTypeSymbol record => record.Fields.Any(field => ContainsPresentationValue(field.Type, visited, true)),
                EnumTypeSymbol enumeration => enumeration.Cases.Any(@case =>
                    @case.PayloadFields.Any(field => ContainsPresentationValue(field.Type, visited, true))),
                ResultTypeSymbol result => ContainsPresentationValue(result.SuccessType, visited, true)
                    || ContainsPresentationValue(result.ErrorType, visited, true),
                AsyncTypeSymbol asyncType => ContainsPresentationValue(asyncType.EventualType, visited, true),
                _ => false,
            };
        }
    }
}
