using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Lexing;
using Copeland.Markdown;
using Copeland.Script.Syntax;
using Xunit;

namespace Aurelian.Shaders.Tests;

/// <summary>
/// Records the M6b source-contract observations without introducing a production shared-source dependency.
/// Each adapter reaches an existing lane through its public/current implementation only.
/// </summary>
public sealed class CompilerSourceContractSpikeTests
{
    public static IEnumerable<object[]> SourceCases()
    {
        yield return ["empty", string.Empty];
        yield return ["ordinary line", "alpha"];
        yield return ["LF", "a\nb"];
        yield return ["CRLF", "a\r\nb"];
        yield return ["bare CR", "a\rb"];
        yield return ["mixed newlines", "a\r\nb\rc\nd"];
        yield return ["leading newline", "\nalpha"];
        yield return ["consecutive empty lines", "a\n\nb"];
        yield return ["trailing newline", "a\n"];
        yield return ["empty final line", "a\r\n"];
        yield return ["ASCII", "alpha 123"];
        yield return ["tabs", "a\tb"];
        yield return ["BMP Unicode", "a\u03A9b"];
        yield return ["surrogate pair", "a\U0001F600b"];
    }

    [Theory]
    [MemberData(nameof(SourceCases))]
    public void AllLanes_ExposeUtf16OffsetsAtEveryValidOffsetIncludingEof(string _, string source)
    {
        var markdown = new MarkdownSourceProbe(source);
        var script = new ScriptSourceProbe(source);
        var sdslv = new SdslvSourceProbe(source);

        Assert.Equal(source.Length, markdown.Length);
        Assert.Equal(source.Length, script.Length);
        Assert.Equal(source.Length, sdslv.Length);

        for (int offset = 0; offset <= source.Length; offset += 1)
        {
            Assert.Equal(offset, markdown.LocationAt(offset).Index);
            Assert.Equal(offset, script.PositionAt(offset));
            Assert.Equal(offset, sdslv.LocationAt(offset).Start);
        }
    }

    [Fact]
    public void MarkdownAndSdslv_UseOneBasedUtf16ColumnsForTabsAndSurrogatePairs()
    {
        const string source = "a\t\u03A9\U0001F600b";
        var markdown = new MarkdownSourceProbe(source);
        var sdslv = new SdslvSourceProbe(source);

        // The emoji occupies two UTF-16 offsets. Neither lane reports Unicode scalar or grapheme columns.
        Assert.Equal(new SourceLocation(5, 1, 6), markdown.LocationAt(5));
        Assert.Equal(new SdslvSpan(5, 6, 1, 6), sdslv.LocationAt(5));
    }

    [Fact]
    public void MarkdownAndSdslv_AgreeOnLfAndCrLfIncludingOffsetsOnNewlineCharacters()
    {
        AssertLocations("a\nb", newlineStart: 1, expectedLineTwoStart: 2);
        AssertLocations("a\r\nb", newlineStart: 1, expectedLineTwoStart: 3);
    }

    [Fact]
    public void MarkdownAndSdslv_DisagreeOnBareCrRecognition()
    {
        const string source = "a\rb";
        var markdown = new MarkdownSourceProbe(source);
        var sdslv = new SdslvSourceProbe(source);

        Assert.Equal(new SourceLocation(2, 2, 1), markdown.LocationAt(2));
        Assert.Equal(new SdslvSpan(2, 3, 1, 3), sdslv.LocationAt(2));
    }

    [Fact]
    public void Markdown_RejectsInvalidOffsetsAndSourceBoundSpans()
    {
        var source = new MarkdownSourceText("abc");

        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLocation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLocation(4));
        Assert.Equal(new SourceLocation(3, 1, 4), source.GetLocation(3));

        Assert.Equal(new SourceSpan(0, 0, new SourceLocation(0, 1, 1), new SourceLocation(0, 1, 1)), source.CreateSpan(0, 0));
        Assert.Equal(3, source.CreateSpan(0, 3).End);
        Assert.Equal(3, source.CreateSpan(3, 0).End);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateSpan(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateSpan(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateSpan(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateSpan(2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateSpan(3, int.MaxValue));
    }

    [Fact]
    public void ScriptAndSdslv_DoNotExposeValidatedSourceBoundSpanConstruction()
    {
        var scriptDiagnostic = new Copeland.Script.Diagnostics.Diagnostic("COPE", "test", -1, int.MaxValue);
        var sdslvSpan = new SdslvSpan(-1, int.MaxValue, -2, -3);

        Assert.Equal(-1, scriptDiagnostic.Position);
        Assert.Equal(int.MaxValue, scriptDiagnostic.Length);
        Assert.Equal(-1, sdslvSpan.Start);
        Assert.Equal(int.MaxValue, sdslvSpan.End);
    }

    [Fact]
    public void Script_DoesNotProvideLineColumnOrSpanValidationAsPartOfItsCurrentSourceContract()
    {
        SyntaxTree tree = SyntaxTree.ParseTokens("a\rb");

        Assert.Equal("a\rb", tree.Text);
        Assert.Equal(0, tree.Tokens[0].Position);
        Assert.Equal(2, tree.Tokens[1].Position);
        Assert.Equal(3, tree.Tokens[^1].Position);
    }

    private static void AssertLocations(string source, int newlineStart, int expectedLineTwoStart)
    {
        var markdown = new MarkdownSourceProbe(source);
        var sdslv = new SdslvSourceProbe(source);

        Assert.Equal(new SourceLocation(newlineStart, 1, newlineStart + 1), markdown.LocationAt(newlineStart));
        Assert.Equal(new SdslvSpan(newlineStart, newlineStart + 1, 1, newlineStart + 1), sdslv.LocationAt(newlineStart));

        if (source[newlineStart] == '\r')
        {
            Assert.Equal(new SourceLocation(newlineStart + 1, 1, newlineStart + 2), markdown.LocationAt(newlineStart + 1));
            Assert.Equal(new SdslvSpan(newlineStart + 1, newlineStart + 2, 1, newlineStart + 2), sdslv.LocationAt(newlineStart + 1));
        }

        Assert.Equal(new SourceLocation(expectedLineTwoStart, 2, 1), markdown.LocationAt(expectedLineTwoStart));
        Assert.Equal(new SdslvSpan(expectedLineTwoStart, expectedLineTwoStart + 1, 2, 1), sdslv.LocationAt(expectedLineTwoStart));
    }

    private sealed class MarkdownSourceProbe(string source)
    {
        private readonly MarkdownSourceText sourceText = new(source);

        public int Length => sourceText.Text.Length;

        public SourceLocation LocationAt(int offset)
        {
            return sourceText.GetLocation(offset);
        }
    }

    private sealed class ScriptSourceProbe(string source)
    {
        public int Length => source.Length;

        public int PositionAt(int offset)
        {
            SyntaxTree tree = SyntaxTree.ParseTokens(source.Insert(offset, "@"));
            return Assert.Single(tree.Tokens, token => token.Kind == SyntaxKind.BadToken && token.Text == "@").Position;
        }
    }

    private sealed class SdslvSourceProbe(string source)
    {
        public int Length => source.Length;

        public SdslvSpan LocationAt(int offset)
        {
            var result = SdslvLexer.Lex(source.Insert(offset, "@"));
            return Assert.Single(result.Tokens, token => token.Kind == Aurelian.Shaders.Language.Tokens.SdslvTokenKind.Unknown && token.Text == "@").Span;
        }
    }
}
