using System.Globalization;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private readonly Dictionary<string, VdMirStorageLayout> storageLayouts = new(StringComparer.Ordinal);
        private bool explicitStorageTransfer;

        private static bool IsBuffer(string type) => type.StartsWith("storage-buffer<", StringComparison.Ordinal);

        private static string BufferElement(string type) => type["storage-buffer<".Length..^1];

        private string BindStorageType(string path, GenericTypeSyntax syntax)
        {
            string element = BindType(path, syntax.TypeArguments[0], syntax.Identifier);
            AdmitStorage(element, Span(path, syntax));
            return "storage-buffer<" + element + ">";
        }

        private static bool TryStorageTarget(VdMirExpression expression, out VdMirExpression storage, out string path)
        {
            path = "";
            storage = expression;
            var fields = new List<string>();
            while (storage.Kind == "field")
            {
                fields.Insert(0, storage.Value!);
                storage = storage.Operands![0];
            }
            path = string.Join(".", fields);
            return storage.Kind == "storage-load";
        }

        private static int VectorWidth(string type) => type switch
        {
            "float2" => 2,
            "float3" => 3,
            "float4" => 4,
            _ => 0,
        };

        private VdMirExpression? BindTransfer(string path, GenericCallExpressionSyntax call,
            Dictionary<string, ValueBinding> scope, bool allowStore)
        {
            string name = ((NameExpressionSyntax)call.Target).IdentifierToken.Text;
            if (name is not ("Load" or "Store" or "WordCount"))
            {
                return null;
            }
            explicitStorageTransfer = true;
            var source = Span(path, call);
            if (call.TypeArguments.Count != 1)
            {
                return Invalid("Storage operations require one concrete value type.");
            }
            string type = BindType(path, call.TypeArguments[0], new(SyntaxKind.IdentifierToken, source.Start, name, null));
            AdmitStorage(type, source);
            if (!storageLayouts.TryGetValue(type, out var layout))
            {
                return Invalid("Storage type has no admitted layout.");
            }
            if (name == "WordCount" && call.Arguments.Count == 0)
            {
                return new("literal", "u32", source, (layout.Stride / 4).ToString(CultureInfo.InvariantCulture));
            }
            int expected = name == "Store" ? 3 : 2;
            if (call.Arguments.Count != expected || name == "WordCount")
            {
                return Invalid($"{name} has an invalid argument count.");
            }
            var buffer = BindExpression(path, call.Arguments[0], scope);
            var offset = BindExpression(path, call.Arguments[1], scope);
            var resource = ResourceArgument(call.Arguments[0], scope);
            if (!IsBuffer(buffer.Type) || offset.Type != "u32" || resource is null)
            {
                return Invalid("Storage transfer requires a bound buffer and a u32 word offset.");
            }
            if (name == "Load")
            {
                return new("storage-load", type, source, "word", [buffer, offset]);
            }
            if (!allowStore || resource.Access != VdMirResourceAccess.Readwrite)
            {
                return Invalid("Store is statement-only and requires a readwrite resource.");
            }
            var value = BindExpression(path, call.Arguments[2], scope, type);
            if (value.Type != type)
            {
                return Invalid("Stored value must match the explicit transfer type.");
            }
            return new("storage-assignment-word", type, source, type, [buffer, offset, value]);

            VdMirExpression Invalid(string message)
            {
                AddDiagnostic("COPE-GPU-STORAGE-0002", "SDSL-V3703", "storage", message, source);
                return ErrorExpression(path, call);
            }
        }

        private void AdmitStorage(string type, VdMirSourceSpan source)
        {
            if (storageLayouts.ContainsKey(type))
            {
                return;
            }
            var fields = new List<VdMirStorageField>();
            Flatten(type, "");
            if (fields.Count > 0)
            {
                storageLayouts.Add(type, new(type, fields.Count * 4, fields));
            }
            else
            {
                AddDiagnostic("COPE-GPU-STORAGE-0001", "SDSL-V4114", "storage",
                    "Storage values must contain at least one transport word.", source);
            }

            void Flatten(string fieldType, string path)
            {
                if (fieldType is "f32" or "u32")
                {
                    fields.Add(new(path, fieldType, fields.Count * 4));
                    return;
                }
                int width = VectorWidth(fieldType);
                if (width > 0)
                {
                    foreach (char lane in "xyzw"[..width])
                    {
                        Flatten("f32", Join(path, lane.ToString()));
                    }
                    return;
                }
                if (_values.Find(fieldType) is { } aggregate)
                {
                    foreach (var field in aggregate.Fields)
                    {
                        Flatten(field.Type, Join(path, field.Name));
                    }
                    return;
                }
                AddDiagnostic("COPE-GPU-STORAGE-0001", "SDSL-V4114", "storage",
                    "Storage requires finite f32/u32/vector/record/array values; bool, enums and resources need explicit transport encoding.", source);
            }

            static string Join(string path, string field) => path.Length == 0 ? field : path + "." + field;
        }

        private VdMirExpression? BindNativeCall(string path, CallExpressionSyntax call, Dictionary<string, ValueBinding> scope)
        {
            if (call.Target is not NameExpressionSyntax name)
            {
                return null;
            }
            string operation = name.IdentifierToken.Text;
            int width = VectorWidth(operation);
            if (width == 0 && operation is not ("Dot" or "Cross" or "Length" or "Normalize"))
            {
                return null;
            }
            var operands = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            if (width > 0)
            {
                if (operands.Length != width || operands.Any(operand => operand.Type != "f32"))
                {
                    return NativeError($"{operation} requires {width} f32 lanes.");
                }
                return new("call", operation, Span(path, call), operation, operands);
            }
            if (operands.Length > 0 && _values.Find(operands[0].Type) is { Kind: "tensor" })
            {
                return null;
            }
            int arity = operation is "Dot" or "Cross" ? 2 : 1;
            bool valid = operands.Length == arity && VectorWidth(operands[0].Type) > 0
                && operands.All(operand => operand.Type == operands[0].Type)
                && (operation != "Cross" || operands[0].Type == "float3");
            if (!valid)
            {
                return NativeError($"{operation} requires {arity} matching native vector operand(s); Cross requires float3.");
            }
            return new("call", operation is "Dot" or "Length" ? "f32" : operands[0].Type,
                Span(path, call), operation, operands);

            VdMirExpression NativeError(string message)
            {
                AddDiagnostic("COPE-GPU-MATH-0001", "SDSL-V1503", "math", message, Span(path, call));
                return ErrorExpression(path, call);
            }
        }

        private VdMirResource? ResourceArgument(ExpressionSyntax expression, Dictionary<string, ValueBinding> scope)
        {
            return expression is NameExpressionSyntax name
                && scope.TryGetValue(name.IdentifierToken.Text, out var binding) ? binding.Resource : null;
        }
    }
}
