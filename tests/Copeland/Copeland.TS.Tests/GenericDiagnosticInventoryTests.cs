using System.Text;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GenericDiagnosticInventoryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return Case("COPE-INTERFACE-0001", "interface Empty { }");
        yield return Case("COPE-INTERFACE-0002", "interface Bad { value(): number; }");
        yield return Case("COPE-INTERFACE-0003", "interface Bad { x: number; x: number; }");
        yield return Case("COPE-INTERFACE-0004", "interface Bad { x: void; }");
        yield return Case("COPE-INTERFACE-0005", "interface Positioned { x: number; } const value: Positioned = { x: 1 };");
        yield return Case("COPE-INTERFACE-0006", BuildTooManyInterfaceFieldsSource());
        yield return Case("COPE-REQUIREMENT-0001", "function use<T extends Missing>(value: T): T { return value; }");
        yield return Case("COPE-REQUIREMENT-0002", "interface Positioned { x: number; } function use<T extends Positioned & Positioned>(value: T): number { return value.x; }");
        yield return Case("COPE-REQUIREMENT-0003", "interface X { value: number; } interface Y { value: string; } function use<T extends X & Y>(value: T): number { return value.value; }");
        yield return Case("COPE-REQUIREMENT-0004", "interface Positioned { x: number; } function use<T extends Positioned>(value: T): number { return value.name; }");
        yield return Case("COPE-REQUIREMENT-0005", "interface Positioned { x: number; } function use<T extends Positioned>(value: T): number { return value.x; } const value: number = use<number>(1);");
        yield return Case("COPE-REQUIREMENT-0006", "interface Positioned { x: number; y: number; } record Point { x: number; } function use<T extends Positioned>(value: T): number { return value.x; } const value: number = use<Point>({ x: 1 });");
        yield return Case("COPE-REQUIREMENT-0007", "interface Positioned { x: number; } record Point { x: string; } function use<T extends Positioned>(value: T): number { return value.x; } const value: number = use<Point>({ x: \"bad\" });");
        yield return Case("COPE-REQUIREMENT-0008", "record Point { x: number; } function use<T extends Point>(value: T): T { return value; }");
        yield return Case("COPE-REQUIREMENT-0009", BuildTooManyRequirementsSource());
        yield return Case("COPE-REQUIREMENT-0010", BuildTooManyNormalizedFieldsSource());
        yield return Case("COPE-GENERIC-0001", "function use<T, T>(value: T): T { return value; }");
        yield return Case("COPE-GENERIC-0002", "record T { value: number; } function use<T>(value: T): T { return value; }");
        yield return Case("COPE-INFER-0001", "function discard<T>(value: number): void { } discard(1);");
        yield return Case("COPE-INFER-0002", "function same<T>(left: T, right: T): T { return left; } const answer: number = same(1, \"two\");");
        yield return Case("COPE-INFER-0003", "function shape<T>(value: T[]): T { return value[0]; } const answer: number = shape(1);");
        yield return Case("COPE-INFER-0005", BuildDepthLimitSource());
        yield return Case("COPE-INFER-0006", BuildStepLimitSource());
        yield return Case("COPE-INFER-0007", BuildEvidenceLimitSource());
        yield return Case("COPE-GENERIC-0005", "function value(input: number): number { return input; } const answer: number = value<number>(1);");
        yield return Case("COPE-GENERIC-0006", "function inner<T>(value: T): T { return value; } function outer<U>(value: U): U { return inner<U>(value); }");
        yield return Case("COPE-GENERIC-0007", "function identity<T>(value: T): T { return value; } const answer: number = identity<number, string>(1);");
        yield return Case("COPE-GENERIC-0008", "interface Positioned { x: number; } function identity<T>(value: T): T { return value; } const answer: number = identity<Positioned>(1);");
        yield return Case("COPE-GENERIC-0009", BuildTotalInstantiationLimitSource());
        yield return Case("COPE-GENERIC-0011", "function use<T0, T1, T2, T3, T4, T5, T6, T7, T8>(value: T0): T0 { return value; }");
        yield return Case("COPE-GENERIC-0012", BuildPerGenericInstantiationLimitSource());
        yield return Case("COPE-GENERIC-0014", "function loop<T>(value: T): T { return loop<T>(value); }");
        yield return Case("COPE-GENERIC-0015", BuildTypeDepthLimitSource());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_m1b_generic_or_interface_diagnostic_has_a_focused_source_and_meaningful_span(
        string diagnosticId,
        string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        var diagnostic = compilation.Diagnostics.First(candidate => candidate.Id == diagnosticId);
        Assert.True(diagnostic.Position >= 0);
        Assert.True(diagnostic.Length > 0);
    }

    private static object[] Case(string diagnosticId, string source) => [diagnosticId, source];

    private static string BuildTooManyInterfaceFieldsSource()
    {
        var source = new StringBuilder("interface Wide {");
        for (var index = 0; index < 129; index++)
        {
            source.Append(" f").Append(index).Append(": number;");
        }

        source.Append(" }");
        return source.ToString();
    }

    private static string BuildTooManyRequirementsSource()
    {
        var source = new StringBuilder();
        for (var index = 0; index < 9; index++)
        {
            source.Append("interface I").Append(index).Append(" { x").Append(index).Append(": number; }\n");
        }

        source.Append("function use<T extends ");
        for (var index = 0; index < 9; index++)
        {
            if (index > 0)
            {
                source.Append(" & ");
            }

            source.Append("I").Append(index);
        }

        source.Append(">(value: T): number { return value.x0; }");
        return source.ToString();
    }

    private static string BuildTooManyNormalizedFieldsSource()
    {
        var source = new StringBuilder();
        for (var interfaceIndex = 0; interfaceIndex < 8; interfaceIndex++)
        {
            source.Append("interface I").Append(interfaceIndex).Append(" {");
            for (var fieldIndex = 0; fieldIndex < 5; fieldIndex++)
            {
                source.Append(" f").Append(interfaceIndex).Append('_').Append(fieldIndex).Append(": number;");
            }

            source.Append(" }\n");
        }

        source.Append("function use<T extends ");
        for (var index = 0; index < 8; index++)
        {
            if (index > 0)
            {
                source.Append(" & ");
            }

            source.Append("I").Append(index);
        }

        source.Append(">(value: T): number { return value.f0_0; }");
        return source.ToString();
    }

    private static string BuildPerGenericInstantiationLimitSource()
    {
        var source = new StringBuilder("function identity<T>(value: T): T { return value; }\n");
        for (var index = 0; index < 17; index++)
        {
            source.Append("record R").Append(index).Append(" { value: number; }\n");
            source.Append("const v").Append(index).Append(": R").Append(index).Append(" = identity<R").Append(index).Append(">({ value: ").Append(index).Append(" });\n");
        }

        return source.ToString();
    }

    private static string BuildTotalInstantiationLimitSource()
    {
        var source = new StringBuilder();
        for (var functionIndex = 0; functionIndex < 9; functionIndex++)
        {
            source.Append("function f").Append(functionIndex).Append("<T>(value: T): T { return value; }\n");
            for (var instantiationIndex = 0; instantiationIndex < 15; instantiationIndex++)
            {
                source.Append("record R").Append(functionIndex).Append('_').Append(instantiationIndex).Append(" { value: number; }\n");
                source.Append("const v").Append(functionIndex).Append('_').Append(instantiationIndex).Append(": R").Append(functionIndex).Append('_').Append(instantiationIndex)
                    .Append(" = f").Append(functionIndex).Append("<R").Append(functionIndex).Append('_').Append(instantiationIndex).Append(">({ value: ")
                    .Append(instantiationIndex).Append(" });\n");
            }
        }
        return source.ToString();
    }

    private static string BuildTypeDepthLimitSource()
    {
        string nested = "number";
        for (var index = 0; index < 17; index++)
        {
            nested = "(" + nested + " ! string)";
        }

        return $"function identity<T>(value: T): T {{ return value; }} const value: {nested} = identity<{nested}>(err(\"bad\"));";
    }

    private static string BuildDepthLimitSource()
    {
        string parameterType = "T" + string.Concat(Enumerable.Repeat("[]", 17));
        string value = "42";
        for (var index = 0; index < 17; index++) value = "[" + value + "]";
        return $"function relay<T>(value: {parameterType}): void {{ }} relay({value});";
    }

    private static string BuildStepLimitSource()
    {
        string type = "string";
        string value = "\"bad\"";
        for (var index = 0; index < 7; index++)
        {
            type = "(" + type + " ! " + type + ")";
            value = "err(" + value + ")";
        }

        return $"function inspect<T>(witness: T, value: {type}): void {{ }} const source: {type} = {value}; inspect(1, source);";
    }

    private static string BuildEvidenceLimitSource()
    {
        string parameters = string.Join(", ", Enumerable.Range(0, 17).Select(index => $"value{index}: T"));
        string arguments = string.Join(", ", Enumerable.Repeat("42", 17));
        return $"function same<T>({parameters}): void {{ }} same({arguments});";
    }
}
