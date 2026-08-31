using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionDiagramRenderRequest(
    string ContentId,
    string Source,
    string? SourceReference,
    string OutputDirectory,
    OblivionResolvedAppearance Appearance,
    string? WorkspaceId = null,
    string? PageId = null,
    string? CardId = null);

public enum OblivionResolvedAppearance
{
    Light,
    Dark,
}

public sealed record OblivionMermaidRenderOptions(
    string OutputFormat,
    OblivionResolvedAppearance Appearance)
{
    public static OblivionMermaidRenderOptions For(OblivionResolvedAppearance appearance)
    {
        return new OblivionMermaidRenderOptions(
            OblivionMermaidRendererOptions.OutputFormat,
            appearance);
    }
}

internal sealed record OblivionMermaidQualifiedTheme(
    string Theme,
    string BackgroundColor,
    string ConfigurationIdentity)
{
    public string RenderingIdentity => string.Join(
        ";",
        $"theme={Theme}",
        $"background={BackgroundColor}",
        "securityLevel=strict",
        $"configuration={ConfigurationIdentity}");

    public static OblivionMermaidQualifiedTheme For(OblivionResolvedAppearance appearance)
    {
        return appearance switch
        {
            OblivionResolvedAppearance.Light => new OblivionMermaidQualifiedTheme(
                "default",
                "#ffffff",
                "m19p-light-v1"),
            OblivionResolvedAppearance.Dark => new OblivionMermaidQualifiedTheme(
                "dark",
                "#0f172a",
                "m19p-dark-v1"),
            _ => throw new ArgumentOutOfRangeException(nameof(appearance)),
        };
    }
}

public sealed record MermaidDerivedArtifactKey(
    string SourceHash,
    string RendererId,
    string RendererVersion,
    string OutputFormat,
    string RenderingOptions)
{
    public string Value => OblivionMermaidHashing.HashUtf8(
        string.Join("\n", SourceHash, RendererId, RendererVersion, OutputFormat, RenderingOptions));
}

public static class OblivionMermaidArtifactIdentity
{
    public static MermaidDerivedArtifactKey CreateKey(
        string sourceHash,
        OblivionResolvedAppearance appearance)
    {
        return new MermaidDerivedArtifactKey(
            sourceHash,
            OblivionMermaidRendererOptions.RendererId,
            OblivionMermaidRendererOptions.PinnedVersion,
            OblivionMermaidRendererOptions.OutputFormat,
            OblivionMermaidRendererOptions.RenderingOptionsFor(appearance));
    }

    public static string ArtifactPath(
        string outputDirectory,
        string sourceHash,
        OblivionResolvedAppearance appearance)
    {
        MermaidDerivedArtifactKey key = CreateKey(sourceHash, appearance);
        return Path.Combine(Path.GetFullPath(outputDirectory), key.Value + ".png");
    }
}

public sealed record OblivionDiagramProvenance(
    string SourceKind,
    string SourceHash,
    string RendererId,
    string RendererVersion,
    string RenderOperation,
    string OutputFormat,
    OblivionResolvedAppearance ResolvedAppearance,
    string RenderOptionsIdentity,
    string Producer,
    string? WorkspaceId,
    string? PageId,
    string? CardId,
    string ContentId,
    string? SourceReference,
    bool Derived);

public sealed record OblivionDiagramRenderResult(
    bool Succeeded,
    string Renderer,
    string RendererVersion,
    string SourceHash,
    string? RenderedPath,
    string? MediaType,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics,
    string? CacheKey = null,
    bool CacheHit = false,
    OblivionDiagramProvenance? Provenance = null);

public interface IOblivionDiagramRenderer
{
    OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request);
}

public static class OblivionMermaidHashing
{
    public static string CanonicalizeSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string ComputeSourceHash(string source)
    {
        return HashUtf8(CanonicalizeSource(source));
    }

    internal static string HashUtf8(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }
}

