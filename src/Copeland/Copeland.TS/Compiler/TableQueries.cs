using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;

namespace Copeland.TS.Compiler;

/// <summary>CLI-independent, already-tokenized request for an ad-hoc table query.</summary>
public sealed record TableQueryRequest(
    string SourceRelation,
    string? PredicateText,
    IReadOnlyList<TableQueryProjectionRequest> Projection,
    IReadOnlyList<string> GroupKeys,
    IReadOnlyList<TableQueryAggregateRequest> Aggregates,
    IReadOnlyList<TableQueryOrderingRequest> Ordering,
    int Skip,
    int Take,
    string? SourceIdentity = null);

public sealed record TableQueryProjectionRequest(string Column, string? Alias = null);
public sealed record TableQueryAggregateRequest(string Function, string? Input, string Alias);
public sealed record TableQueryOrderingRequest(string Column, string Direction);

public sealed class TableQueryBindingException(string code, string message, int? position = null) : Exception(message)
{
    public string Code { get; } = code;
    public int? Position { get; } = position;
}

public enum TableQueryAggregateKind { Count, Sum, Average, Min, Max }

public sealed record TableQueryColumnProvenance(string Kind, string SourceTable, IReadOnlyList<string> Inputs, IReadOnlyList<string> Relationships, string? Aggregate = null, string? Filter = null);
public sealed record BoundTableQuerySourceColumn(string Name, TableColumnSymbol Symbol, MirTableColumnId MirId, MirType MirType, int SourceIndex);
public sealed record BoundTableQueryProjection(string Name, int SourceIndex, TableQueryColumnProvenance Provenance);
public sealed record BoundTableQueryGroupKey(string Name, int SourceIndex, TableQueryColumnProvenance Provenance);
public sealed record BoundTableQueryAggregate(string Name, TableQueryAggregateKind Kind, int InputIndex, TypeSymbol Type, MirType MirType, TableQueryColumnProvenance Provenance);
public sealed record BoundTableQueryResultColumn(string Name, TypeSymbol Type, MirType MirType, int ValueIndex, TableQueryColumnProvenance Provenance);
public sealed record BoundTableQueryOrderTerm(string Name, int ValueIndex, int? SourceIndex, TypeSymbol Type, MirType MirType, bool Descending);

/// <summary>
/// Semantic query plan used by every frontend. It is immutable and contains no
/// rendering or execution policy.
/// </summary>
public sealed class BoundTableQueryPlan(
    BoundTableDefinition sourceRelation,
    MirTableDefinition sourceMirRelation,
    string? predicateCSharp,
    IReadOnlyList<BoundTableQuerySourceColumn> sourceColumns,
    IReadOnlyList<BoundTableQueryProjection> projection,
    IReadOnlyList<BoundTableQueryGroupKey> groupKeys,
    IReadOnlyList<BoundTableQueryAggregate> aggregates,
    IReadOnlyList<BoundTableQueryResultColumn> resultColumns,
    IReadOnlyList<BoundTableQueryOrderTerm> ordering,
    int skip,
    int take,
    string stableId)
{
    public BoundTableDefinition SourceRelation { get; } = sourceRelation;
    public MirTableDefinition SourceMirRelation { get; } = sourceMirRelation;
    public string? PredicateCSharp { get; } = predicateCSharp;
    public IReadOnlyList<BoundTableQuerySourceColumn> SourceColumns { get; } = sourceColumns;
    public IReadOnlyList<BoundTableQueryProjection> Projection { get; } = projection;
    public IReadOnlyList<BoundTableQueryGroupKey> GroupKeys { get; } = groupKeys;
    public IReadOnlyList<BoundTableQueryAggregate> Aggregates { get; } = aggregates;
    public IReadOnlyList<BoundTableQueryResultColumn> ResultColumns { get; } = resultColumns;
    public IReadOnlyList<BoundTableQueryOrderTerm> Ordering { get; } = ordering;
    public int Skip { get; } = skip;
    public int Take { get; } = take;
    public string StableId { get; } = stableId;
}

