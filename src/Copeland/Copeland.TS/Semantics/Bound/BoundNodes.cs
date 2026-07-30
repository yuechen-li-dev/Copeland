using Copeland.TS.Syntax;

using Copeland.TS.MachinaSource;

namespace Copeland.TS.Semantics.Bound;

public abstract class BoundNode;
public abstract class BoundStatement : BoundNode;
public abstract class BoundExpression : BoundNode { public abstract TypeSymbol Type { get; } }

public sealed class BoundProgram
{
    public BoundProgram(
        IReadOnlyList<BoundFunctionDeclaration> functions,
        IReadOnlyList<BoundEnumDeclaration> enums,
        IReadOnlyList<BoundRecordDeclaration> records,
        IReadOnlyList<BoundStatement> globalStatements,
        IReadOnlyList<BoundTableDefinition>? tables = null,
        IReadOnlyList<BoundTsonEncodingPlan>? tsonEncodingPlans = null,
        IReadOnlyList<BoundNpmImport>? npmImports = null,
        IReadOnlyList<BoundNpmComponentImport>? npmComponentImports = null,
        IReadOnlyList<BoundPackageImport>? packageImports = null,
        IReadOnlyList<BoundJavaScriptHostImport>? javaScriptHostImports = null,
        IReadOnlyList<string>? csharpUsings = null,
        string? csharpSourcePath = null,
        IReadOnlyList<BoundFlowDefinition>? flows = null,
        IReadOnlyList<BoundTemplateDeclaration>? templates = null,
        IReadOnlyList<BoundLayoutDeclaration>? layouts = null,
        IReadOnlyList<BoundLayoutBinding>? layoutBindings = null,
        IReadOnlyList<BoundComponentDefinition>? componentDefinitions = null,
        IReadOnlyList<BoundComponentInstance>? componentInstances = null)
    {
        Functions = functions;
        Enums = enums;
        Records = records;
        GlobalStatements = globalStatements;
        Tables = tables ?? [];
        TsonEncodingPlans = tsonEncodingPlans ?? [];
        NpmImports = npmImports ?? [];
        NpmComponentImports = npmComponentImports ?? [];
        PackageImports = packageImports ?? [];
        JavaScriptHostImports = javaScriptHostImports ?? [];
        CSharpUsings = csharpUsings ?? [];
        CSharpSourcePath = csharpSourcePath;
        Flows = flows ?? [];
        Templates = templates ?? [];
        Layouts = layouts ?? [];
        LayoutBindings = layoutBindings ?? [];
        ComponentDefinitions = componentDefinitions ?? [];
        ComponentInstances = componentInstances ?? [];
    }
    public IReadOnlyList<BoundFunctionDeclaration> Functions { get; }
    public IReadOnlyList<BoundEnumDeclaration> Enums { get; }
    public IReadOnlyList<BoundRecordDeclaration> Records { get; }
    public IReadOnlyList<BoundStatement> GlobalStatements { get; }
    public IReadOnlyList<BoundTableDefinition> Tables { get; }
    public IReadOnlyList<BoundTsonEncodingPlan> TsonEncodingPlans { get; }
    public IReadOnlyList<BoundNpmImport> NpmImports { get; }
    public IReadOnlyList<BoundNpmComponentImport> NpmComponentImports { get; }
    public IReadOnlyList<BoundPackageImport> PackageImports { get; }
    public IReadOnlyList<BoundJavaScriptHostImport> JavaScriptHostImports { get; }
    public IReadOnlyList<string> CSharpUsings { get; }
    public string? CSharpSourcePath { get; }
    public IReadOnlyList<BoundFlowDefinition> Flows { get; }
    public IReadOnlyList<BoundTemplateDeclaration> Templates { get; }
    public IReadOnlyList<BoundLayoutDeclaration> Layouts { get; }
    public IReadOnlyList<BoundLayoutBinding> LayoutBindings { get; }
    /// <summary>Renderer-neutral capsules inferred from ordinary render functions.</summary>
    public IReadOnlyList<BoundComponentDefinition> ComponentDefinitions { get; }
    /// <summary>Concrete calls attached to compiler-owned parent layout hosts.</summary>
    public IReadOnlyList<BoundComponentInstance> ComponentInstances { get; }
}

/// <summary>
/// A component is an ordinary callable render function plus a private
/// presentation domain. React is one implementation kind, never the component
/// identity itself.
/// </summary>
public sealed class BoundComponentDefinition(
    FunctionSymbol function,
    ComponentImplementationKind implementationKind,
    BoundLayoutBinding? localStream = null,
    BoundComponentPresentation? presentation = null)
    : BoundNode
{
    public FunctionSymbol Function { get; } = function;
    public ComponentImplementationKind ImplementationKind { get; } = implementationKind;
    /// <summary>Optional private stream. Its layout boxes remain tooling facts only.</summary>
    public BoundLayoutBinding? LocalStream { get; } = localStream;
    public IReadOnlyList<BoundComponentCapture> Captures { get; } = localStream?.Captures ?? [];
    public string StableIdentity => Function.StableIdentity + "::component";
    /// <summary>
    /// Canonical renderer-neutral component result. Its payload remains private
    /// to the selected adapter; layout and identity never do.
    /// </summary>
    public BoundComponentPresentation Presentation { get; } = presentation ?? BoundComponentPresentation.ReactBridge(localStream is not null);
    /// <summary>Compatibility alias for existing capsule consumers.</summary>
    public ComponentHostCapabilities HostCapabilities => Presentation.RequiredHostCapabilities;
    public ComponentHostCapabilities RequiredHostCapabilities => Presentation.RequiredHostCapabilities;
    public ComponentContentCapabilities RequiredContentCapabilities => Presentation.RequiredContentCapabilities;
    public RendererAdapterIdentity RendererAdapter => Presentation.RendererAdapter;
}

/// <summary>
/// A presentation is Copeland's result at the renderer boundary. It identifies
/// the component and assigned host without exposing renderer node identity.
/// </summary>
public sealed record BoundComponentPresentation(
    ComponentPresentationKind Kind,
    RendererAdapterIdentity RendererAdapter,
    ComponentContentCapabilities RequiredContentCapabilities,
    ComponentHostCapabilities RequiredHostCapabilities,
    string PayloadContract)
{
    public static BoundComponentPresentation ReactBridge(bool hasPrivateLayout)
        => new(
            hasPrivateLayout ? ComponentPresentationKind.PrivateLayout : ComponentPresentationKind.RendererPayload,
            RendererAdapterIdentity.React,
            ComponentContentCapabilities.ReactSubtree,
            ComponentHostCapabilities.FillAssignedBox |
            ComponentHostCapabilities.RendererAttachment |
            ComponentHostCapabilities.StableMountPoint |
            ComponentHostCapabilities.ResolvedWidth |
            ComponentHostCapabilities.ResolvedHeight,
            hasPrivateLayout ? "private-layout/react-bridge" : "react-node-bridge");

    public static BoundComponentPresentation CustomElementBridge()
        => new(
            ComponentPresentationKind.RendererPayload,
            RendererAdapterIdentity.CustomElement,
            ComponentContentCapabilities.CustomElement,
            ComponentHostCapabilities.RendererAttachment |
            ComponentHostCapabilities.StableMountPoint |
            ComponentHostCapabilities.ResolvedWidth |
            ComponentHostCapabilities.ResolvedHeight,
            "custom-element-bridge");
}

public enum ComponentPresentationKind
{
    RendererPayload,
    PrivateLayout,
}

public enum RendererAdapterIdentity
{
    React,
    CustomElement,
    NativeMachina,
}

[Flags]
public enum ComponentContentCapabilities
{
    None = 0,
    DocumentMir = 1,
    SemanticText = 2,
    InteractiveControls = 4,
    ReactSubtree = 8,
    VueSubtree = 16,
    SvelteComponent = 32,
    CustomElement = 64,
    Canvas = 128,
    NativeMachina = 256,
}