public sealed record OblivionMermaidRendererOptions(
    string? ExecutablePath,
    string? CliPath,
    string ExpectedVersion,
    TimeSpan Timeout,
    string DiscoverySource)
{
    public const string RendererId = "mermaid-cli";
    public const string PinnedVersion = "11.16.0";
    public const string OutputFormat = "png";

    public static string RenderingOptionsFor(OblivionResolvedAppearance appearance)
    {
        return OblivionMermaidQualifiedTheme.For(appearance).RenderingIdentity;
    }

    public static OblivionMermaidRendererOptions Unavailable(string discoverySource)
    {
        return new OblivionMermaidRendererOptions(
            null,
            null,
            PinnedVersion,
            TimeSpan.FromSeconds(30),
            discoverySource);
    }
}

public static class OblivionMermaidRendererDiscovery
{
    public static OblivionMermaidRendererOptions Discover(string? repositoryRoot = null)
    {
        string? configuredCli = Environment.GetEnvironmentVariable("OBLIVION_MERMAID_CLI");
        if (!string.IsNullOrWhiteSpace(configuredCli))
        {
            return CreateOptions(Path.GetFullPath(configuredCli), "OBLIVION_MERMAID_CLI");
        }

        string? root = ResolveRepositoryRoot(repositoryRoot);
        if (root is not null)
        {
            string localCli = Path.Combine(
                root,
                "tools",
                "mermaid",
                "node_modules",
                "@mermaid-js",
                "mermaid-cli",
                "src",
                "cli.js");
            if (File.Exists(localCli))
            {
                return CreateOptions(localCli, "repo-owned tools/mermaid");
            }
        }

        return OblivionMermaidRendererOptions.Unavailable(
            "OBLIVION_MERMAID_CLI was not set and the repo-owned tools/mermaid install was absent");
    }

    private static OblivionMermaidRendererOptions CreateOptions(string cliPath, string source)
    {
        if (string.Equals(Path.GetExtension(cliPath), ".js", StringComparison.OrdinalIgnoreCase))
        {
            return new OblivionMermaidRendererOptions(
                ResolveNodeExecutable(),
                cliPath,
                OblivionMermaidRendererOptions.PinnedVersion,
                TimeSpan.FromSeconds(30),
                source);
        }

        return new OblivionMermaidRendererOptions(
            cliPath,
            null,
            OblivionMermaidRendererOptions.PinnedVersion,
            TimeSpan.FromSeconds(30),
            source);
    }

    private static string? ResolveNodeExecutable()
    {
        string? configuredNode = Environment.GetEnvironmentVariable("OBLIVION_NODE_EXE");
        if (!string.IsNullOrWhiteSpace(configuredNode))
        {
            return Path.GetFullPath(configuredNode);
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string knownNodePath = Path.Combine(programFiles, "nodejs", "node.exe");
        return File.Exists(knownNodePath) ? knownNodePath : null;
    }

    private static string? ResolveRepositoryRoot(string? repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return Path.GetFullPath(repositoryRoot);
        }

        DirectoryInfo? current = new(Path.GetFullPath(Environment.CurrentDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Copeland.slnx")) &&
                Directory.Exists(Path.Combine(current.FullName, "tools", "mermaid")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}

public sealed record OblivionProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout);

public sealed record OblivionProcessResult(
    bool Started,
    bool TimedOut,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string? StartError = null);

public interface IOblivionProcessRunner
{
    OblivionProcessResult Run(OblivionProcessRequest request);
}

public sealed class OblivionBoundedProcessRunner : IOblivionProcessRunner
{
    private const int MaximumCapturedCharacters = 8_192;

    public OblivionProcessResult Run(OblivionProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            ProcessStartInfo startInfo = new(request.ExecutablePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = request.WorkingDirectory,
            };
            foreach (string argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The renderer process did not start.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)request.Timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return new OblivionProcessResult(
                    true,
                    true,
                    null,
                    Bound(standardOutput.GetAwaiter().GetResult()),
                    Bound(standardError.GetAwaiter().GetResult()));
            }

            return new OblivionProcessResult(
                true,
                false,
                process.ExitCode,
                Bound(standardOutput.GetAwaiter().GetResult()),
                Bound(standardError.GetAwaiter().GetResult()));
        }
        catch (Exception exception)
        {
            return new OblivionProcessResult(false, false, null, string.Empty, string.Empty, exception.Message);
        }
    }

    private static string Bound(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= MaximumCapturedCharacters
            ? trimmed
            : trimmed[..MaximumCapturedCharacters] + "...";
    }
}

public sealed class OblivionExternalMermaidRenderer : IOblivionDiagramRenderer
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = CreateMetadataJsonOptions();
    private readonly OblivionMermaidRendererOptions _options;
    private readonly IOblivionProcessRunner _processRunner;
    private string? _qualifiedVersion;