/// <summary>
/// Binds an expression/query against an explicit relation scope from an existing
/// bound compilation. The implementation never synthesizes a source module.
/// </summary>
public static class TableQueryBinder
{
    public static BoundTableQueryPlan Bind(BoundCompilation compilation, MirProgram mirProgram, TableQueryRequest request)
    {
        if (request.Skip < 0 || request.Take < 0)
        {
            throw new TableQueryBindingException("COPE-TABLE-QUERY-0014", "Query pagination must be non-negative.");
        }

        BoundTableDefinition source = compilation.Program.Tables.SingleOrDefault(table => table.TableType.Name == request.SourceRelation)
            ?? throw new TableQueryBindingException("COPE-TABLE-QUERY-0005", $"Table '{request.SourceRelation}' was not found.");
        MirTableDefinition mirSource = mirProgram.Tables.Single(table => table.Name == request.SourceRelation);
        IReadOnlyList<TableColumnSymbol> symbols = QueryColumns(source);
        var sourceColumns = symbols.Select((symbol, index) =>
        {
            MirTableColumnDefinition mirColumn = mirSource.Columns.Single(column => column.Name == symbol.Name);
            return new BoundTableQuerySourceColumn(symbol.Name, symbol, mirColumn.Id, mirColumn.ElementType, index);
        }).ToArray();
        var byName = sourceColumns.ToDictionary(column => column.Name, StringComparer.Ordinal);

        string? predicate = null;
        if (!string.IsNullOrWhiteSpace(request.PredicateText))
        {
            CopelandExpressionParseResult parsed = CopelandExpressionParser.Parse(request.PredicateText, request.SourceIdentity);
            if (!parsed.Success)
            {
                DiagnosticFailure(parsed.Diagnostics[0]);
            }

            predicate = NormalizePredicate(request.PredicateText!, sourceColumns);
        }

        if (request.GroupKeys.Count > 0 && request.Aggregates.Count == 0)
        {
            throw new TableQueryBindingException("COPE-TABLE-QUERY-0019", "'--group-by' requires at least one '--aggregate' declaration.");
        }
        if (request.Aggregates.Count > 0 && request.Projection.Count > 0)
        {
            throw new TableQueryBindingException("COPE-TABLE-QUERY-0020", "'--select' cannot be combined with '--aggregate'; aggregates define the result schema.");
        }

        var projection = new List<BoundTableQueryProjection>();
        IEnumerable<TableQueryProjectionRequest> requestedProjection = request.Projection.Count == 0
            ? sourceColumns.Select(column => new TableQueryProjectionRequest(column.Name))
            : request.Projection;
        foreach (TableQueryProjectionRequest item in requestedProjection)
        {
            BoundTableQuerySourceColumn column = RequireColumn(item.Column, byName, source.TableType.Name);
            string name = item.Alias ?? column.Name;
            if (projection.Any(existing => existing.Name == name))
            {
                throw new TableQueryBindingException("COPE-TABLE-QUERY-0007", $"Query selection produces duplicate column '{name}'.");
            }
            projection.Add(new BoundTableQueryProjection(name, column.SourceIndex, Provenance(source, column.Symbol)));
        }

        var groupKeys = new List<BoundTableQueryGroupKey>();
        foreach (string name in request.GroupKeys)
        {
            BoundTableQuerySourceColumn column = RequireColumn(name, byName, source.TableType.Name);
            if (!IsGroupable(column.Symbol.Type))
            {
                throw new TableQueryBindingException("COPE-TABLE-QUERY-0021", $"Column '{name}' of type '{column.Symbol.Type.Name}' cannot be used as a group key.");
            }
            if (groupKeys.Any(key => key.SourceIndex == column.SourceIndex))
            {
                throw new TableQueryBindingException("COPE-TABLE-QUERY-0022", $"Query grouping contains duplicate column '{name}'.");
            }
            groupKeys.Add(new BoundTableQueryGroupKey(column.Name, column.SourceIndex, Provenance(source, column.Symbol)));
        }

        var aggregates = new List<BoundTableQueryAggregate>();
        foreach (TableQueryAggregateRequest requestAggregate in request.Aggregates)
        {
            TableQueryAggregateKind kind = ParseAggregateKind(requestAggregate.Function);
            BoundTableQuerySourceColumn? input = requestAggregate.Input is null ? null : RequireColumn(requestAggregate.Input, byName, source.TableType.Name);
            ValidateAggregate(kind, requestAggregate, input);
            if (aggregates.Any(existing => existing.Name == requestAggregate.Alias) || groupKeys.Any(key => key.Name == requestAggregate.Alias))
            {
                throw new TableQueryBindingException("COPE-TABLE-QUERY-0026", $"Aggregate result name '{requestAggregate.Alias}' is duplicated.");
            }
            TypeSymbol type = kind == TableQueryAggregateKind.Count ? PrimitiveTypeSymbol.Int : input!.Symbol.Type;
            MirType mirType = kind == TableQueryAggregateKind.Count ? new MirNamedType("int") : input!.MirType;
            TableQueryColumnProvenance provenance = kind == TableQueryAggregateKind.Count
                ? new("aggregate", source.TableType.Name, [], [], requestAggregate.Function, predicate)
                : new("aggregate", source.TableType.Name, [input!.Name], Provenance(source, input.Symbol).Relationships, requestAggregate.Function, predicate);
            aggregates.Add(new BoundTableQueryAggregate(requestAggregate.Alias, kind, input?.SourceIndex ?? -1, type, mirType, provenance));
        }

        IReadOnlyList<BoundTableQueryResultColumn> resultColumns = aggregates.Count == 0
            ? projection.Select((item, index) =>
            {
                BoundTableQuerySourceColumn column = sourceColumns[item.SourceIndex];
                return new BoundTableQueryResultColumn(item.Name, column.Symbol.Type, column.MirType, index, item.Provenance);
            }).ToArray()
            : groupKeys.Select((item, index) =>
            {
                BoundTableQuerySourceColumn column = sourceColumns[item.SourceIndex];
                return new BoundTableQueryResultColumn(item.Name, column.Symbol.Type, column.MirType, index, item.Provenance);
            }).Concat(aggregates.Select((item, index) => new BoundTableQueryResultColumn(item.Name, item.Type, item.MirType, groupKeys.Count + index, item.Provenance))).ToArray();

        var ordering = new List<BoundTableQueryOrderTerm>();
        foreach (TableQueryOrderingRequest order in request.Ordering)
        {
            BoundTableQueryResultColumn? column = resultColumns.SingleOrDefault(result => result.Name == order.Column);
            BoundTableQuerySourceColumn? sourceColumn = aggregates.Count == 0 && byName.TryGetValue(order.Column, out BoundTableQuerySourceColumn? foundSource)
                ? foundSource
                : null;
            if (aggregates.Count == 0 && sourceColumn is not null)
            {
                column = resultColumns.SingleOrDefault(result => result.Name == order.Column);
            }
            if (column is null && sourceColumn is null)
            {
                throw new TableQueryBindingException("COPE-TABLE-QUERY-0005", aggregates.Count > 0
                    ? $"Column '{order.Column}' is not present in the aggregate result."
                    : UnknownColumnMessage(order.Column, sourceColumns, source.TableType.Name));
            }
            TypeSymbol orderType = sourceColumn?.Symbol.Type ?? column!.Type;
            MirType orderMirType = sourceColumn?.MirType ?? column!.MirType;
            if (!IsOrderable(orderType))
            {
                throw new TableQueryBindingException("COPE-TABLE-QUERY-0010", $"Column '{order.Column}' of type '{orderType.Name}' is not orderable.");
            }
            bool descending = order.Direction switch
            {
                "asc" or "ascending" => false,
                "desc" or "descending" => true,
                _ => throw new TableQueryBindingException("COPE-TABLE-QUERY-0009", $"Order direction '{order.Direction}' must be 'asc' or 'desc'."),
            };
            ordering.Add(new BoundTableQueryOrderTerm(order.Column, column?.ValueIndex ?? -1, sourceColumn?.SourceIndex, orderType, orderMirType, descending));
        }

        string stableId = StableId(source.TableType.StableIdentity, predicate, projection, groupKeys, aggregates, ordering, request.Skip, request.Take);
        return new BoundTableQueryPlan(source, mirSource, predicate, sourceColumns, projection, groupKeys, aggregates, resultColumns, ordering, request.Skip, request.Take, stableId);
    }

