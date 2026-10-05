using System.Reflection;

namespace Copeland.TS.Compiler;

/// <summary>
/// An already-built CLR assembly made available to the Copeland binder. Package
/// restore and project graph discovery deliberately remain outside CTS-CLR-M1.
/// </summary>
public sealed record CopelandClrReference(
    string? AssemblyPath,
    Assembly? DeclarationAssembly = null,
    bool IncludeInternalSymbols = false);

/// <summary>
/// Compiler-time metadata source for the bounded CLR interop surface. It is not
/// emitted into generated applications and does not participate in runtime
/// dispatch.
/// </summary>
public sealed class CopelandClrMetadataResolver
{
    private readonly IReadOnlyList<Assembly> _assemblies;
    private readonly IReadOnlyDictionary<Assembly, bool> _internalVisibility;

    public CopelandClrMetadataResolver(IReadOnlyList<CopelandClrReference> references)
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var internalVisibility = new Dictionary<Assembly, bool>();
        AddAssemblies(assemblies, internalVisibility, AppDomain.CurrentDomain.GetAssemblies(), includeInternal: false);
        LoadFrameworkAssembly(assemblies, internalVisibility, "System.Runtime");
        LoadFrameworkAssembly(assemblies, internalVisibility, "System.Text.Json");

        foreach (CopelandClrReference reference in references)
        {
            try
            {
                Assembly assembly = reference.DeclarationAssembly
                    ?? Assembly.LoadFrom(reference.AssemblyPath ?? throw new InvalidOperationException("CLR reference has no assembly source."));
                AddAssemblies(assemblies, internalVisibility, [assembly], reference.IncludeInternalSymbols);
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                // The binder turns this availability failure into an authored
                // diagnostic instead of attempting a fallback resolution path.
            }
        }

        _assemblies = assemblies.Values.ToArray();
        _internalVisibility = internalVisibility;
    }

    public IReadOnlyList<Type> FindTypesInNamespace(string @namespace)
        => EnumeratePublicTypes()
            .Where(type => string.Equals(type.Namespace, @namespace, StringComparison.Ordinal))
            .ToArray();

    public IReadOnlyList<Type> FindTypes(string fullName)
        => EnumeratePublicTypes()
            .Where(type => string.Equals(type.FullName, fullName, StringComparison.Ordinal)
                || string.Equals(type.FullName?.Replace('+', '.'), fullName, StringComparison.Ordinal))
            .ToArray();

    public IReadOnlyList<Type> FindTypes(string assemblyIdentity, string fullName)
        => FindTypes(fullName)
            .Where(type => string.Equals(type.Assembly.GetName().Name, assemblyIdentity, StringComparison.Ordinal))
            .ToArray();

    /// <summary>Bounded metadata query for editor hosts; it returns only visible types.</summary>
    public IReadOnlyList<Type> FindTypesBySimpleName(string name)
        => EnumeratePublicTypes()
            .Where(type => string.Equals(type.Name, name, StringComparison.Ordinal))
            .ToArray();

    /// <summary>Returns immediate namespace/type children without exposing a raw assembly dump.</summary>
    public IReadOnlyList<string> FindNamespaceChildren(string @namespace)
    {
        string prefix = string.IsNullOrEmpty(@namespace) ? string.Empty : @namespace + ".";
        return EnumeratePublicTypes()
            .Select(type => type.Namespace)
            .Where(name => name is not null && name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name![prefix.Length..].Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
    }

    public bool IsTypeVisible(Type type)
    {
        bool includeInternal = _internalVisibility.TryGetValue(type.Assembly, out bool value) && value;
        for (Type? current = type; current is not null; current = current.DeclaringType)
        {
            if (current.IsPublic || current.IsNestedPublic)
            {
                continue;
            }

            if (!includeInternal || !(current.IsNotPublic || current.IsNestedAssembly))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsMemberVisible(MethodBase member)
        => member.IsPublic
            || (_internalVisibility.TryGetValue(member.DeclaringType!.Assembly, out bool includeInternal)
                && includeInternal
                && member.IsAssembly);

    private IEnumerable<Type> EnumeratePublicTypes()
    {
        foreach (Assembly assembly in _assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }

            foreach (Type type in types)
            {
                if (IsTypeVisible(type))
                {
                    yield return type;
                }
            }
        }
    }

    private static void AddAssemblies(
        Dictionary<string, Assembly> target,
        Dictionary<Assembly, bool> internalVisibility,
        IEnumerable<Assembly> assemblies,
        bool includeInternal)
    {
        foreach (Assembly assembly in assemblies)
        {
            string identity = assembly.FullName ?? assembly.GetName().Name ?? Guid.NewGuid().ToString("N");
            target.TryAdd(identity, assembly);
            internalVisibility[assembly] = internalVisibility.TryGetValue(assembly, out bool existing)
                ? existing || includeInternal
                : includeInternal;
        }
    }

    private static void LoadFrameworkAssembly(
        Dictionary<string, Assembly> target,
        Dictionary<Assembly, bool> internalVisibility,
        string name)
    {
        try
        {
            AddAssemblies(target, internalVisibility, [Assembly.Load(name)], includeInternal: false);
        }
        catch (FileNotFoundException)
        {
            // The normal binding diagnostic identifies unavailable framework
            // symbols; no npm or source-module fallback is attempted.
        }
    }
}
