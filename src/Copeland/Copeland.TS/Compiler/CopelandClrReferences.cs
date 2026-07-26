using System.Reflection;

namespace Copeland.TS.Compiler;

/// <summary>
/// An already-built CLR assembly made available to the Copeland binder. Package
/// restore and project graph discovery deliberately remain outside CTS-CLR-M1.
/// </summary>
public sealed record CopelandClrReference(string AssemblyPath);

/// <summary>
/// Compiler-time metadata source for the bounded CLR interop surface. It is not
/// emitted into generated applications and does not participate in runtime
/// dispatch.
/// </summary>
public sealed class CopelandClrMetadataResolver
{
    private readonly IReadOnlyList<Assembly> _assemblies;

    public CopelandClrMetadataResolver(IReadOnlyList<CopelandClrReference> references)
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        AddAssemblies(assemblies, AppDomain.CurrentDomain.GetAssemblies());
        LoadFrameworkAssembly(assemblies, "System.Runtime");
        LoadFrameworkAssembly(assemblies, "System.Text.Json");

        foreach (CopelandClrReference reference in references)
        {
            try
            {
                AddAssemblies(assemblies, [Assembly.LoadFrom(reference.AssemblyPath)]);
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                // The binder turns this availability failure into an authored
                // diagnostic instead of attempting a fallback resolution path.
            }
        }

        _assemblies = assemblies.Values.ToArray();
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

    private IEnumerable<Type> EnumeratePublicTypes()
    {
        foreach (Assembly assembly in _assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }

            foreach (Type type in types)
            {
                if (type.IsPublic || type.IsNestedPublic)
                {
                    yield return type;
                }
            }
        }
    }

    private static void AddAssemblies(Dictionary<string, Assembly> target, IEnumerable<Assembly> assemblies)
    {
        foreach (Assembly assembly in assemblies)
        {
            string identity = assembly.FullName ?? assembly.GetName().Name ?? Guid.NewGuid().ToString("N");
            target.TryAdd(identity, assembly);
        }
    }

    private static void LoadFrameworkAssembly(Dictionary<string, Assembly> target, string name)
    {
        try
        {
            AddAssemblies(target, [Assembly.Load(name)]);
        }
        catch (FileNotFoundException)
        {
            // The normal binding diagnostic identifies unavailable framework
            // symbols; no npm or source-module fallback is attempted.
        }
    }
}