/// <summary>One typed lexical value consumed by a component-private presentation.</summary>
public sealed class BoundComponentCapture(
    Symbol source,
    TypeSymbol type,
    ComponentCaptureKind kind,
    string stableIdentity)
{
    public Symbol Source { get; } = source;
    public TypeSymbol Type { get; } = type;
    public ComponentCaptureKind Kind { get; } = kind;
    public string StableIdentity { get; } = stableIdentity;
}

public enum ComponentCaptureKind
{
    Parameter,
    ImmutableLocal,
    ModuleConstant,
}

public sealed class BoundComponentInstance(
    string stableIdentity,
    BoundComponentDefinition definition,
    BoundLayoutBinding parentBinding,
    string parentHostBox,
    IReadOnlyList<BoundExpression> props,
    int ordinal,
    string authoredCallIdentity,
    BoundComponentInstance? parentComponentInstance,
    ComponentHostCapabilities hostCapabilities)
    : BoundNode
{
    public string StableIdentity { get; } = stableIdentity;
    public BoundComponentDefinition Definition { get; } = definition;
    public BoundLayoutBinding ParentBinding { get; } = parentBinding;
    public string ParentHostBox { get; } = parentHostBox;
    public IReadOnlyList<BoundExpression> Props { get; } = props;
    public int Ordinal { get; } = ordinal;
    /// <summary>Stable authored call fallback when no explicit collection key exists.</summary>
    public string AuthoredCallIdentity { get; } = authoredCallIdentity;
    /// <summary>Null only for a call attached directly to a page/layout host.</summary>
    public BoundComponentInstance? ParentComponentInstance { get; } = parentComponentInstance;
    public ComponentHostCapabilities HostCapabilities { get; } = hostCapabilities;
}

public enum ComponentImplementationKind
{
    NativeMachina,
    React,
    ForeignRenderer,
}

[Flags]
public enum ComponentHostCapabilities
{
    None = 0,
    FillAssignedBox = 1,
    RendererAttachment = 2,
    ResolvedWidth = 4,
    ResolvedHeight = 8,
    Scroll = 16,
    Clip = 32,
    ScrollX = 64,
    ScrollY = 128,
    FocusContainer = 256,
    StableMountPoint = 512,
}

/// <summary>
/// Adapter declarations are capability contracts, not a shared virtual DOM.
/// Individual adapters keep their renderer objects and reconciliation models
/// private behind this small ownership boundary.
/// </summary>
public sealed record RendererAdapterContract(
    RendererAdapterIdentity Identity,
    ComponentContentCapabilities SupportedContentCapabilities,
    ComponentHostCapabilities RequiredHostCapabilities,
    bool IsBrowserAdapter);

public sealed record RendererAdapterValidation(string Id, string Message);

public static class RendererAdapterContracts
{
    private static readonly IReadOnlyDictionary<RendererAdapterIdentity, RendererAdapterContract> Contracts =
        new Dictionary<RendererAdapterIdentity, RendererAdapterContract>
        {
            [RendererAdapterIdentity.React] = new(
                RendererAdapterIdentity.React,
                ComponentContentCapabilities.ReactSubtree | ComponentContentCapabilities.DocumentMir | ComponentContentCapabilities.SemanticText | ComponentContentCapabilities.InteractiveControls,
                BoundComponentPresentation.ReactBridge(false).RequiredHostCapabilities,
                true),
            [RendererAdapterIdentity.CustomElement] = new(
                RendererAdapterIdentity.CustomElement,
                ComponentContentCapabilities.CustomElement | ComponentContentCapabilities.InteractiveControls,
                ComponentHostCapabilities.RendererAttachment | ComponentHostCapabilities.StableMountPoint | ComponentHostCapabilities.ResolvedWidth | ComponentHostCapabilities.ResolvedHeight,
                true),
            [RendererAdapterIdentity.NativeMachina] = new(
                RendererAdapterIdentity.NativeMachina,
                ComponentContentCapabilities.NativeMachina | ComponentContentCapabilities.DocumentMir | ComponentContentCapabilities.SemanticText,
                ComponentHostCapabilities.FillAssignedBox | ComponentHostCapabilities.ResolvedWidth | ComponentHostCapabilities.ResolvedHeight,
                false),
        };

    public static IReadOnlyList<RendererAdapterContract> All => Contracts.Values.OrderBy(contract => contract.Identity).ToArray();

    public static bool TryGet(RendererAdapterIdentity identity, out RendererAdapterContract? contract)
        => Contracts.TryGetValue(identity, out contract);

    public static IReadOnlyList<RendererAdapterValidation> Validate(
        RendererAdapterIdentity adapter,
        ComponentContentCapabilities requiredContent,
        ComponentHostCapabilities suppliedHost)
    {
        if (!Contracts.TryGetValue(adapter, out RendererAdapterContract? contract))
        {
            return [new RendererAdapterValidation("COPE-RENDERER-0001", $"Renderer adapter '{adapter}' is unavailable.")];
        }

        ComponentContentCapabilities unsupported = requiredContent & ~contract.SupportedContentCapabilities;
        if (unsupported != ComponentContentCapabilities.None)
        {
            return [new RendererAdapterValidation("COPE-RENDERER-0002", $"Renderer adapter '{adapter}' does not support content capability '{unsupported}'.")];
        }

        ComponentHostCapabilities missing = contract.RequiredHostCapabilities & ~suppliedHost;
        return missing == ComponentHostCapabilities.None
            ? []
            : [new RendererAdapterValidation("COPE-RENDERER-0003", $"Host lacks renderer adapter '{adapter}' required capability '{missing}'.")];
    }
}

/// <summary>
/// Small deterministic ownership registry for adapter hosts. M0 keeps this as
/// a runtime-contract model so browser/native hosts can share the same failure
/// semantics without exposing renderer-internal objects to the compiler.
/// </summary>
public sealed class RendererAttachmentRegistry
{
    private readonly Dictionary<string, RendererAttachment> _attachmentsByHost = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RendererAttachment> _attachmentsByInstance = new(StringComparer.Ordinal);

    public RendererRuntimeDiagnostic? Mount(string? componentInstanceId, string hostId, RendererAdapterIdentity adapter)
    {
        if (string.IsNullOrWhiteSpace(componentInstanceId))
        {
            return Diagnostic("COPE-RENDERER-0007", adapter, componentInstanceId, "Renderer subtree requires a canonical component instance.");
        }

        if (_attachmentsByHost.TryGetValue(hostId, out RendererAttachment? existing)
            || _attachmentsByInstance.TryGetValue(componentInstanceId, out existing))
        {
            return Diagnostic("COPE-RENDERER-0004", adapter, componentInstanceId, $"Host '{hostId}' is already claimed by adapter '{existing.Adapter}' for component instance '{existing.ComponentInstanceId}'.");
        }

        var attachment = new RendererAttachment(componentInstanceId, hostId, adapter);
        _attachmentsByHost.Add(hostId, attachment);
        _attachmentsByInstance.Add(componentInstanceId, attachment);
        return null;
    }

    public RendererRuntimeDiagnostic? Update(string componentInstanceId, RendererAdapterIdentity adapter)
        => _attachmentsByInstance.ContainsKey(componentInstanceId)
            ? null
            : Diagnostic("COPE-RENDERER-0005", adapter, componentInstanceId, "Renderer update was delivered to an unmounted component instance.");

    public RendererRuntimeDiagnostic? Unmount(string componentInstanceId, RendererAdapterIdentity adapter, Action cleanup)
    {
        if (!_attachmentsByInstance.Remove(componentInstanceId, out RendererAttachment? attachment))
        {
            return Diagnostic("COPE-RENDERER-0005", adapter, componentInstanceId, "Renderer unmount was requested for an unknown component instance.");
        }

        _attachmentsByHost.Remove(attachment.HostId);
        try
        {
            cleanup();
            return null;
        }
        catch (Exception exception)
        {
            return Diagnostic("COPE-RENDERER-0006", adapter, componentInstanceId, "Renderer cleanup failed: " + exception.Message);
        }
    }

