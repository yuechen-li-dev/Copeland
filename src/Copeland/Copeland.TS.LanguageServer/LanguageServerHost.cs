using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;

namespace Copeland.TS.LanguageServer;

public sealed class LanguageServerHost
{
    public const string Version = "0.1.0-preview.1";
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly TextWriter _log;
    private readonly CopelandWorkspace _workspace = new();
    private bool _shutdownRequested;

    public LanguageServerHost(Stream input, Stream output, TextWriter log)
    {
        _input = input;
        _output = output;
        _log = log;
    }

    public int Run()
    {
        try
        {
            while (TryReadMessage(out JsonDocument? message))
            {
                using (message)
                {
                    Handle(message!.RootElement);
                }

                if (_shutdownRequested)
                {
                    break;
                }
            }

            return 0;
        }
        catch (Exception exception)
        {
            _log.WriteLine($"[error] unhandled language-server failure: {exception}");
            return 1;
        }
    }

    private void Handle(JsonElement request)
    {
        string? method = request.TryGetProperty("method", out JsonElement methodElement) ? methodElement.GetString() : null;
        bool hasId = request.TryGetProperty("id", out JsonElement id);
        JsonElement parameters = request.TryGetProperty("params", out JsonElement value) ? value : default;
        try
        {
            object? result = method switch
            {
                "initialize" => Initialize(parameters),
                "shutdown" => Shutdown(),
                "exit" => Exit(),
                "textDocument/didOpen" => DidOpen(parameters),
                "textDocument/didChange" => DidChange(parameters),
                "textDocument/didClose" => DidClose(parameters),
                "textDocument/hover" => Hover(parameters),
                "textDocument/completion" => Completion(parameters),
                "textDocument/definition" => Definition(parameters),
                "textDocument/documentSymbol" => DocumentSymbols(parameters),
                "textDocument/semanticTokens/full" => SemanticTokens(parameters),
                "textDocument/signatureHelp" => SignatureHelp(parameters),
                _ => null,
            };
            if (hasId)
            {
                Write(new { jsonrpc = "2.0", id = JsonSerializer.Deserialize<object>(id.GetRawText()), result });
            }
        }
        catch (LanguageServerException exception)
        {
            if (hasId) Write(new { jsonrpc = "2.0", id = JsonSerializer.Deserialize<object>(id.GetRawText()), error = new { code = -32602, message = exception.Message } });
        }
        catch (Exception exception)
        {
            _log.WriteLine($"[error] {method}: {exception}");
            if (hasId) Write(new { jsonrpc = "2.0", id = JsonSerializer.Deserialize<object>(id.GetRawText()), error = new { code = -32603, message = "Copeland language server failed while handling the request." } });
        }
    }

    private object Initialize(JsonElement parameters)
    {
        _workspace.Initialize(parameters);
        return new
        {
            serverInfo = new { name = "Copeland TS Language Server", version = Version },
            capabilities = new
            {
                // The M0 server accepts a single complete replacement in each
                // didChange notification. LSP value 1 is Full; value 2 would
                // advertise incremental ranges that this host deliberately
                // does not implement.
                textDocumentSync = 1,
                hoverProvider = true,
                completionProvider = new { triggerCharacters = new[] { ".", "<" } },
                definitionProvider = true,
                documentSymbolProvider = true,
                semanticTokensProvider = new { legend = new { tokenTypes = SemanticTokenKinds, tokenModifiers = Array.Empty<string>() }, full = true },
                signatureHelpProvider = new { triggerCharacters = new[] { "(", "," } },
            },
        };
    }

    private object? Shutdown()
    {
        _shutdownRequested = true;
        return null;
    }

    private object? Exit()
    {
        _shutdownRequested = true;
        return null;
    }

    private object? DidOpen(JsonElement parameters)
    {
        JsonElement document = parameters.GetProperty("textDocument");
        string uri = document.GetProperty("uri").GetString()!;
        int version = document.GetProperty("version").GetInt32();
        _workspace.Open(uri, version, document.GetProperty("text").GetString() ?? string.Empty);
        PublishDiagnostics(uri);
        return null;
    }

    private object? DidChange(JsonElement parameters)
    {
        JsonElement document = parameters.GetProperty("textDocument");
        string uri = document.GetProperty("uri").GetString()!;
        JsonElement changes = parameters.GetProperty("contentChanges");
        if (changes.GetArrayLength() != 1 || !changes[0].TryGetProperty("text", out JsonElement text))
        {
            throw new LanguageServerException("CTS-LSP-0001: M0 accepts one full-text content change per notification.");
        }

        int version = document.GetProperty("version").GetInt32();
        string changedText = text.GetString() ?? string.Empty;
        bool changed = _workspace.Change(uri, version, changedText);
        if (changed) PublishDiagnostics(uri);
        return null;
    }

    private object? DidClose(JsonElement parameters)
    {
        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        _workspace.Close(uri);
        PublishDiagnostics(uri);
        return null;
    }

    private object? Hover(JsonElement parameters) => _workspace.Hover(Uri(parameters), Position(parameters));
    private object? Completion(JsonElement parameters) => _workspace.Completion(Uri(parameters), Position(parameters));
    private object? Definition(JsonElement parameters) => _workspace.Definition(Uri(parameters), Position(parameters));
    private object? DocumentSymbols(JsonElement parameters) => _workspace.DocumentSymbols(Uri(parameters));
    private object? SemanticTokens(JsonElement parameters) => _workspace.SemanticTokens(Uri(parameters));
    private object? SignatureHelp(JsonElement parameters) => _workspace.SignatureHelp(Uri(parameters), Position(parameters));

    private void PublishDiagnostics(string uri)
    {
        object[] diagnostics = _workspace.Diagnostics(uri);
        Write(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new { uri, diagnostics } });
    }

    private static string Uri(JsonElement parameters) => parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
    private static JsonElement Position(JsonElement parameters) => parameters.GetProperty("position");

    private bool TryReadMessage(out JsonDocument? message)
    {
        message = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            string? line = ReadLine(_input);
            if (line is null) return false;
            if (line.Length == 0) break;
            int separator = line.IndexOf(':');
            if (separator > 0) headers[line[..separator]] = line[(separator + 1)..].Trim();
        }
        if (!headers.TryGetValue("Content-Length", out string? lengthText) || !int.TryParse(lengthText, out int length) || length < 0)
        {
            throw new LanguageServerException("Invalid JSON-RPC Content-Length header.");
        }
        byte[] content = new byte[length];
        int offset = 0;
        while (offset < content.Length)
        {
            int read = _input.Read(content, offset, content.Length - offset);
            if (read == 0) throw new EndOfStreamException("Unexpected end of LSP message.");
            offset += read;
        }
        message = JsonDocument.Parse(content);
        return true;
    }

    private void Write(object message)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        _output.Write(header);
        _output.Write(bytes);
        _output.Flush();
    }

    private static string? ReadLine(Stream stream)
    {
        var bytes = new List<byte>();
        while (true)
        {
            int value = stream.ReadByte();
            if (value < 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (value == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add((byte)value);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] SemanticTokenKinds = ["keyword", "type", "enum", "enumMember", "function", "parameter", "variable", "property", "namespace", "class", "layout", "layoutProfile", "layoutNode", "layoutSlot", "layoutDimension", "layoutCoordinate"];
}

internal sealed class LanguageServerException(string message) : Exception(message);