    public OblivionExternalMermaidRenderer(string? executablePath)
        : this(string.IsNullOrWhiteSpace(executablePath)
            ? OblivionMermaidRendererOptions.Unavailable("explicit path was not configured")
            : new OblivionMermaidRendererOptions(
                executablePath,
                null,
                OblivionMermaidRendererOptions.PinnedVersion,
                TimeSpan.FromSeconds(30),
                "explicit constructor path"))
    {
    }

    public OblivionExternalMermaidRenderer(
        OblivionMermaidRendererOptions options,
        IOblivionProcessRunner? processRunner = null)
    {
        _options = options;
        _processRunner = processRunner ?? new OblivionBoundedProcessRunner();
    }

    public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string canonicalSource = OblivionMermaidHashing.CanonicalizeSource(request.Source);
        string sourceHash = OblivionMermaidHashing.ComputeSourceHash(canonicalSource);
        OblivionMermaidRenderOptions renderOptions = OblivionMermaidRenderOptions.For(
            request.Appearance);
        OblivionMermaidQualifiedTheme qualifiedTheme = OblivionMermaidQualifiedTheme.For(
            request.Appearance);
        OblivionDiagramProvenance provenance = CreateProvenance(request, sourceHash);
        OblivionDiagramRenderResult? qualificationFailure = QualifyRenderer(
            request,
            sourceHash,
            provenance);
        if (qualificationFailure is not null)
        {
            return qualificationFailure;
        }

        provenance = provenance with { RendererVersion = _qualifiedVersion! };
        MermaidDerivedArtifactKey key = new(
            sourceHash,
            OblivionMermaidRendererOptions.RendererId,
            _qualifiedVersion!,
            renderOptions.OutputFormat,
            qualifiedTheme.RenderingIdentity);
        string cacheKey = key.Value;
        Directory.CreateDirectory(request.OutputDirectory);
        string outputPath = Path.Combine(request.OutputDirectory, cacheKey + ".png");
        string metadataPath = Path.Combine(request.OutputDirectory, cacheKey + ".json");
        CacheValidation cache = ValidateCache(metadataPath, outputPath, key, provenance);
        if (cache.Valid)
        {
            return Success(sourceHash, outputPath, cacheKey, true, provenance, []);
        }

        List<OblivionCardDiagnostic> diagnostics = [];
        if (cache.InvalidReason is not null)
        {
            diagnostics.Add(Diagnostic(
                request,
                "OBLIVION-MERMAID-CACHE-INVALID",
                $"Mermaid cache entry was ignored: {cache.InvalidReason}"));
        }