    private static RendererRuntimeDiagnostic Diagnostic(string id, RendererAdapterIdentity adapter, string? instance, string message)
        => new(id, adapter, instance, message);

    private sealed record RendererAttachment(string ComponentInstanceId, string HostId, RendererAdapterIdentity Adapter);
}

public sealed record RendererRuntimeDiagnostic(
    string Id,
    RendererAdapterIdentity Adapter,
    string? ComponentInstanceId,
    string Message);

/// <summary>
/// A validated semantic attachment between a concrete immutable layout and
/// renderable component values. It intentionally contains no resolved pixels.
/// </summary>
public sealed class BoundLayoutBinding(
    LayoutSymbol layout,
    LayoutTypeSymbol contract,
    LayoutBindingDeclarationSyntax syntax,
    FunctionSymbol runtimeFunction,
    string createElementBinding,
    BoundLayoutReactRealization realization,
    IReadOnlyList<BoundLayoutBindingEntry> entries,
    IReadOnlyList<BoundStreamCollection>? collections = null,
    IReadOnlyList<BoundComponentCapture>? captures = null,
    FunctionSymbol? owningComponent = null) : BoundNode
{
    public LayoutSymbol Layout { get; } = layout;
    public LayoutTypeSymbol Contract { get; } = contract;
    public LayoutBindingDeclarationSyntax Syntax { get; } = syntax;
    public FunctionSymbol RuntimeFunction { get; } = runtimeFunction;
    public string CreateElementBinding { get; } = createElementBinding;
    public BoundLayoutReactRealization Realization { get; } = realization;
    public IReadOnlyList<BoundLayoutBindingEntry> Entries { get; } = entries;
    public IReadOnlyList<BoundStreamCollection> Collections { get; } = collections ?? [];
    public IReadOnlyList<BoundComponentCapture> Captures { get; } = captures ?? [];
    public FunctionSymbol? OwningComponent { get; } = owningComponent;
    public bool IsPrivate => OwningComponent is not null;
}

/// <summary>Bounded ordered content attached to one named structural region.
/// Items intentionally have no authored slot identities.</summary>
public sealed class BoundStreamCollection(
    BoundLayoutNode region,
    StreamNodeSyntax syntax,
    IReadOnlyList<BoundExpression> items) : BoundNode
{
    public BoundLayoutNode Region { get; } = region;
    public StreamNodeSyntax Syntax { get; } = syntax;
    public IReadOnlyList<BoundExpression> Items { get; } = items;
}

/// <summary>
/// Compiler-owned React host plan for a concrete layout. Host nodes are an
/// explicit backend realization of declared layout boxes; component expressions
/// remain children and are never inspected or mutated for layout attachment.
/// </summary>
public sealed class BoundLayoutReactRealization(
    BoundLayoutNode root,
    IReadOnlyDictionary<string, string> classesByNode)
{
    public BoundLayoutNode Root { get; } = root;
    public IReadOnlyDictionary<string, string> ClassesByNode { get; } = classesByNode;
}

public sealed class BoundLayoutBindingEntry(
    LayoutSlotSymbol slot,
    LayoutBindingEntrySyntax syntax,
    BoundExpression component) : BoundNode
{
    public LayoutSlotSymbol Slot { get; } = slot;
    public LayoutBindingEntrySyntax Syntax { get; } = syntax;
    public BoundExpression Component { get; } = component;
}

/// <summary>
/// Static-phase declaration retained after ordinary symbol/type binding. Bodies
/// remain structural syntax until the bounded evaluator selects branches.
/// </summary>
public sealed class BoundTemplateDeclaration(TemplateSymbol symbol, TemplateDeclarationSyntax syntax) : BoundNode
{
    public TemplateSymbol Symbol { get; } = symbol;
    public TemplateDeclarationSyntax Syntax { get; } = syntax;
    public BoundTemplateBlock? Plan { get; internal set; }
    public IReadOnlyList<VariableSymbol> Parameters { get; internal set; } = [];
}

public abstract class BoundTemplateNode(SyntaxToken anchor) : BoundNode
{
    public SyntaxToken Anchor { get; } = anchor;
}

public abstract class BoundTemplateValue(SyntaxToken anchor, TypeSymbol type) : BoundTemplateNode(anchor)
{
    public TypeSymbol Type { get; } = type;
}

public sealed class BoundTemplateLiteral(SyntaxToken anchor, object? value, TypeSymbol type) : BoundTemplateValue(anchor, type)
{
    public object? Value { get; } = value;
}

public sealed class BoundTemplateArray(SyntaxToken anchor, IReadOnlyList<BoundTemplateValue> elements) : BoundTemplateValue(anchor, new ArrayTypeSymbol(elements.FirstOrDefault()?.Type ?? PrimitiveTypeSymbol.Error))
{
    public IReadOnlyList<BoundTemplateValue> Elements { get; } = elements;
}

public sealed class BoundTemplateString(SyntaxToken anchor, IReadOnlyList<BoundTemplateValue> parts) : BoundTemplateValue(anchor, PrimitiveTypeSymbol.String)
{
    public IReadOnlyList<BoundTemplateValue> Parts { get; } = parts;
}

public sealed class BoundTemplateBinary(SyntaxToken anchor, SyntaxKind operatorKind, BoundTemplateValue left, BoundTemplateValue right, TypeSymbol type) : BoundTemplateValue(anchor, type)
{
    public SyntaxKind OperatorKind { get; } = operatorKind;
    public BoundTemplateValue Left { get; } = left;
    public BoundTemplateValue Right { get; } = right;
}

public sealed class BoundTemplateRecord(SyntaxToken anchor, RecordTypeSymbol type, IReadOnlyList<BoundRecordFieldInitializer> fields) : BoundTemplateValue(anchor, type)
{
    public IReadOnlyList<BoundRecordFieldInitializer> Fields { get; } = fields;
}

public sealed class BoundTemplateStructuralObject(SyntaxToken anchor, StructuralObjectTypeSymbol type, IReadOnlyList<BoundTemplateStructuralField> fields) : BoundTemplateValue(anchor, type)
{
    public IReadOnlyList<BoundTemplateStructuralField> Fields { get; } = fields;
}

public sealed class BoundTemplateStructuralField(string name, BoundTemplateValue value)
{
    public string Name { get; } = name;
    public BoundTemplateValue Value { get; } = value;
}

public sealed class BoundTemplateMemberAccess(SyntaxToken anchor, BoundTemplateValue receiver, string memberName, TypeSymbol type) : BoundTemplateValue(anchor, type)
{
    public BoundTemplateValue Receiver { get; } = receiver;
    public string MemberName { get; } = memberName;
}

public sealed class BoundTemplateLocalReference(SyntaxToken anchor, VariableSymbol local) : BoundTemplateValue(anchor, local.Type)
{
    public VariableSymbol Local { get; } = local;
}

public enum BoundArtifactIntrinsic { Project, Directory, TextFile, SourceFile }

public sealed class BoundArtifactConstructor(SyntaxToken anchor, BoundArtifactIntrinsic intrinsic, IReadOnlyList<BoundTemplateValue> arguments, TypeSymbol resultType) : BoundTemplateValue(anchor, resultType)
{
    public BoundArtifactIntrinsic Intrinsic { get; } = intrinsic;
    public IReadOnlyList<BoundTemplateValue> Arguments { get; } = arguments;
}

