namespace Copeland.TS.Mir;

/// <summary>
/// Target-owned projection of Copeland View values. Adapter identity is an open
/// string, not a list of frameworks. Factory and property conventions belong
/// to the adapter; source application code never imports the factory.
/// </summary>
public sealed record MirPresentationAdapter(
    string Identity,
    string ElementModule,
    string ElementExport,
    MirPresentationChildren Children,
    string ClassProperty,
    string LabelForProperty,
    MirPresentationCommitProtocol CommitProtocol = MirPresentationCommitProtocol.None)
{
    public static MirPresentationAdapter React { get; } = new(
        "react", "react", "createElement", MirPresentationChildren.Variadic,
        "className", "htmlFor", MirPresentationCommitProtocol.ReactHooks);

    public static MirPresentationAdapter Vue { get; } = new(
        "vue", "vue", "h", MirPresentationChildren.DefaultSlot,
        "class", "for", MirPresentationCommitProtocol.VueComposition);
}

public enum MirPresentationChildren
{
    Variadic,
    Array,
    DefaultSlot,
}

/// <summary>
/// Qualified lifecycle protocols, not framework identities. A new adapter may
/// project stateless views immediately; stateful views require a qualified
/// commit protocol so effect acknowledgement is never guessed.
/// </summary>
public enum MirPresentationCommitProtocol
{
    None,
    ReactHooks,
    VueComposition,
}
