using Oblivion.Model;
using Oblivion.Persistence;
using Xunit;

namespace Oblivion.Persistence.Tests;

public sealed class FunctionCardPersistenceTests
{
    [Fact]
    public void Function_card_round_trips_the_small_durable_source_contract()
    {
        const string source = """
            format = 1
            kind = "card"
            id = "check-reload"
            card_kind = "function"
            status = "idle"
            title = "Check reload"
            tags = ["fact"]

            [function]
            kind = "copeland-xunit"
            reference = "source/Reload.tsxtest"
            test = "invalid_reload_preserves_session"

            [body]
            format = "plain"
            text = ""
            """;

        OblivionCardTomlReadResult read = OblivionCardTomlReader.Read(source);
        OblivionCardAssetDocument document = Assert.IsType<OblivionCardAssetDocument>(read.Document);
        string written = OblivionCardTomlWriter.Write(document);
        OblivionCardTomlReadResult reread = OblivionCardTomlReader.Read(written);

        Assert.Empty(read.Diagnostics);
        Assert.Equal("function", document.CardKind);
        Assert.Equal("copeland-xunit", document.Function!.Kind);
        Assert.Equal("source/Reload.tsxtest", document.Function.Reference);
        Assert.Equal("invalid_reload_preserves_session", document.Function.Test);
        Assert.NotNull(reread.Document);
        Assert.Equal(document.Id, reread.Document.Id);
        Assert.Equal(document.CardKind, reread.Document.CardKind);
        Assert.Equal(document.Function, reread.Document.Function);
    }

    [Theory]
    [InlineData("source/Reload.ts", "unsupported-function-source-reference")]
    [InlineData("../Reload.tsxtest", "unsafe-function-source-reference")]
    public void Function_source_rejects_non_tsxtest_and_workspace_escape(
        string reference,
        string expectedCode)
    {
        string source = $$"""
            format = 1
            kind = "card"
            id = "function"
            card_kind = "function"
            status = "idle"
            title = "Function"
            tags = []

            [function]
            kind = "copeland-xunit"
            reference = "{{reference}}"
            test = "test"

            [body]
            format = "plain"
            text = ""
            """;

        OblivionCardTomlReadResult read = OblivionCardTomlReader.Read(source);

        Assert.Null(read.Document);
        Assert.Contains(read.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }
}