public sealed class BoundTemplateInvocation(
    SyntaxToken anchor,
    TemplateSymbol template,
    IReadOnlyList<TypeSymbol> typeArguments,
    IReadOnlyList<BoundTemplateValue> arguments) : BoundTemplateValue(anchor, template.ReturnType)
{
    public TemplateSymbol Template { get; } = template;
    public IReadOnlyList<TypeSymbol> TypeArguments { get; } = typeArguments;
    public IReadOnlyList<BoundTemplateValue> Arguments { get; } = arguments;
}

public abstract class BoundTemplateStatement(SyntaxToken anchor) : BoundTemplateNode(anchor);
public sealed class BoundTemplateBlock(SyntaxToken anchor, IReadOnlyList<BoundTemplateStatement> statements) : BoundTemplateStatement(anchor)
{
    public IReadOnlyList<BoundTemplateStatement> Statements { get; } = statements;
}
public sealed class BoundTemplateEmit(SyntaxToken anchor, BoundTemplateValue value) : BoundTemplateStatement(anchor) { public BoundTemplateValue Value { get; } = value; }
public sealed class BoundTemplateLocal(SyntaxToken anchor, VariableSymbol local, BoundTemplateValue initializer) : BoundTemplateStatement(anchor) { public VariableSymbol Local { get; } = local; public BoundTemplateValue Initializer { get; } = initializer; }
public sealed class BoundStaticIf(SyntaxToken anchor, BoundTemplateValue condition, BoundTemplateStatement thenStatement, BoundTemplateStatement? elseStatement) : BoundTemplateStatement(anchor) { public BoundTemplateValue Condition { get; } = condition; public BoundTemplateStatement ThenStatement { get; } = thenStatement; public BoundTemplateStatement? ElseStatement { get; } = elseStatement; }
public sealed class BoundStaticFor(SyntaxToken anchor, VariableSymbol local, BoundTemplateArray values, BoundTemplateStatement body) : BoundTemplateStatement(anchor) { public VariableSymbol Local { get; } = local; public BoundTemplateArray Values { get; } = values; public BoundTemplateStatement Body { get; } = body; }
public sealed class BoundStaticMatchArm(BoundTemplateLiteral pattern, BoundTemplateStatement statement)
{
    public BoundTemplateLiteral Pattern { get; } = pattern;
    public BoundTemplateStatement Statement { get; } = statement;
}
public sealed class BoundStaticMatch(SyntaxToken anchor, BoundTemplateValue input, IReadOnlyList<BoundStaticMatchArm> arms) : BoundTemplateStatement(anchor)
{
    public BoundTemplateValue Input { get; } = input;
    public IReadOnlyList<BoundStaticMatchArm> Arms { get; } = arms;
}
public sealed class BoundTemplateReturn(SyntaxToken anchor, BoundTemplateValue? value) : BoundTemplateStatement(anchor) { public BoundTemplateValue? Value { get; } = value; }

public sealed class BoundNpmImport(NpmFunctionSymbol function)
{
    public NpmFunctionSymbol Function { get; } = function;
}

public sealed class BoundNpmComponentImport(NpmComponentSymbol component)
{
    public NpmComponentSymbol Component { get; } = component;
}

public sealed class BoundPackageImport(CopelandPackageFunctionSymbol function)
{
    public CopelandPackageFunctionSymbol Function { get; } = function;
}

public sealed class BoundJavaScriptHostImport(JavaScriptHostFunctionSymbol function)
{
    public JavaScriptHostFunctionSymbol Function { get; } = function;
}

public sealed class BoundTsonEncodingPlan(
    string id,
    string schemaIdentity,
    TypeSymbol rootType,
    IReadOnlyList<TypeSymbol> definitions,
    BoundTsonTablePlan? tablePlan = null)
{
    public string Id { get; } = id;
    public string SchemaIdentity { get; } = schemaIdentity;
    public TypeSymbol RootType { get; } = rootType;
    public IReadOnlyList<TypeSymbol> Definitions { get; } = definitions;
    public BoundTsonTablePlan? TablePlan { get; } = tablePlan;
}

public sealed class BoundTsonTablePlan(
    TableTypeSymbol tableType,
    int expectedRowCount,
    IReadOnlyList<BoundTsonTableColumnPlan> columns)
{
    public TableTypeSymbol TableType { get; } = tableType;
    public int ExpectedRowCount { get; } = expectedRowCount;
    public IReadOnlyList<BoundTsonTableColumnPlan> Columns { get; } = columns.ToArray();
}

public sealed class BoundTsonTableColumnPlan(
    TableColumnSymbol column,
    int expectedElementCount)
{
    public TableColumnSymbol Column { get; } = column;
    public int ExpectedElementCount { get; } = expectedElementCount;
}
public sealed class BoundCompilation
{
    public BoundCompilation(
        SyntaxTree syntaxTree,
        BoundProgram program,
        IReadOnlyList<Diagnostics.Diagnostic> diagnostics,
        BoundModuleScope? moduleScope = null,
        IReadOnlyList<MachinaSource.BoundTextDocument>? textDocuments = null)
    {
        SyntaxTree = syntaxTree;
        Program = program;
        Diagnostics = diagnostics;
        ModuleScope = moduleScope;
        TextDocuments = textDocuments ?? [];
    }
    public SyntaxTree SyntaxTree { get; }
    public BoundProgram Program { get; }
    public IReadOnlyList<Diagnostics.Diagnostic> Diagnostics { get; }
    /// <summary>Canonical documents bound from this exact parsed source snapshot.</summary>
    public IReadOnlyList<MachinaSource.BoundTextDocument> TextDocuments { get; }
    /// <summary>Compiler-owned declarations of this source module. Imported names are deliberately absent.</summary>
    public BoundModuleScope? ModuleScope { get; }
}

public sealed class BoundModuleScope(
    string moduleIdentity,
    IReadOnlyDictionary<string, Symbol> declarations,
    IReadOnlyDictionary<string, TypeAliasSymbol> aliases,
    IReadOnlyDictionary<string, InterfaceSymbol> interfaces,
    IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> genericBodies)
{
    public string ModuleIdentity { get; } = moduleIdentity;
    public IReadOnlyDictionary<string, Symbol> Declarations { get; } = declarations;
    public IReadOnlyDictionary<string, TypeAliasSymbol> Aliases { get; } = aliases;
    public IReadOnlyDictionary<string, InterfaceSymbol> Interfaces { get; } = interfaces;
    public IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> GenericBodies { get; } = genericBodies;
}

/// <summary>Names introduced into one module by resolved local imports.</summary>
public sealed class BoundModuleImports(
    IReadOnlyDictionary<string, Symbol> declarations,
    IReadOnlyDictionary<string, TypeAliasSymbol> aliases,
    IReadOnlyDictionary<string, InterfaceSymbol> interfaces,
    IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> genericBodies)
{
    public IReadOnlyDictionary<string, Symbol> Declarations { get; } = declarations;
    public IReadOnlyDictionary<string, TypeAliasSymbol> Aliases { get; } = aliases;
    public IReadOnlyDictionary<string, InterfaceSymbol> Interfaces { get; } = interfaces;
    public IReadOnlyDictionary<FunctionSymbol, BoundFunctionDeclaration> GenericBodies { get; } = genericBodies;
}

public sealed class BoundFunctionDeclaration : BoundNode { public BoundFunctionDeclaration(FunctionSymbol symbol, BoundBlockStatement body) { Symbol = symbol; Body = body; } public FunctionSymbol Symbol { get; } public BoundBlockStatement Body { get; } }
public sealed class BoundEnumDeclaration : BoundNode { public BoundEnumDeclaration(EnumTypeSymbol enumType) => EnumType = enumType; public EnumTypeSymbol EnumType { get; } }
public sealed class BoundRecordDeclaration : BoundNode { public BoundRecordDeclaration(RecordTypeSymbol recordType) => RecordType = recordType; public RecordTypeSymbol RecordType { get; } }
public sealed class BoundTableDefinition(
    TableTypeSymbol tableType,
    IReadOnlyList<BoundTableColumnDefinition> columns,
    int rowCount,
    bool isExported = false) : BoundNode
{
    public TableTypeSymbol TableType { get; } = tableType;
    public IReadOnlyList<BoundTableColumnDefinition> Columns { get; } = columns;
    public int RowCount { get; } = rowCount;
    public bool IsExported { get; } = isExported;
}

