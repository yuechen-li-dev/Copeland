namespace Copeland.Markdown;

public static class MarkdownDiagnosticIds
{
    public const string UnsupportedBlockSyntax = "COPE-MD-PARSE-0001";
    public const string MalformedHeadingMarker = "COPE-MD-PARSE-0002";
    public const string UnclosedCodeFence = "COPE-MD-PARSE-0003";
    public const string MalformedLink = "COPE-MD-PARSE-0004";
    public const string UnmatchedEmphasisMarker = "COPE-MD-PARSE-0005";
    public const string UnmatchedStrongMarker = "COPE-MD-PARSE-0006";
    public const string UnmatchedInlineCodeMarker = "COPE-MD-PARSE-0007";
    public const string UnsupportedInlineSyntax = "COPE-MD-PARSE-0008";
    public const string NestedListNotSupported = "COPE-MD-PARSE-0009";
}
