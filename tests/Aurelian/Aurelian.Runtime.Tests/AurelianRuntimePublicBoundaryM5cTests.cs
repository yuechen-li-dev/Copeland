using System.Reflection;
using Aurelian.Core.Engine;
using Aurelian.Runtime.Dominatus;
using Aurelian.Runtime.Sessions;
using global::Dominatus.Core.Runtime;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class AurelianRuntimePublicBoundaryM5cTests
{
    private static readonly IReadOnlySet<string> IntentionalAdvancedDominatusSymbols = new HashSet<string>(StringComparer.Ordinal)
    {
        "Aurelian.Runtime.Dominatus.AurelianRuntimeDominatusAccess.ActuatorHost",
        "Aurelian.Runtime.Dominatus.AurelianRuntimeDominatusAccess.World",
        "Aurelian.Runtime.Dominatus.AurelianRuntimeDominatusOptions.ActuatorHost",
        "Aurelian.Runtime.Dominatus.AurelianRuntimeDominatusOptions.ConfigureActuatorHost",
        "Aurelian.Runtime.Dominatus.AurelianRuntimeDominatusOptions.World",
        "Aurelian.Runtime.Dominatus.CompositorPolicyDominatus.RunOnceAsync",
        "Aurelian.Runtime.Dominatus.IAurelianDominatusWorldRunner.RunTickAsync",
        "Aurelian.Runtime.Dominatus.SequentialAurelianDominatusWorldRunner.RunTickAsync",
    };

    [Fact]
    public void AurelianRuntime_CompiledPublicSurface_HasOnlyExactAdvancedDominatusAllowlist()
    {
        IReadOnlyList<string> actual = DominatusPublicSurfaceInspector.FindLeaks(typeof(AurelianRuntimeSession).Assembly);

        Assert.Equal(
            IntentionalAdvancedDominatusSymbols.OrderBy(static symbol => symbol),
            actual.OrderBy(static symbol => symbol));
    }

    [Fact]
    public void AurelianCore_CompiledPublicSurface_HasNoDominatusTypes()
    {
        IReadOnlyList<string> actual = DominatusPublicSurfaceInspector.FindLeaks(typeof(AurelianEngine).Assembly);

        Assert.Empty(actual);
    }

    [Fact]
    public void CompiledPublicSurfaceInspector_DetectsNestedGenericDominatusLeak()
    {
        IReadOnlyList<string> actual = DominatusPublicSurfaceInspector.FindLeaks(typeof(NestedGenericDominatusLeak));

        Assert.Equal(
            ["Aurelian.Runtime.Tests.AurelianRuntimePublicBoundaryM5cTests+NestedGenericDominatusLeak.Worlds"],
            actual);
    }

    [Fact]
    public void AdvancedSurface_IsExplicitlyNamedAndSeparatedFromOrdinarySessionMembers()
    {
        PropertyInfo[] sessionProperties = typeof(AurelianRuntimeSession).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(sessionProperties, property => property.PropertyType.Namespace?.StartsWith("Dominatus.", StringComparison.Ordinal) == true);
        Assert.Equal("Aurelian.Runtime.Dominatus", typeof(AurelianRuntimeDominatusAccess).Namespace);
    }

    [Fact]
    public void CoreAndMachinaBridge_SourceAndProjectsRemainDominatusFree()
    {
        string root = GetRepositoryRoot();
        AssertNoDominatusToken(Path.Combine(root, "src", "Aurelian", "Aurelian.Core"));
        AssertNoDominatusToken(Path.Combine(root, "src", "Integrations", "Aurelian.Machina"));
    }

    private static void AssertNoDominatusToken(string directory)
    {
        foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly)))
        {
            Assert.DoesNotContain("Dominatus", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    public sealed class NestedGenericDominatusLeak
    {
        public IReadOnlyList<Dictionary<string, AiWorld>> Worlds { get; } = [];
    }
}

internal static class DominatusPublicSurfaceInspector
{
    public static IReadOnlyList<string> FindLeaks(Assembly assembly)
    {
        return assembly.GetExportedTypes()
            .SelectMany(FindLeaks)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindLeaks(Type type)
    {
        var leaks = new List<string>();
        AddTypeDeclarationLeaks(type, leaks);

        foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            AddMemberLeak(Symbol(type, constructor), constructor.GetParameters().Select(static parameter => parameter.ParameterType), [], leaks);
        }

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName))
        {
            AddMemberLeak(
                Symbol(type, method),
                [method.ReturnType, .. method.GetParameters().Select(static parameter => parameter.ParameterType)],
                method.GetGenericArguments(),
                leaks);
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            AddMemberLeak(Symbol(type, property), [property.PropertyType, .. property.GetIndexParameters().Select(static parameter => parameter.ParameterType)], [], leaks);
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            AddMemberLeak(Symbol(type, field), [field.FieldType], [], leaks);
        }

        foreach (EventInfo @event in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            AddMemberLeak(Symbol(type, @event), [@event.EventHandlerType!], [], leaks);
        }

        return leaks;
    }

    private static void AddTypeDeclarationLeaks(Type type, List<string> leaks)
    {
        if (ContainsDominatusType(type.BaseType) || type.GetInterfaces().Any(ContainsDominatusType) || type.GetGenericArguments().Any(ContainsDominatusType))
        {
            leaks.Add($"{type.FullName} [type declaration]");
        }
    }

    private static void AddMemberLeak(
        string symbol,
        IEnumerable<Type> signatureTypes,
        IEnumerable<Type> genericParameters,
        List<string> leaks)
    {
        if (signatureTypes.Any(ContainsDominatusType) || genericParameters.Any(ContainsDominatusType))
        {
            leaks.Add(symbol);
        }
    }

    private static bool ContainsDominatusType(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.Namespace?.StartsWith("Dominatus.", StringComparison.Ordinal) == true)
        {
            return true;
        }

        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            return ContainsDominatusType(type.GetElementType());
        }

        if (type.IsGenericParameter)
        {
            return type.GetGenericParameterConstraints().Any(ContainsDominatusType);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(ContainsDominatusType);
    }

    private static string Symbol(Type type, MemberInfo member) => $"{type.FullName}.{member.Name}";
}