public sealed class BoundFlowDefinition(
    string name,
    string stableIdentity,
    RecordTypeSymbol boardType,
    IReadOnlyList<BoundFlowBoardField> boardFields,
    IReadOnlyList<BoundFlowEvent> events,
    IReadOnlyList<BoundFlowState> states,
    string initialState,
    TypeSymbol resultType,
    TypeSymbol? failureType)
    : BoundNode
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public RecordTypeSymbol BoardType { get; } = boardType;
    public IReadOnlyList<BoundFlowBoardField> BoardFields { get; } = boardFields;
    public IReadOnlyList<BoundFlowEvent> Events { get; } = events;
    public IReadOnlyList<BoundFlowState> States { get; } = states;
    public string InitialState { get; } = initialState;
    public TypeSymbol ResultType { get; } = resultType;
    public TypeSymbol? FailureType { get; } = failureType;
}

public sealed class BoundFlowBoardField(RecordFieldSymbol field, BoundExpression initializer) : BoundNode
{
    public RecordFieldSymbol Field { get; } = field;
    public BoundExpression Initializer { get; } = initializer;
}

public sealed class BoundFlowEvent(string name, string stableIdentity, IReadOnlyList<ParameterSymbol> parameters) : BoundNode
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public IReadOnlyList<ParameterSymbol> Parameters { get; } = parameters;
}

public sealed class BoundFlowState(string name, string stableIdentity, bool isInitial, IReadOnlyList<BoundFlowTransition> transitions, BoundFlowTerminal? terminal) : BoundNode
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public bool IsInitial { get; } = isInitial;
    public IReadOnlyList<BoundFlowTransition> Transitions { get; } = transitions;
    public BoundFlowTerminal? Terminal { get; } = terminal;
}

public sealed class BoundFlowTransition(string eventName, string targetState, BoundExpression? guard, IReadOnlyList<ParameterSymbol> bindings, IReadOnlyList<BoundFlowBoardUpdate> updates) : BoundNode
{
    public string EventName { get; } = eventName;
    public string TargetState { get; } = targetState;
    public BoundExpression? Guard { get; } = guard;
    public IReadOnlyList<ParameterSymbol> Bindings { get; } = bindings;
    public IReadOnlyList<BoundFlowBoardUpdate> Updates { get; } = updates;
}

public sealed class BoundFlowBoardUpdate(RecordFieldSymbol field, BoundExpression value) : BoundNode
{
    public RecordFieldSymbol Field { get; } = field;
    public BoundExpression Value { get; } = value;
}

public sealed class BoundFlowTerminal(bool isFailure, BoundExpression? expression) : BoundNode
{
    public bool IsFailure { get; } = isFailure;
    public BoundExpression? Expression { get; } = expression;
}
public abstract class BoundTableConstant(TypeSymbol type) : BoundNode
{
    public TypeSymbol Type { get; } = type;
}

public sealed class BoundTableLiteralConstant(object value, TypeSymbol type) : BoundTableConstant(type)
{
    public object Value { get; } = value;
}

public sealed class BoundTableArrayConstant(
    ArrayTypeSymbol arrayType,
    IReadOnlyList<BoundTableConstant> elements) : BoundTableConstant(arrayType)
{
    public ArrayTypeSymbol ArrayType { get; } = arrayType;
    public IReadOnlyList<BoundTableConstant> Elements { get; } = Array.AsReadOnly(elements.ToArray());
}

public sealed class BoundTableRecordConstant(RecordTypeSymbol recordType, IReadOnlyList<BoundTableRecordFieldConstant> fields) : BoundTableConstant(recordType)
{
    public RecordTypeSymbol RecordType { get; } = recordType;
    public IReadOnlyList<BoundTableRecordFieldConstant> Fields { get; } = fields;
}

public sealed class BoundTableRecordFieldConstant(RecordFieldSymbol field, BoundTableConstant value) : BoundNode
{
    public RecordFieldSymbol Field { get; } = field;
    public BoundTableConstant Value { get; } = value;
}

public sealed class BoundTableEnumConstant(EnumCaseSymbol @case, IReadOnlyList<BoundTableConstant> payloads) : BoundTableConstant(@case.EnumType)
{
    public EnumCaseSymbol Case { get; } = @case;
    public IReadOnlyList<BoundTableConstant> Payloads { get; } = payloads;
}

public sealed class BoundTableResultConstant(bool isOk, BoundTableConstant payload, ResultTypeSymbol type) : BoundTableConstant(type)
{
    public bool IsOk { get; } = isOk;
    public BoundTableConstant Payload { get; } = payload;
}

public sealed class BoundTableColumnDefinition(TableColumnSymbol column, IReadOnlyList<BoundTableConstant> cells) : BoundNode
{ public TableColumnSymbol Column { get; } = column; public IReadOnlyList<BoundTableConstant> Cells { get; } = cells; }
public sealed class BoundBlockStatement : BoundStatement { public BoundBlockStatement(IReadOnlyList<BoundStatement> statements) => Statements = statements; public IReadOnlyList<BoundStatement> Statements { get; } }
public sealed class BoundVariableDeclaration : BoundStatement { public BoundVariableDeclaration(VariableSymbol variable, BoundExpression initializer) { Variable = variable; Initializer = initializer; } public VariableSymbol Variable { get; } public BoundExpression Initializer { get; } }
public sealed class BoundLocalPresentationDeclaration(BoundLayoutBinding binding) : BoundStatement
{
    public BoundLayoutBinding Binding { get; } = binding;
}
public sealed class BoundResourceUsingDeclaration : BoundStatement { public BoundResourceUsingDeclaration(VariableSymbol variable, BoundExpression initializer) { Variable = variable; Initializer = initializer; } public VariableSymbol Variable { get; } public BoundExpression Initializer { get; } }
public sealed class BoundCSharpCapture(string name, TypeSymbol type)
{
    public string Name { get; } = name;
    public TypeSymbol Type { get; } = type;
}

public sealed class BoundCSharpBlockStatement(
    string bodyText,
    int sourceLine,
    TypeSymbol expectedResultType,
    IReadOnlyList<BoundCSharpCapture> captures) : BoundStatement
{
    public string BodyText { get; } = bodyText;
    public int SourceLine { get; } = sourceLine;
    public TypeSymbol ExpectedResultType { get; } = expectedResultType;
    public IReadOnlyList<BoundCSharpCapture> Captures { get; } = captures;
}
public sealed class BoundExpressionStatement : BoundStatement { public BoundExpressionStatement(BoundExpression expression) => Expression = expression; public BoundExpression Expression { get; } }
public sealed class BoundIfStatement : BoundStatement { public BoundIfStatement(BoundExpression condition, BoundStatement thenStatement, BoundStatement? elseStatement) { Condition = condition; ThenStatement = thenStatement; ElseStatement = elseStatement; } public BoundExpression Condition { get; } public BoundStatement ThenStatement { get; } public BoundStatement? ElseStatement { get; } }
public sealed class BoundWhileStatement : BoundStatement { public BoundWhileStatement(BoundExpression condition, BoundStatement body) { Condition = condition; Body = body; } public BoundExpression Condition { get; } public BoundStatement Body { get; } }
public sealed class BoundForStatement : BoundStatement { public BoundForStatement(BoundStatement? initializer, BoundExpression? condition, BoundExpression? increment, BoundStatement body) { Initializer = initializer; Condition = condition; Increment = increment; Body = body; } public BoundStatement? Initializer { get; } public BoundExpression? Condition { get; } public BoundExpression? Increment { get; } public BoundStatement Body { get; } }
public sealed class BoundForOfStatement : BoundStatement { public BoundForOfStatement(VariableSymbol variable, BoundExpression iterable, BoundStatement body) { Variable = variable; Iterable = iterable; Body = body; } public VariableSymbol Variable { get; } public BoundExpression Iterable { get; } public BoundStatement Body { get; } }
public sealed class BoundReturnStatement : BoundStatement { public BoundReturnStatement(BoundExpression? expression) => Expression = expression; public BoundExpression? Expression { get; } }
public sealed class BoundYieldStatement : BoundStatement { public BoundYieldStatement(BoundExpression? expression, bool isDelegating = false) { Expression = expression; IsDelegating = isDelegating; } public BoundExpression? Expression { get; } public bool IsDelegating { get; } }
public sealed class BoundBreakStatement : BoundStatement;
public sealed class BoundContinueStatement : BoundStatement;