        string temporaryDirectory = Path.Combine(
            request.OutputDirectory,
            ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string inputPath = Path.Combine(temporaryDirectory, "source.mmd");
            string temporaryOutputPath = Path.Combine(temporaryDirectory, "diagram.png");
            string mermaidConfigPath = Path.Combine(temporaryDirectory, "mermaid-config.json");
            File.WriteAllText(
                inputPath,
                canonicalSource,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(
                mermaidConfigPath,
                "{\"securityLevel\":\"strict\"}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            OblivionProcessResult process = _processRunner.Run(new OblivionProcessRequest(
                _options.ExecutablePath!,
                BuildRenderArguments(
                    inputPath,
                    temporaryOutputPath,
                    mermaidConfigPath,
                    renderOptions,
                    qualifiedTheme),
                temporaryDirectory,
                _options.Timeout));
            if (process.TimedOut)
            {
                return Failure(
                    request,
                    sourceHash,
                    provenance,
                    "OBLIVION-MERMAID-RENDER-TIMEOUT",
                    $"Mermaid rendering exceeded the {_options.Timeout.TotalSeconds:0} second bound.",
                    cacheKey);
            }

            if (!process.Started || process.ExitCode != 0)
            {
                string detail = process.StartError ?? process.StandardError;
                if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = $"renderer exited with code {process.ExitCode?.ToString() ?? "unknown"}";
                }

                return Failure(
                    request,
                    sourceHash,
                    provenance,
                    "OBLIVION-MERMAID-RENDER-FAILED",
                    $"Mermaid rendering failed: {detail}",
                    cacheKey);
            }

            if (!File.Exists(temporaryOutputPath))
            {
                return Failure(
                    request,
                    sourceHash,
                    provenance,
                    "OBLIVION-MERMAID-OUTPUT-MISSING",
                    "Mermaid reported success but produced no PNG output.",
                    cacheKey);
            }

            if (!IsReadablePng(temporaryOutputPath))
            {
                return Failure(
                    request,
                    sourceHash,
                    provenance,
                    "OBLIVION-MERMAID-OUTPUT-INVALID",
                    "Mermaid produced an unreadable or malformed PNG output.",
                    cacheKey);
            }

            File.Move(temporaryOutputPath, outputPath, overwrite: true);
            WriteMetadataAtomically(metadataPath, key, provenance);
            return Success(sourceHash, outputPath, cacheKey, false, provenance, diagnostics);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private IReadOnlyList<string> BuildRenderArguments(
        string inputPath,
        string outputPath,
        string mermaidConfigPath,
        OblivionMermaidRenderOptions renderOptions,
        OblivionMermaidQualifiedTheme qualifiedTheme)
    {
        List<string> arguments = BuildCliArguments();
        arguments.Add("--input");
        arguments.Add(inputPath);
        arguments.Add("--output");
        arguments.Add(outputPath);
        arguments.Add("--outputFormat");
        arguments.Add(renderOptions.OutputFormat);
        arguments.Add("--theme");
        arguments.Add(qualifiedTheme.Theme);
        arguments.Add("--backgroundColor");
        arguments.Add(qualifiedTheme.BackgroundColor);
        arguments.Add("--configFile");
        arguments.Add(mermaidConfigPath);
        arguments.Add("--quiet");
        return arguments;
    }

    private List<string> BuildCliArguments()
    {
        List<string> arguments = [];
        if (_options.CliPath is not null)
        {
            arguments.Add(_options.CliPath);
        }

        return arguments;
    }

    private OblivionDiagramRenderResult? QualifyRenderer(
        OblivionDiagramRenderRequest request,
        string sourceHash,
        OblivionDiagramProvenance provenance)
    {
        if (string.IsNullOrWhiteSpace(_options.ExecutablePath) || !File.Exists(_options.ExecutablePath) ||
            (_options.CliPath is not null && !File.Exists(_options.CliPath)))
        {
            return Failure(
                request,
                sourceHash,
                provenance,
                "OBLIVION-MERMAID-RENDERER-UNAVAILABLE",
                $"Mermaid source was retained, but the pinned renderer is unavailable ({_options.DiscoverySource}).");
        }

        if (_qualifiedVersion is not null)
        {
            return null;
        }

        List<string> arguments = BuildCliArguments();
        arguments.Add("--version");
        OblivionProcessResult result = _processRunner.Run(new OblivionProcessRequest(
            _options.ExecutablePath,
            arguments,
            Path.GetDirectoryName(_options.CliPath ?? _options.ExecutablePath)!,
            TimeSpan.FromSeconds(10)));
        string actualVersion = result.StandardOutput.Trim();
        if (!result.Started || result.TimedOut || result.ExitCode != 0 ||
            !string.Equals(actualVersion, _options.ExpectedVersion, StringComparison.Ordinal))
        {
            string observed = string.IsNullOrWhiteSpace(actualVersion)
                ? result.StartError ?? result.StandardError ?? "no version reported"
                : actualVersion;
            return Failure(
                request,
                sourceHash,
                provenance,
                "OBLIVION-MERMAID-RENDERER-VERSION-MISMATCH",
                $"Expected Mermaid CLI {_options.ExpectedVersion}, but observed '{observed}'.");
        }

        _qualifiedVersion = actualVersion;
        return null;
    }