    public static MirTableQueryArtifact Lower(BoundTableQueryPlan plan)
        => new(
            plan.StableId,
            plan.SourceMirRelation.Id,
            plan.SourceRelation.TableType.Name,
            plan.PredicateCSharp,
            plan.SourceColumns.Select(column => new MirTableQueryColumn(column.Name, column.MirId, column.MirType, column.SourceIndex)).ToArray(),
            plan.Projection.Select(item => new MirTableQueryProjection(item.Name, item.SourceIndex)).ToArray(),
            plan.GroupKeys.Select(item => new MirTableQueryGroupKey(item.Name, item.SourceIndex)).ToArray(),
            plan.Aggregates.Select(item => new MirTableQueryAggregate(item.Name, (MirTableQueryAggregateKind)item.Kind, item.InputIndex, item.MirType)).ToArray(),
            plan.ResultColumns.Select(item => new MirTableQueryResultColumn(item.Name, item.MirType, item.ValueIndex, item.Provenance.Kind)).ToArray(),
            plan.Ordering.Select(item => new MirTableQueryOrderTerm(item.ValueIndex, item.MirType, item.Descending, item.SourceIndex)).ToArray(),
            plan.Skip,
            plan.Take,
            "relation=" + plan.SourceRelation.TableType.Name);