public sealed class BoundLiteralExpression : BoundExpression { public BoundLiteralExpression(object? value, TypeSymbol type) { Value = value; TypeImpl = type; } public object? Value { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundVariableExpression : BoundExpression { public BoundVariableExpression(VariableSymbol variable) => Variable = variable; public VariableSymbol Variable { get; } public override TypeSymbol Type => Variable.Type; }
public sealed class BoundAssignmentExpression : BoundExpression { public BoundAssignmentExpression(VariableSymbol variable, BoundExpression expression) { Variable = variable; Expression = expression; } public VariableSymbol Variable { get; } public BoundExpression Expression { get; } public override TypeSymbol Type => Expression.Type; }
public sealed class BoundUnaryExpression : BoundExpression { public BoundUnaryExpression(SyntaxKind op, BoundExpression operand, TypeSymbol type) { OperatorKind = op; Operand = operand; TypeImpl = type; } public SyntaxKind OperatorKind { get; } public BoundExpression Operand { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundAwaitExpression : BoundExpression { public BoundAwaitExpression(BoundExpression operand, TypeSymbol type) { Operand = operand; TypeImpl = type; } public BoundExpression Operand { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundBinaryExpression : BoundExpression { public BoundBinaryExpression(BoundExpression left, SyntaxKind op, BoundExpression right, TypeSymbol type) { Left = left; OperatorKind = op; Right = right; TypeImpl = type; } public BoundExpression Left { get; } public SyntaxKind OperatorKind { get; } public BoundExpression Right { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public enum BoundNumericConversionKind { StringFrom, IntToFloat, IntFloor, IntCeil, IntRound, IntTruncate }
/// <summary>Compiler-owned numeric and canonical formatting conversion.</summary>
public sealed class BoundNumericConversionExpression(BoundNumericConversionKind kind, BoundExpression operand, TypeSymbol type) : BoundExpression
{
    public BoundNumericConversionKind Kind { get; } = kind;
    public BoundExpression Operand { get; } = operand;
    public override TypeSymbol Type { get; } = type;
}
public sealed class BoundCallExpression : BoundExpression { public BoundCallExpression(FunctionSymbol function, IReadOnlyList<BoundExpression> arguments) { Function = function; Arguments = arguments; } public FunctionSymbol Function { get; } public IReadOnlyList<BoundExpression> Arguments { get; } public override TypeSymbol Type => Function.InvocationReturnType; }
public sealed class BoundNpmCallExpression : BoundExpression
{
    public BoundNpmCallExpression(NpmFunctionSymbol function, IReadOnlyList<BoundExpression> arguments, BoundRecordConstructionExpression argumentTuple, BoundTsonEncodingPlan requestPlan, BoundTsonEncodingPlan responsePlan, BoundTsonEncodingPlan remoteErrorPlan, RecordFieldSymbol responseValueField, RecordFieldSymbol remoteErrorValueField) { Function = function; Arguments = arguments; ArgumentTuple = argumentTuple; RequestPlan = requestPlan; ResponsePlan = responsePlan; RemoteErrorPlan = remoteErrorPlan; ResponseValueField = responseValueField; RemoteErrorValueField = remoteErrorValueField; }
    public NpmFunctionSymbol Function { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public BoundRecordConstructionExpression ArgumentTuple { get; }
    public BoundTsonEncodingPlan RequestPlan { get; }
    public BoundTsonEncodingPlan ResponsePlan { get; }
    public BoundTsonEncodingPlan RemoteErrorPlan { get; }
    public RecordFieldSymbol ResponseValueField { get; }
    public RecordFieldSymbol RemoteErrorValueField { get; }
    public override TypeSymbol Type => Function.InvocationReturnType;
}
public sealed class BoundNpmDirectCallExpression : BoundExpression
{
    public BoundNpmDirectCallExpression(NpmFunctionSymbol function, IReadOnlyList<BoundExpression> arguments)
    {
        Function = function;
        Arguments = arguments;
    }

    public NpmFunctionSymbol Function { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public override TypeSymbol Type => Function.ResultType;
}
public sealed class BoundNpmComponentValueExpression(NpmComponentSymbol component) : BoundExpression
{
    public NpmComponentSymbol Component { get; } = component;
    public override TypeSymbol Type => new NpmComponentNamespaceTypeSymbol(Component);
}

public sealed class BoundNpmComponentMemberExpression(NpmComponentMemberSymbol component) : BoundExpression
{
    public NpmComponentMemberSymbol Component { get; } = component;
    public override TypeSymbol Type => new ReactComponentTypeSymbol(Component);
}
public sealed class BoundReactElementExpression(
    string createElementBinding,
    BoundExpression elementType,
    bool isIntrinsic,
    IReadOnlyList<BoundReactProperty> properties,
    IReadOnlyList<BoundExpression> children) : BoundExpression
{
    public string CreateElementBinding { get; } = createElementBinding;
    public BoundExpression ElementType { get; } = elementType;
    public bool IsIntrinsic { get; } = isIntrinsic;
    public IReadOnlyList<BoundReactProperty> Properties { get; } = properties;
    public IReadOnlyList<BoundExpression> Children { get; } = children;
    public override TypeSymbol Type => ReactNodeTypeSymbol.Instance;
}

public sealed class BoundReactProperty(string name, BoundExpression value)
{
    public string Name { get; } = name;
    public BoundExpression Value { get; } = value;
}

public sealed class BoundReactRootRenderExpression(BoundExpression root, BoundExpression node) : BoundExpression
{
    public BoundExpression Root { get; } = root;
    public BoundExpression Node { get; } = node;
    public override TypeSymbol Type => PrimitiveTypeSymbol.Void;
}

public sealed class BoundReactRootUnmountExpression(BoundExpression root) : BoundExpression
{
    public BoundExpression Root { get; } = root;
    public override TypeSymbol Type => PrimitiveTypeSymbol.Void;
}
public sealed class BoundJavaScriptHostCallExpression(JavaScriptHostFunctionSymbol function, IReadOnlyList<BoundExpression> arguments) : BoundExpression
{
    public JavaScriptHostFunctionSymbol Function { get; } = function;
    public IReadOnlyList<BoundExpression> Arguments { get; } = arguments;
    public override TypeSymbol Type => Function.ReturnType;
}
public sealed class BoundClrInvocationExpression : BoundExpression
{
    public BoundClrInvocationExpression(System.Reflection.MethodBase member, BoundExpression? receiver, IReadOnlyList<TypeSymbol> genericArguments, IReadOnlyList<BoundExpression> arguments, TypeSymbol type)
    {
        Member = member;
        Receiver = receiver;
        Arguments = arguments;
        GenericArguments = genericArguments;
        TypeImpl = type;
    }

