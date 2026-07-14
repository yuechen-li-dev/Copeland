using Copeland.TS.Backend.JavaScript;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptStructuredEmissionTests
{
    [Theory]
    [InlineData(1, "甲")]
    [InlineData(2, "乙")]
    [InlineData(9, "壬")]
    [InlineData(10, "癸")]
    [InlineData(11, "甲甲")]
    [InlineData(12, "甲乙")]
    [InlineData(19, "甲壬")]
    [InlineData(20, "乙癸")]
    [InlineData(21, "乙甲")]
    [InlineData(99, "壬壬")]
    [InlineData(100, "甲癸癸")]
    [InlineData(101, "甲癸甲")]
    public void Heavenly_stem_ordinals_are_bijective_base_ten(int value, string expected)
    {
        Assert.Equal(expected, SymbolicJavaScriptVocabulary.HeavenlyStemOrdinal(value));
    }

    [Fact]
    public void Symbolic_allocator_uses_closed_vocabulary_and_advances_on_collision()
    {
        var document = new JavaScriptEmissionDocument();
        var allocator = new JavaScriptNameAllocator(
            document,
            document.ProgramScope,
            ["$录型甲"],
            JavaScriptEmissionProfile.Symbolic);

        JavaScriptAllocatedBinding record = allocator.Allocate(
            JavaScriptBindingRole.TypeToken,
            "record_type_r1",
            symbolicRole: JavaScriptSymbolicBindingRole.RecordType);

        Assert.Equal("$录型乙", record.Name);
        document.Validate();
    }

    [Fact]
    public void Diagnostic_allocator_preserves_stable_ordinals_and_hostile_user_names()
    {
        var document = new JavaScriptEmissionDocument();
        var allocator = new JavaScriptNameAllocator(
            document,
            document.ProgramScope,
            ["main", "__cope_m3_record_type_r1_0", "__cope_m3_result_validate_2"]);

        JavaScriptAllocatedBinding record = allocator.Allocate(
            JavaScriptBindingRole.TypeToken,
            "record_type_r1",
            compilerOrigin: "r1");
        JavaScriptAllocatedBinding result = allocator.Allocate(
            JavaScriptBindingRole.Validator,
            "result_validate",
            compilerOrigin: "Result<number, string>");

        Assert.Equal("__cope_m3_record_type_r1_1", record.Name);
        Assert.Equal("__cope_m3_result_validate_3", result.Name);
        Assert.NotEqual(record.Id, result.Id);
        Assert.Equal("r1", document.Bindings[record.Id.Value].CompilerOrigin);
        document.Validate();
    }

    [Fact]
    public void Bindings_are_identity_based_and_scoped()
    {
        var document = new JavaScriptEmissionDocument();
        JavaScriptScopeId function = document.CreateScope(JavaScriptScopeKind.Function, document.ProgramScope);
        JavaScriptScopeId block = document.CreateScope(JavaScriptScopeKind.Block, function);
        JavaScriptBindingId outer = document.RegisterBinding(
            function,
            JavaScriptBindingRole.Temporary,
            "value",
            JavaScriptDeclarationKind.Const);
        JavaScriptBindingId inner = document.RegisterBinding(
            block,
            JavaScriptBindingRole.Temporary,
            "value",
            JavaScriptDeclarationKind.Const);
        document.Declare(outer);
        document.Declare(inner);
        document.AssignName(outer, "__cope_m3_value_0");
        document.AssignName(inner, "__cope_m3_value_1");

        document.Reference(block, outer);
        document.Reference(block, inner);
        Assert.Equal(1, document.Bindings[outer.Value].ReferenceCount);
        Assert.Equal(1, document.Bindings[inner.Value].ReferenceCount);
        Assert.Throws<InvalidOperationException>(() => document.Reference(function, inner));
        document.Validate();
    }

    [Fact]
    public void Invalid_binding_documents_fail_before_printing()
    {
        var document = new JavaScriptEmissionDocument();
        JavaScriptBindingId binding = document.RegisterBinding(
            document.ProgramScope,
            JavaScriptBindingRole.Temporary,
            "reserved",
            JavaScriptDeclarationKind.Const);

        Assert.Throws<InvalidOperationException>(() => document.Validate());
        Assert.Throws<InvalidOperationException>(() => document.AssignName(binding, "class"));
        Assert.Throws<InvalidOperationException>(() => document.Reference(document.ProgramScope, new JavaScriptBindingId(99)));
    }

    [Fact]
    public void Token_writer_separates_words_numbers_and_unary_adjacency()
    {
        var writer = new JavaScriptTokenWriter();
        writer.Keyword("return");
        writer.ExternalIdentifier("value");
        writer.Punctuator("+");
        writer.Punctuator("+");
        writer.ExternalIdentifier("next");
        writer.Punctuator(";");
        writer.LineBreak();
        writer.Number(1);
        writer.Punctuator(".");
        writer.ExternalIdentifier("toString");
        writer.Punctuator("(");
        writer.Punctuator(")");
        writer.Punctuator(";");

        Assert.Equal("return value+ +next;\n1 .toString();\n", writer.Complete());
    }

    [Fact]
    public void Token_writer_preserves_literal_escaping_and_exactly_one_final_lf()
    {
        var writer = new JavaScriptTokenWriter();
        writer.Keyword("const");
        writer.ExternalIdentifier("value");
        writer.Punctuator("=");
        writer.String("\"\\\n\r\t\u0001/\ud800雪");
        writer.Punctuator(";");

        Assert.Equal("const value=\"\\\"\\\\\\n\\r\\t\\u0001/\\ud800雪\";\n", writer.Complete());
        Assert.Throws<InvalidOperationException>(() => writer.Keyword("after"));
    }

    [Fact]
    public void Token_writer_rejects_unbalanced_indentation_and_unknown_punctuators()
    {
        var writer = new JavaScriptTokenWriter();
        Assert.Throws<InvalidOperationException>(() => writer.Unindent());
        Assert.Throws<InvalidOperationException>(() => writer.Punctuator("/*"));
    }

    [Fact]
    public void Diagnostic_line_writer_records_typed_binding_references_before_rendering()
    {
        var document = new JavaScriptEmissionDocument();
        JavaScriptBindingId binding = document.RegisterBinding(
            document.ProgramScope,
            JavaScriptBindingRole.RuntimeHelper,
            "helper",
            JavaScriptDeclarationKind.Const);
        document.Declare(binding);
        document.AssignName(binding, "__cope_m3_helper_0");
        var reference = new JavaScriptBindingReference(binding, document.GetAssignedName(binding));
        var writer = new JavaScriptTextWriter(document);

        writer.WriteLine($"const {reference} = 1;");

        Assert.Equal(0, document.Bindings[binding.Value].ReferenceCount);
        Assert.Equal("const __cope_m3_helper_0 = 1;\n", writer.ToString());
        Assert.Equal(1, document.Bindings[binding.Value].ReferenceCount);
    }

    [Fact]
    public void Diagnostic_line_writer_enforces_function_scope_for_typed_binding_references()
    {
        var document = new JavaScriptEmissionDocument();
        JavaScriptScopeId function = document.CreateScope(JavaScriptScopeKind.Function, document.ProgramScope);
        JavaScriptBindingId binding = document.RegisterBinding(
            function,
            JavaScriptBindingRole.Temporary,
            "temporary",
            JavaScriptDeclarationKind.Const);
        document.Declare(binding);
        document.AssignName(binding, "__cope_m3_temporary_0");
        var reference = new JavaScriptBindingReference(binding, document.GetAssignedName(binding));
        var invalidWriter = new JavaScriptTextWriter(document);

        invalidWriter.WriteLine($"const value = {reference};");

        Assert.Throws<InvalidOperationException>(() => invalidWriter.ToString());

        var validWriter = new JavaScriptTextWriter(document);
        validWriter.EnterScope(function);
        validWriter.WriteLine($"const value = {reference};");

        Assert.Equal("const value = __cope_m3_temporary_0;\n", validWriter.ToString());
    }
}