    private OblivionDiagramProvenance CreateProvenance(
        OblivionDiagramRenderRequest request,
        string sourceHash)
    {
        return new OblivionDiagramProvenance(
            "Mermaid",
            sourceHash,
            OblivionMermaidRendererOptions.RendererId,
            _qualifiedVersion ?? _options.ExpectedVersion,
            "render-mermaid-png",
            OblivionMermaidRendererOptions.OutputFormat,
            request.Appearance,
            OblivionMermaidRendererOptions.RenderingOptionsFor(request.Appearance),
            $"@mermaid-js/mermaid-cli@{_options.ExpectedVersion}",
            request.WorkspaceId,
            request.PageId,
            request.CardId,
            request.ContentId,
            request.SourceReference,
            Derived: true);
    }

    private static CacheValidation ValidateCache(
        string metadataPath,
        string outputPath,
        MermaidDerivedArtifactKey expectedKey,
        OblivionDiagramProvenance expectedProvenance)
    {
        bool metadataExists = File.Exists(metadataPath);
        bool outputExists = File.Exists(outputPath);
        if (!metadataExists && !outputExists)
        {
            return new CacheValidation(false, null);
        }

        if (!metadataExists || !outputExists)
        {
            return new CacheValidation(false, "metadata or artifact was missing");
        }

        try
        {
            MermaidCacheMetadata? metadata = JsonSerializer.Deserialize<MermaidCacheMetadata>(
                File.ReadAllText(metadataPath),
                MetadataJsonOptions);
            if (metadata is null || metadata.Format != 2 ||
                metadata.Key != expectedKey || metadata.Provenance != expectedProvenance)
            {
                return new CacheValidation(
                    false,
                    "metadata did not match the requested source, renderer, options, or owner");
            }

            return IsReadablePng(outputPath)
                ? new CacheValidation(true, null)
                : new CacheValidation(false, "cached PNG was corrupt");
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new CacheValidation(false, $"metadata was unreadable ({exception.Message})");
        }
    }