    public System.Reflection.MethodBase Member { get; }
    public BoundExpression? Receiver { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public IReadOnlyList<TypeSymbol> GenericArguments { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}

public sealed class BoundClrPropertyAccessExpression : BoundExpression
{
    public BoundClrPropertyAccessExpression(System.Reflection.PropertyInfo property, BoundExpression? receiver, TypeSymbol type)
    {
        Property = property;
        Receiver = receiver;
        TypeImpl = type;
    }

    public System.Reflection.PropertyInfo Property { get; }
    public BoundExpression? Receiver { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundFunctionReferenceExpression : BoundExpression { public BoundFunctionReferenceExpression(FunctionSymbol function) { Function = function; } public FunctionSymbol Function { get; } public override TypeSymbol Type => Function.CallableType; }
public sealed class BoundCallableConstructionExpression : BoundExpression
{
    public BoundCallableConstructionExpression(FunctionSymbol code, IReadOnlyList<BoundExpression> captures, CallableTypeSymbol callableType)
    {
        Code = code;
        Captures = captures;
        CallableType = callableType;
    }

    public FunctionSymbol Code { get; }
    public IReadOnlyList<BoundExpression> Captures { get; }
    public CallableTypeSymbol CallableType { get; }
    public override TypeSymbol Type => CallableType;
}
public sealed class BoundInvokeExpression : BoundExpression { public BoundInvokeExpression(BoundExpression callee, IReadOnlyList<BoundExpression> arguments, CallableTypeSymbol callableType) { Callee = callee; Arguments = arguments; CallableType = callableType; } public BoundExpression Callee { get; } public IReadOnlyList<BoundExpression> Arguments { get; } public CallableTypeSymbol CallableType { get; } public override TypeSymbol Type => CallableType.ReturnType; }
public sealed class BoundEnumValueExpression : BoundExpression
{
    public BoundEnumValueExpression(EnumCaseSymbol @case, IReadOnlyList<BoundExpression> arguments)
    {
        Case = @case;
        Arguments = arguments;
    }
    public EnumCaseSymbol Case { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public bool IsConstructor => Arguments.Count > 0;
    public override TypeSymbol Type => Case.EnumType;
}
public readonly record struct BoundHandlerId(int Value)
{
    public override string ToString() => $"h{Value}";
}

public abstract record BoundPropagationTarget
{
    public sealed record FunctionReturn : BoundPropagationTarget;
    public sealed record LexicalExcept(BoundHandlerId HandlerId) : BoundPropagationTarget;
}
public sealed class BoundPropagateExpression : BoundExpression
{
    public BoundPropagateExpression(BoundExpression operand, ResultTypeSymbol resultType, BoundPropagationTarget target)
    {
        Operand = operand;
        ResultType = resultType;
        Target = target;
    }

    public BoundExpression Operand { get; }
    public ResultTypeSymbol ResultType { get; }
    public BoundPropagationTarget Target { get; }
    public override TypeSymbol Type => ResultType.SuccessType;
}
public sealed class BoundUnwrapExpression : BoundExpression
{
    public BoundUnwrapExpression(BoundExpression operand, ResultTypeSymbol resultType)
    {
        Operand = operand;
        ResultType = resultType;
    }

    public BoundExpression Operand { get; }
    public ResultTypeSymbol ResultType { get; }
    public override TypeSymbol Type => ResultType.SuccessType;
}
public sealed class BoundValueBlock
{
    public BoundValueBlock(IReadOnlyList<BoundStatement> prefixStatements, BoundExpression valueExpression)
    {
        PrefixStatements = prefixStatements;
        ValueExpression = valueExpression;
    }

    public IReadOnlyList<BoundStatement> PrefixStatements { get; }
    public BoundExpression ValueExpression { get; }
    public TypeSymbol Type => ValueExpression.Type;
}

public sealed class BoundBatchExpression(
    BoundExpression input,
    VariableSymbol item,
    BoundValueBlock body,
    ArrayTypeSymbol type) : BoundExpression
{
    public BoundExpression Input { get; } = input;
    public VariableSymbol Item { get; } = item;
    public BoundValueBlock Body { get; } = body;
    public override TypeSymbol Type { get; } = type;
}

public sealed class BoundTryExceptExpression : BoundExpression
{
    public BoundTryExceptExpression(
        BoundHandlerId handlerId,
        BoundValueBlock protectedBlock,
        VariableSymbol handlerBinding,
        TypeSymbol handledErrorType,
        BoundValueBlock handlerBlock,
        TypeSymbol type)
    {
        HandlerId = handlerId;
        Protected = protectedBlock;
        HandlerBinding = handlerBinding;
        HandledErrorType = handledErrorType;
        Handler = handlerBlock;
        TypeImpl = type;
    }

    public BoundHandlerId HandlerId { get; }
    public BoundValueBlock Protected { get; }
    public VariableSymbol HandlerBinding { get; }
    public TypeSymbol HandledErrorType { get; }
    public BoundValueBlock Handler { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundOkExpression : BoundExpression { public BoundOkExpression(BoundExpression payload, ResultTypeSymbol type) { Payload = payload; TypeImpl = type; } public BoundExpression Payload { get; } private ResultTypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundErrExpression : BoundExpression { public BoundErrExpression(BoundExpression payload, ResultTypeSymbol type) { Payload = payload; TypeImpl = type; } public BoundExpression Payload { get; } private ResultTypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundUnitExpression : BoundExpression { public override TypeSymbol Type => PrimitiveTypeSymbol.Void; }
public sealed class BoundMatchArm
{
    public BoundMatchArm(EnumCaseSymbol @case, IReadOnlyList<VariableSymbol> payloadVariables, BoundExpression expression)
    {
        Case = @case;
        PayloadVariables = payloadVariables;
        Expression = expression;
    }
    public EnumCaseSymbol Case { get; }
    public IReadOnlyList<VariableSymbol> PayloadVariables { get; }
    public BoundExpression Expression { get; }
}
public sealed class BoundIfExpression : BoundExpression { public BoundIfExpression(BoundExpression condition, BoundExpression thenExpression, BoundExpression elseExpression, TypeSymbol type) { Condition = condition; ThenExpression = thenExpression; ElseExpression = elseExpression; TypeImpl = type; } public BoundExpression Condition { get; } public BoundExpression ThenExpression { get; } public BoundExpression ElseExpression { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundTsonEncodeExpression(BoundExpression operand, BoundTsonEncodingPlan plan, ResultTypeSymbol resultType) : BoundExpression
{
    public BoundExpression Operand { get; } = operand;
    public BoundTsonEncodingPlan Plan { get; } = plan;
    public ResultTypeSymbol ResultType { get; } = resultType;
    public override TypeSymbol Type => ResultType;
}
public sealed class BoundTsonTransportExpression(
    BoundExpression operation,
    BoundExpression request,
    BoundTsonEncodingPlan requestPlan,
    BoundTsonEncodingPlan responsePlan,
    BoundTsonEncodingPlan remoteErrorPlan,
    AsyncTypeSymbol type) : BoundExpression
{
    public BoundExpression Operation { get; } = operation;
    public BoundExpression Request { get; } = request;
    public BoundTsonEncodingPlan RequestPlan { get; } = requestPlan;
    public BoundTsonEncodingPlan ResponsePlan { get; } = responsePlan;
    public BoundTsonEncodingPlan RemoteErrorPlan { get; } = remoteErrorPlan;
    public override TypeSymbol Type { get; } = type;
}
internal sealed class BoundSyntheticTypeExpression(TypeSymbol type) : BoundExpression
{
    public override TypeSymbol Type { get; } = type;
}
public sealed class BoundMatchExpression : BoundExpression
{
    public BoundMatchExpression(BoundExpression scrutinee, EnumTypeSymbol enumType, IReadOnlyList<BoundMatchArm> arms, TypeSymbol type)
    {
        Scrutinee = scrutinee;
        EnumType = enumType;
        Arms = arms;
        TypeImpl = type;
    }
    public BoundExpression Scrutinee { get; }
    public EnumTypeSymbol EnumType { get; }
    public IReadOnlyList<BoundMatchArm> Arms { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundResultMatchExpression : BoundExpression
{
    public BoundResultMatchExpression(BoundExpression scrutinee, VariableSymbol okVariable, BoundExpression okExpression, VariableSymbol errVariable, BoundExpression errExpression, TypeSymbol type)
    {
        Scrutinee = scrutinee;
        OkVariable = okVariable;
        OkExpression = okExpression;
        ErrVariable = errVariable;
        ErrExpression = errExpression;
        TypeImpl = type;
    }

    public BoundExpression Scrutinee { get; }
    public VariableSymbol OkVariable { get; }
    public BoundExpression OkExpression { get; }
    public VariableSymbol ErrVariable { get; }
    public BoundExpression ErrExpression { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundArrayExpression : BoundExpression { public BoundArrayExpression(IReadOnlyList<BoundExpression> elements, TypeSymbol type) { Elements = elements; TypeImpl = type; } public IReadOnlyList<BoundExpression> Elements { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundArrayLengthExpression(BoundExpression receiver) : BoundExpression
{
    public BoundExpression Receiver { get; } = receiver;
    public override TypeSymbol Type => PrimitiveTypeSymbol.Int;
}
public sealed class BoundArrayElementAccessExpression(BoundExpression receiver, BoundExpression index, ArrayTypeSymbol arrayType) : BoundExpression
{
    public BoundExpression Receiver { get; } = receiver;
    public BoundExpression Index { get; } = index;
    public ArrayTypeSymbol ArrayType { get; } = arrayType;
    public override TypeSymbol Type => ArrayType.ElementType;
}
public sealed class BoundArrayIterableExpression(BoundExpression receiver, IterableTypeSymbol type) : BoundExpression
{
    public BoundExpression Receiver { get; } = receiver;
    public override TypeSymbol Type { get; } = type;
}
public sealed class BoundRecordFieldInitializer(RecordFieldSymbol field, BoundExpression value)
{
    public RecordFieldSymbol Field { get; } = field;
    public BoundExpression Value { get; } = value;
}
public sealed class BoundRecordConstructionExpression(RecordTypeSymbol recordType, IReadOnlyList<BoundRecordFieldInitializer> initializers) : BoundExpression
{
    public RecordTypeSymbol RecordType { get; } = recordType;
    public IReadOnlyList<BoundRecordFieldInitializer> Initializers { get; } = initializers;
    public override TypeSymbol Type => RecordType;
}
public sealed class BoundRecordFieldAccessExpression(BoundExpression receiver, RecordTypeSymbol recordType, RecordFieldSymbol field) : BoundExpression
{
    public BoundExpression Receiver { get; } = receiver;
    public RecordTypeSymbol RecordType { get; } = recordType;
    public RecordFieldSymbol Field { get; } = field;
    public override TypeSymbol Type => Field.Type;
}
public sealed class BoundRequirementFieldAccessExpression(BoundExpression receiver, TypeParameterTypeSymbol typeParameter, RequirementFieldSymbol field) : BoundExpression
{
    public BoundExpression Receiver { get; } = receiver;
    public TypeParameterTypeSymbol TypeParameter { get; } = typeParameter;
    public RequirementFieldSymbol Field { get; } = field;
    public override TypeSymbol Type => Field.Type;
}
public sealed class BoundTableReferenceExpression(TableTypeSymbol tableType) : BoundExpression { public TableTypeSymbol TableType { get; } = tableType; public override TypeSymbol Type => TableType; }
public sealed class BoundTableColumnAccessExpression(BoundExpression receiver, TableTypeSymbol tableType, TableColumnSymbol column) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public TableTypeSymbol TableType { get; } = tableType; public TableColumnSymbol Column { get; } = column; public override TypeSymbol Type => new ColumnTypeSymbol(Column.Type); }
public sealed class BoundTableRowAccessExpression(BoundExpression receiver, BoundExpression index, TableTypeSymbol tableType, ResultTypeSymbol type) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public BoundExpression Index { get; } = index; public TableTypeSymbol TableType { get; } = tableType; private ResultTypeSymbol TypeImpl { get; } = type; public override TypeSymbol Type => TypeImpl; }
public sealed class BoundColumnElementAccessExpression(BoundExpression receiver, BoundExpression index, ResultTypeSymbol type) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public BoundExpression Index { get; } = index; private ResultTypeSymbol TypeImpl { get; } = type; public override TypeSymbol Type => TypeImpl; }
public sealed class BoundTableRowFieldAccessExpression(BoundExpression receiver, TableRowTypeSymbol rowType, TableRowFieldSymbol field) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public TableRowTypeSymbol RowType { get; } = rowType; public TableRowFieldSymbol Field { get; } = field; public override TypeSymbol Type => Field.Type; }
public sealed class BoundTableRowsExpression(BoundExpression table, TableTypeSymbol tableType) : BoundExpression
{
    public BoundExpression Table { get; } = table;
    public TableTypeSymbol TableType { get; } = tableType;
    public override TypeSymbol Type => new TableRowsTypeSymbol(TableType);
}
public sealed class BoundTableWhereExpression(BoundExpression source, TableTypeSymbol tableType, IReadOnlyList<BoundExpression> predicates) : BoundExpression
{
    public BoundExpression Source { get; } = source;
    public TableTypeSymbol TableType { get; } = tableType;
    public IReadOnlyList<BoundExpression> Predicates { get; } = predicates;
    public override TypeSymbol Type => new TableRowsTypeSymbol(TableType);
}
public sealed class BoundTableSelectExpression(BoundExpression source, TableTypeSymbol tableType, BoundExpression projector, ArrayTypeSymbol type) : BoundExpression
{
    public BoundExpression Source { get; } = source;
    public TableTypeSymbol TableType { get; } = tableType;
    public BoundExpression Projector { get; } = projector;
    private ArrayTypeSymbol TypeImpl { get; } = type;
    public override TypeSymbol Type => TypeImpl;
}
public enum TableAggregateKind { Sum, Average, Min, Max, Count }
public sealed class BoundTableAggregateExpression(BoundExpression receiver, TableTypeSymbol tableType, TableColumnSymbol column, TableAggregateKind kind, TypeSymbol type) : BoundExpression
{
    public BoundExpression Receiver { get; } = receiver;
    public TableTypeSymbol TableType { get; } = tableType;
    public TableColumnSymbol Column { get; } = column;
    public TableAggregateKind Kind { get; } = kind;
    private TypeSymbol TypeImpl { get; } = type;
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundTableColumnReplacement(TableColumnSymbol column, BoundArrayExpression value) : BoundNode
{
    public TableColumnSymbol Column { get; } = column;
    public BoundArrayExpression Value { get; } = value;
}
public sealed class BoundTableWithExpression(
    BoundExpression source,
    TableTypeSymbol tableType,
    IReadOnlyList<BoundTableColumnReplacement> replacements) : BoundExpression
{
    public BoundExpression Source { get; } = source;
    public TableTypeSymbol TableType { get; } = tableType;
    public IReadOnlyList<BoundTableColumnReplacement> Replacements { get; } = replacements;
    public override TypeSymbol Type => TableType;
}
public sealed class BoundRecordWithExpression(BoundExpression source, RecordTypeSymbol recordType, IReadOnlyList<BoundRecordFieldInitializer> replacements) : BoundExpression
{
    public BoundExpression Source { get; } = source;
    public RecordTypeSymbol RecordType { get; } = recordType;
    public IReadOnlyList<BoundRecordFieldInitializer> Replacements { get; } = replacements;
    public override TypeSymbol Type => RecordType;
}
public sealed class BoundErrorExpression : BoundExpression { public override TypeSymbol Type => PrimitiveTypeSymbol.Error; }