    private static void DiagnosticFailure(Diagnostics.Diagnostic diagnostic)
        => throw new TableQueryBindingException("COPE-TABLE-QUERY-0003", "Invalid '--where' expression: " + diagnostic.Message, diagnostic.Position);

    private static IReadOnlyList<TableColumnSymbol> QueryColumns(BoundTableDefinition table)
        => table is BoundDerivedTableDefinition derived ? derived.Projections.Select(projection => projection.Column).ToArray() : table.Columns.Select(column => column.Column).ToArray();

    private static BoundTableQuerySourceColumn RequireColumn(string name, IReadOnlyDictionary<string, BoundTableQuerySourceColumn> columns, string table)
        => columns.TryGetValue(name, out BoundTableQuerySourceColumn? column)
            ? column
            : throw new TableQueryBindingException("COPE-TABLE-QUERY-0005", UnknownColumnMessage(name, columns.Values.ToArray(), table));

    private static string UnknownColumnMessage(string name, IReadOnlyList<BoundTableQuerySourceColumn> columns, string table)
    {
        string? suggestion = columns.OrderBy(column => Distance(name, column.Name)).FirstOrDefault(column => Distance(name, column.Name) <= 3)?.Name;
        return suggestion is null ? $"Column '{name}' was not found in table '{table}'." : $"Column '{name}' was not found in table '{table}'. Did you mean '{suggestion}'?";
    }

    private static TableQueryAggregateKind ParseAggregateKind(string function)
        => function.ToLowerInvariant() switch
        {
            "count" => TableQueryAggregateKind.Count,
            "sum" => TableQueryAggregateKind.Sum,
            "average" => TableQueryAggregateKind.Average,
            "min" => TableQueryAggregateKind.Min,
            "max" => TableQueryAggregateKind.Max,
            _ => throw new TableQueryBindingException("COPE-TABLE-QUERY-0023", $"Aggregate '{function}' is not supported. Use count, sum, average, min, or max."),
        };

    private static void ValidateAggregate(TableQueryAggregateKind kind, TableQueryAggregateRequest request, BoundTableQuerySourceColumn? input)
    {
        if (kind == TableQueryAggregateKind.Count) return;
        if (input is null) throw new TableQueryBindingException("COPE-TABLE-QUERY-0024", $"Aggregate '{request.Function}' requires a direct column input.");
        if (kind is TableQueryAggregateKind.Sum or TableQueryAggregateKind.Average && !TypeFacts.IsNumeric(input.Symbol.Type))
            throw new TableQueryBindingException("COPE-TABLE-QUERY-0025", $"Aggregate '{request.Function}' requires a numeric column, got '{input.Symbol.Type.Name}'.");
        if (kind == TableQueryAggregateKind.Average && input.Symbol.Type == PrimitiveTypeSymbol.Int)
            throw new TableQueryBindingException("COPE-TABLE-QUERY-0025", "Aggregate 'average' is supported only for number columns; convert int values before aggregation.");
        if (kind is TableQueryAggregateKind.Min or TableQueryAggregateKind.Max && !IsOrderable(input.Symbol.Type))
            throw new TableQueryBindingException("COPE-TABLE-QUERY-0025", $"Aggregate '{request.Function}' requires an orderable column, got '{input.Symbol.Type.Name}'.");
    }