    private static void WriteMetadataAtomically(
        string metadataPath,
        MermaidDerivedArtifactKey key,
        OblivionDiagramProvenance provenance)
    {
        string temporaryPath = metadataPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string json = JsonSerializer.Serialize(
            new MermaidCacheMetadata(2, key, provenance),
            MetadataJsonOptions);
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, metadataPath, overwrite: true);
    }

    private static bool IsReadablePng(string path)
    {
        ReadOnlySpan<byte> expected = [137, 80, 78, 71, 13, 10, 26, 10];
        try
        {
            byte[] png = File.ReadAllBytes(path);
            if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(expected))
            {
                return false;
            }

            bool foundHeader = false;
            bool foundImageData = false;
            int offset = 8;
            while (offset + 12 <= png.Length)
            {
                uint unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
                if (unsignedLength > int.MaxValue)
                {
                    return false;
                }

                int length = (int)unsignedLength;
                int nextOffset = checked(offset + 12 + length);
                if (nextOffset > png.Length)
                {
                    return false;
                }

                ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
                if (type.SequenceEqual("IHDR"u8))
                {
                    foundHeader = offset == 8 && length == 13;
                }
                else if (type.SequenceEqual("IDAT"u8))
                {
                    foundImageData |= length > 0;
                }
                else if (type.SequenceEqual("IEND"u8))
                {
                    return foundHeader && foundImageData && length == 0 && nextOffset == png.Length;
                }

                offset = nextOffset;
            }

            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return false;
        }
    }

    private static OblivionDiagramRenderResult Success(
        string sourceHash,
        string outputPath,
        string cacheKey,
        bool cacheHit,
        OblivionDiagramProvenance provenance,
        IReadOnlyList<OblivionCardDiagnostic> diagnostics)
    {
        return new OblivionDiagramRenderResult(
            true,
            OblivionMermaidRendererOptions.RendererId,
            provenance.RendererVersion,
            sourceHash,
            outputPath,
            "image/png",
            diagnostics,
            cacheKey,
            cacheHit,
            provenance);
    }

    private OblivionDiagramRenderResult Failure(
        OblivionDiagramRenderRequest request,
        string sourceHash,
        OblivionDiagramProvenance provenance,
        string code,
        string message,
        string? cacheKey = null)
    {
        return new OblivionDiagramRenderResult(
            false,
            OblivionMermaidRendererOptions.RendererId,
            _qualifiedVersion ?? _options.ExpectedVersion,
            sourceHash,
            null,
            null,
            [Diagnostic(request, code, message)],
            cacheKey,
            false,
            provenance);
    }

    private static OblivionCardDiagnostic Diagnostic(
        OblivionDiagramRenderRequest request,
        string code,
        string message)
    {
        string context = string.Join(
            ", ",
            new[]
            {
                request.WorkspaceId is null ? null : $"workspaceId={request.WorkspaceId}",
                request.PageId is null ? null : $"pageId={request.PageId}",
                request.CardId is null ? null : $"cardId={request.CardId}",
                $"contentId={request.ContentId}",
            }.Where(value => value is not null));
        return new OblivionCardDiagnostic(
            code,
            OblivionDiagnosticSeverity.Warning,
            $"{message} ({context})",
            request.SourceReference);
    }

    private sealed record CacheValidation(bool Valid, string? InvalidReason);

    private static JsonSerializerOptions CreateMetadataJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record MermaidCacheMetadata(
        int Format,
        MermaidDerivedArtifactKey Key,
        OblivionDiagramProvenance Provenance);
}

public static class OblivionContentRealization
{
    public static IReadOnlyList<OblivionResolvedContentArtifact> ResolveArtifacts(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionWorkspacePage page,
        OblivionCard card,
        OblivionArtifactResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(card);

        OblivionArtifactResolver effectiveResolver = resolver ?? new OblivionArtifactResolver();
        return card.Artifacts
            .Select(declaration => effectiveResolver.Resolve(workspace, location, page, card, declaration).Artifact)
            .Where(artifact => artifact is not null)
            .Select(artifact => new OblivionResolvedContentArtifact(
                artifact!.Address.ArtifactId.Value,
                artifact.Label,
                artifact.Kind,
                artifact.DeclaredReference,
                artifact.ResolvedPath,
                artifact.Exists,
                artifact.MediaType,
                artifact.Generated,
                artifact.DeclarationSourceReference ?? artifact.Provenance.SourceReference))
            .ToArray();
    }

    public static IReadOnlyList<OblivionDiagramRenderResult> RenderMermaid(
        OblivionContentPresentationPlan plan,
        IOblivionDiagramRenderer renderer,
        string outputDirectory,
        OblivionResolvedAppearance appearance = OblivionResolvedAppearance.Light,
        string? workspaceId = null,
        string? pageId = null,
        string? cardId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        return plan.Items
            .Where(item => item.PresenterKind == OblivionContentPresenterKind.ExternalMermaidRenderer)
            .Select(item => renderer.Render(new OblivionDiagramRenderRequest(
                item.ContentId,
                item.Source,
                item.SourceReference,
                outputDirectory,
                appearance,
                workspaceId,
                pageId,
                cardId)))
            .ToArray();
    }
}