    private static TableQueryColumnProvenance Provenance(BoundTableDefinition table, TableColumnSymbol column)
    {
        if (table is not BoundDerivedTableDefinition derived) return new("authored", table.TableType.Name, [column.Name], []);
        BoundDerivedTableColumnDefinition projection = derived.Projections.Single(item => item.Column == column);
        return new(projection.CopiedSourceColumn is null ? "computed" : "copied", derived.SourceTable.Name, projection.SourceColumns, projection.Relationships.Select(join => join.Relationship.SourceTable.Name + "." + join.Relationship.SourceColumn.Name + " -> " + join.Relationship.TargetTable.Name + "." + join.Relationship.TargetKey.Name).ToArray());
    }

    private static bool IsOrderable(TypeSymbol type) => type == PrimitiveTypeSymbol.Int || type == PrimitiveTypeSymbol.Float || type == PrimitiveTypeSymbol.Number || type == PrimitiveTypeSymbol.String;
    private static bool IsGroupable(TypeSymbol type) => IsOrderable(type) || type is EnumTypeSymbol || type == PrimitiveTypeSymbol.Boolean;

    private static string NormalizePredicate(string expression, IReadOnlyList<BoundTableQuerySourceColumn> columns)
    {
        var output = new StringBuilder(expression.Length + 16);
        var byName = columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var enumCases = columns
            .Where(column => column.Symbol.Type is EnumTypeSymbol)
            .SelectMany(column => ((EnumTypeSymbol)column.Symbol.Type).Cases.Where(@case => !@case.HasPayload).Select(@case => new { @case.Name, EnumName = @case.EnumType.Name }))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.EnumName).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        bool inString = false;
        bool escaped = false;
        for (int index = 0; index < expression.Length;)
        {
            char current = expression[index];
            if (inString)
            {
                output.Append(current);
                escaped = escaped ? false : current == '\\';
                if (!escaped && current == '"') inString = false;
                index += 1;
                continue;
            }
            if (current == '"') { inString = true; output.Append(current); index += 1; continue; }
            if (!IsIdentifierStart(current)) { output.Append(current); index += 1; continue; }
            int start = index++;
            while (index < expression.Length && IsIdentifierPart(expression[index])) index += 1;
            string identifier = expression[start..index];
            if (byName.TryGetValue(identifier, out BoundTableQuerySourceColumn? column))
            {
                output.Append("__q").Append(column.SourceIndex);
            }
            else if (enumCases.TryGetValue(identifier, out string[]? enumNames) && enumNames.Length == 1)
            {
                output.Append(enumNames[0]).Append('.').Append(identifier);
            }
            else if (identifier is "true" or "false" || PreviousNonWhitespace(expression, start - 1) == '.')
            {
                output.Append(identifier);
            }
            else
            {
                throw new TableQueryBindingException("COPE-TABLE-QUERY-0003", "Invalid '--where' expression: Name '" + identifier + "' does not exist.", start);
            }
        }
        return output.ToString();
    }

    private static string StableId(string source, string? predicate, IEnumerable<BoundTableQueryProjection> projection, IEnumerable<BoundTableQueryGroupKey> groups, IEnumerable<BoundTableQueryAggregate> aggregates, IEnumerable<BoundTableQueryOrderTerm> ordering, int skip, int take)
    {
        string identity = source + "|" + predicate + "|" + string.Join(",", projection.Select(item => item.Name + ":" + item.SourceIndex)) + "|" + string.Join(",", groups.Select(item => item.SourceIndex)) + "|" + string.Join(",", aggregates.Select(item => item.Name + ":" + item.Kind + ":" + item.InputIndex)) + "|" + string.Join(",", ordering.Select(item => item.Name + ":" + item.SourceIndex + ":" + item.Descending)) + "|" + skip + "|" + take;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }

    private static bool IsIdentifierStart(char value) => char.IsAsciiLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';
    private static char PreviousNonWhitespace(string text, int index)
    {
        while (index >= 0 && char.IsWhiteSpace(text[index])) index -= 1;
        return index < 0 ? '\0' : text[index];
    }
    private static int Distance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex += 1)
        {
            int[] current = new int[right.Length + 1];
            current[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex += 1)
                current[rightIndex] = Math.Min(Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1), previous[rightIndex - 1] + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }
}
