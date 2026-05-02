using Google.Protobuf;
using Google.Protobuf.Collections;
using PgSqlParser;
using TSqlAnalyzer.Domain.Analysis;

using DomainJoinType = TSqlAnalyzer.Domain.Analysis.JoinType;
using PgNode = PgSqlParser.Node;

namespace TSqlAnalyzer.Application.Analysis;

/// <summary>
/// pgsqlparser-dotnet を使って PostgreSQL SQL を解析する実装。
/// PostgreSQL AST を既存の表示用ドメインモデルへ写像する。
/// </summary>
public sealed class PostgreSqlQueryAnalyzer : ISqlQueryAnalyzer
{
    public QueryAnalysisResult Analyze(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new QueryAnalysisResult(
                QueryStatementCategory.Empty,
                [],
                null,
                [],
                [new AnalysisNotice(AnalysisNoticeLevel.Warning, "SQL が入力されていません。")]);
        }

        var parseResult = Parser.Parse(sql);
        if (parseResult.Error is not null)
        {
            return new QueryAnalysisResult(
                QueryStatementCategory.ParseError,
                [],
                null,
                [CreateParseIssue(sql, parseResult.Error)],
                [new AnalysisNotice(AnalysisNoticeLevel.Error, "PostgreSQL SQL を構文解析できませんでした。")]);
        }

        var parseValue = parseResult.Value;
        if (parseValue is null || parseValue.Stmts.Count == 0)
        {
            return new QueryAnalysisResult(
                QueryStatementCategory.Unsupported,
                [],
                null,
                [],
                [new AnalysisNotice(AnalysisNoticeLevel.Warning, "解析対象の文が見つかりませんでした。")]);
        }

        var notices = new List<AnalysisNotice>
        {
            new(AnalysisNoticeLevel.Information, "PostgreSQL 構文として解析しました。")
        };

        if (parseValue.Stmts.Count > 1)
        {
            notices.Add(new AnalysisNotice(
                AnalysisNoticeLevel.Information,
                "複数の文が入力されています。先頭の文を解析対象にします。"));
        }

        var rawStatement = parseValue.Stmts[0];
        var statement = rawStatement.Stmt;
        var withClause = GetWithClause(statement);
        var commonTableExpressionNames = withClause?.Ctes
            .Where(node => node.NodeCase == PgNode.NodeOneofCase.CommonTableExpr)
            .Select(node => node.CommonTableExpr.Ctename)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray() ?? [];

        var core = new AnalyzerCore(sql, notices, commonTableExpressionNames, rawStatement);
        var commonTableExpressions = core.AnalyzeCommonTableExpressions(withClause);
        var dependencyReport = CommonTableExpressionDependencyAnalyzer.Analyze(commonTableExpressions);
        if (dependencyReport.CyclicNames.Count > 0)
        {
            notices.Add(new AnalysisNotice(
                AnalysisNoticeLevel.Information,
                $"再帰または循環する CTE 参照が含まれています: {string.Join(", ", dependencyReport.CyclicNames)}"));
        }

        QueryExpressionAnalysis? query = null;
        DataModificationAnalysis? dataModification = null;
        CreateStatementAnalysis? createStatement = null;
        QueryStatementCategory category;

        switch (statement.NodeCase)
        {
            case PgNode.NodeOneofCase.SelectStmt:
                query = core.AnalyzeQueryExpression(statement.SelectStmt);
                category = query.Kind == QueryExpressionKind.SetOperation
                    ? QueryStatementCategory.SetOperation
                    : QueryStatementCategory.Select;
                break;

            case PgNode.NodeOneofCase.InsertStmt:
                dataModification = core.AnalyzeInsert(statement.InsertStmt);
                category = QueryStatementCategory.Insert;
                break;

            case PgNode.NodeOneofCase.UpdateStmt:
                dataModification = core.AnalyzeUpdate(statement.UpdateStmt);
                category = QueryStatementCategory.Update;
                break;

            case PgNode.NodeOneofCase.DeleteStmt:
                dataModification = core.AnalyzeDelete(statement.DeleteStmt);
                category = QueryStatementCategory.Delete;
                break;

            case PgNode.NodeOneofCase.ViewStmt:
                createStatement = core.AnalyzeCreateView(statement.ViewStmt);
                category = QueryStatementCategory.Create;
                break;

            case PgNode.NodeOneofCase.CreateStmt:
                createStatement = core.AnalyzeCreateTable(statement.CreateStmt);
                category = QueryStatementCategory.Create;
                break;

            case PgNode.NodeOneofCase.CreateTableAsStmt:
                createStatement = core.AnalyzeCreateTableAs(statement.CreateTableAsStmt);
                category = QueryStatementCategory.Create;
                break;

            default:
                notices.Add(new AnalysisNotice(
                    AnalysisNoticeLevel.Warning,
                    $"この PostgreSQL 文種別は未対応です: {statement.NodeCase}"));

                return new QueryAnalysisResult(
                    QueryStatementCategory.Unsupported,
                    commonTableExpressions,
                    null,
                    [],
                    notices);
        }

        return new QueryAnalysisResult(
            category,
            commonTableExpressions,
            query,
            [],
            notices,
            dataModification,
            createStatement);
    }

    private static WithClause? GetWithClause(PgNode statement)
    {
        return statement.NodeCase switch
        {
            PgNode.NodeOneofCase.SelectStmt => statement.SelectStmt.WithClause,
            PgNode.NodeOneofCase.InsertStmt => statement.InsertStmt.WithClause,
            PgNode.NodeOneofCase.UpdateStmt => statement.UpdateStmt.WithClause,
            PgNode.NodeOneofCase.DeleteStmt => statement.DeleteStmt.WithClause,
            PgNode.NodeOneofCase.ViewStmt => statement.ViewStmt.Query?.SelectStmt?.WithClause,
            PgNode.NodeOneofCase.CreateTableAsStmt => statement.CreateTableAsStmt.Query?.SelectStmt?.WithClause,
            _ => null
        };
    }

    private static ParseIssue CreateParseIssue(string sql, Error error)
    {
        var cursorIndex = Math.Max(0, error.CursorPos - 1);
        var line = 1;
        var column = 1;

        for (var index = 0; index < Math.Min(cursorIndex, sql.Length); index++)
        {
            if (sql[index] == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return new ParseIssue(line, column, error.Message ?? "PostgreSQL SQL を構文解析できませんでした。");
    }

    private sealed class AnalyzerCore
    {
        private static readonly HashSet<string> AggregateFunctionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "avg",
            "count",
            "max",
            "min",
            "string_agg",
            "sum"
        };

        private readonly string _sql;
        private readonly ICollection<AnalysisNotice> _notices;
        private readonly HashSet<string> _commonTableExpressionNames;
        private readonly Dictionary<string, CommonTableExpressionAnalysis> _commonTableExpressionRegistry;
        private readonly PostgreSqlTextFormatter _formatter;
        private readonly RawStmt _rawStatement;

        public AnalyzerCore(
            string sql,
            ICollection<AnalysisNotice> notices,
            IEnumerable<string> commonTableExpressionNames,
            RawStmt rawStatement)
        {
            _sql = sql;
            _notices = notices;
            _commonTableExpressionNames = new HashSet<string>(commonTableExpressionNames, StringComparer.OrdinalIgnoreCase);
            _commonTableExpressionRegistry = new Dictionary<string, CommonTableExpressionAnalysis>(StringComparer.OrdinalIgnoreCase);
            _formatter = new PostgreSqlTextFormatter();
            _rawStatement = rawStatement;
        }

        public IReadOnlyList<CommonTableExpressionAnalysis> AnalyzeCommonTableExpressions(WithClause? withClause)
        {
            if (withClause?.Ctes is not { Count: > 0 } commonTableExpressions)
            {
                return [];
            }

            var items = new List<CommonTableExpressionAnalysis>();

            foreach (var node in commonTableExpressions)
            {
                if (node.NodeCase != PgNode.NodeOneofCase.CommonTableExpr)
                {
                    continue;
                }

                var commonTableExpression = node.CommonTableExpr;
                var query = commonTableExpression.Ctequery?.SelectStmt is not null
                    ? AnalyzeQueryExpression(commonTableExpression.Ctequery.SelectStmt)
                    : CreateFallbackSelect($"未対応の CTE 内部構造です: {commonTableExpression.Ctequery?.NodeCase}");
                var columnNames = commonTableExpression.Aliascolnames
                    .Select(ReadIdentifier)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();

                if (columnNames.Length == 0)
                {
                    columnNames = ExtractExposedColumnNames(query);
                }

                var analysis = new CommonTableExpressionAnalysis(
                    commonTableExpression.Ctename,
                    columnNames,
                    query,
                    CreateTextSpan(commonTableExpression.Location, commonTableExpression.Ctename.Length));

                items.Add(analysis);
                _commonTableExpressionRegistry[analysis.Name] = analysis;
            }

            return items;
        }

        public QueryExpressionAnalysis AnalyzeQueryExpression(SelectStmt selectStatement)
        {
            if (selectStatement.Op != SetOperation.SetopNone)
            {
                return AnalyzeSetOperation(selectStatement);
            }

            return AnalyzeSelect(selectStatement);
        }

        public DataModificationAnalysis AnalyzeUpdate(UpdateStmt updateStatement)
        {
            var target = AnalyzeRangeVar(updateStatement.Relation);
            var fromAnalysis = AnalyzeFromClause(updateStatement.FromClause);
            var sourceScope = BuildSourceScope(target, fromAnalysis.MainSource, fromAnalysis.Joins);
            var setClauses = updateStatement.TargetList
                .Where(node => node.NodeCase == PgNode.NodeOneofCase.ResTarget)
                .Select((node, index) => AnalyzeUpdateSetClause(node.ResTarget, index + 1))
                .ToArray();
            var where = AnalyzeCondition(updateStatement.WhereClause, sourceScope);

            return new UpdateStatementAnalysis(
                target,
                null,
                setClauses,
                fromAnalysis.MainSource,
                fromAnalysis.Joins,
                where,
                CreateReturningText(updateStatement.ReturningList),
                null,
                CollectSubqueries(updateStatement.WhereClause, "UPDATE WHERE"),
                CreateRootSpan());
        }

        public DataModificationAnalysis AnalyzeInsert(InsertStmt insertStatement)
        {
            var target = AnalyzeRangeVar(insertStatement.Relation);
            var targetColumns = insertStatement.Cols
                .Where(node => node.NodeCase == PgNode.NodeOneofCase.ResTarget)
                .Select(node => node.ResTarget.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            var insertSource = AnalyzeInsertSource(insertStatement.SelectStmt, targetColumns);

            return new InsertStatementAnalysis(
                target,
                null,
                null,
                targetColumns,
                insertSource,
                CreateReturningText(insertStatement.ReturningList),
                null,
                CollectSubqueries(insertStatement.SelectStmt, "INSERT 入力元"),
                CreateRootSpan());
        }

        public DataModificationAnalysis AnalyzeDelete(DeleteStmt deleteStatement)
        {
            var target = AnalyzeRangeVar(deleteStatement.Relation);
            var usingAnalysis = AnalyzeFromClause(deleteStatement.UsingClause);
            var sourceScope = BuildSourceScope(target, usingAnalysis.MainSource, usingAnalysis.Joins);
            var where = AnalyzeCondition(deleteStatement.WhereClause, sourceScope);

            return new DeleteStatementAnalysis(
                target,
                null,
                usingAnalysis.MainSource,
                usingAnalysis.Joins,
                where,
                CreateReturningText(deleteStatement.ReturningList),
                null,
                CollectSubqueries(deleteStatement.WhereClause, "DELETE WHERE"),
                CreateRootSpan());
        }

        public CreateStatementAnalysis AnalyzeCreateView(ViewStmt viewStatement)
        {
            var query = viewStatement.Query?.SelectStmt is not null
                ? AnalyzeQueryExpression(viewStatement.Query.SelectStmt)
                : CreateFallbackSelect($"未対応の VIEW 内部構造です: {viewStatement.Query?.NodeCase}");
            var columnNames = viewStatement.Aliases
                .Select(ReadIdentifier)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            if (columnNames.Length == 0)
            {
                columnNames = ExtractExposedColumnNames(query);
            }

            return new CreateViewAnalysis(
                BuildRangeVarName(viewStatement.View),
                columnNames,
                query,
                viewStatement.WithCheckOption != ViewCheckOption.NoCheckOption
                    && viewStatement.WithCheckOption != ViewCheckOption.Undefined,
                CreateRootSpan());
        }

        public CreateStatementAnalysis AnalyzeCreateTable(CreateStmt createStatement)
        {
            var columns = createStatement.TableElts
                .Where(node => node.NodeCase == PgNode.NodeOneofCase.ColumnDef)
                .Select((node, index) => AnalyzeCreateTableColumn(node.ColumnDef, index + 1))
                .ToArray();

            return new CreateTableAnalysis(
                BuildRangeVarName(createStatement.Relation),
                columns,
                null,
                CreateRootSpan());
        }

        public CreateStatementAnalysis AnalyzeCreateTableAs(CreateTableAsStmt createTableAsStatement)
        {
            var query = createTableAsStatement.Query?.SelectStmt is not null
                ? AnalyzeQueryExpression(createTableAsStatement.Query.SelectStmt)
                : null;
            var targetName = createTableAsStatement.Into?.Rel is not null
                ? BuildRangeVarName(createTableAsStatement.Into.Rel)
                : "不明";

            return new CreateTableAnalysis(
                targetName,
                [],
                query,
                CreateRootSpan());
        }

        private QueryExpressionAnalysis AnalyzeSetOperation(SelectStmt selectStatement)
        {
            if (selectStatement.Larg is null || selectStatement.Rarg is null)
            {
                return CreateFallbackSelect("集合演算の左右いずれかのクエリが見つかりませんでした。");
            }

            return new SetOperationQueryAnalysis(
                MapSetOperation(selectStatement.Op, selectStatement.All),
                AnalyzeQueryExpression(selectStatement.Larg),
                AnalyzeQueryExpression(selectStatement.Rarg),
                CreateRootSpan());
        }

        private SelectQueryAnalysis AnalyzeSelect(SelectStmt selectStatement)
        {
            var fromAnalysis = AnalyzeFromClause(selectStatement.FromClause);
            var sourceScope = BuildSourceScope(fromAnalysis.MainSource, fromAnalysis.Joins);
            var selectItems = selectStatement.TargetList
                .Where(node => node.NodeCase == PgNode.NodeOneofCase.ResTarget)
                .Select((node, index) => AnalyzeSelectItem(node, index + 1, sourceScope))
                .ToArray();
            var where = AnalyzeCondition(selectStatement.WhereClause, sourceScope);
            var groupBy = AnalyzeGroupBy(selectStatement.GroupClause, sourceScope);
            var having = AnalyzeCondition(selectStatement.HavingClause, sourceScope);
            var orderBy = AnalyzeOrderBy(selectStatement.SortClause, sourceScope, selectItems);
            var subqueries = CollectSubqueries(selectStatement.WhereClause, "WHERE")
                .Concat(CollectSubqueries(selectStatement.HavingClause, "HAVING"))
                .Concat(selectStatement.TargetList.SelectMany(node => CollectSubqueries(node, "SELECT")))
                .Concat(selectStatement.FromClause.SelectMany(node => CollectSubqueries(node, "FROM")))
                .ToArray();

            return new SelectQueryAnalysis(
                selectStatement.DistinctClause.Count > 0,
                CreateLimitText(selectStatement),
                selectItems,
                selectStatement.IntoClause?.Rel is not null ? AnalyzeRangeVar(selectStatement.IntoClause.Rel) : null,
                fromAnalysis.MainSource,
                fromAnalysis.Joins,
                where,
                groupBy,
                having,
                orderBy,
                subqueries,
                CreateRootSpan());
        }

        private SelectItemAnalysis AnalyzeSelectItem(PgNode targetNode, int sequence, IReadOnlyList<SourceAnalysis> sources)
        {
            var target = targetNode.ResTarget;
            var expression = target.Val;
            var expressionText = _formatter.DeparseExpression(expression);
            var displayText = _formatter.DeparseSelectTarget(targetNode);
            var wildcard = TryGetWildcard(expression);
            var columnReferences = ResolveColumnReferences(CollectColumnReferences(expression), sources, []);

            return new SelectItemAnalysis(
                sequence,
                displayText,
                wildcard.IsWildcard ? SelectItemKind.Wildcard : SelectItemKind.Expression,
                expressionText,
                string.IsNullOrWhiteSpace(target.Name) ? null : target.Name,
                GetAggregateFunctionName(expression),
                wildcard.IsWildcard ? wildcard.Kind : SelectWildcardKind.None,
                wildcard.Qualifier,
                CreateTextSpan(target.Location, displayText.Length))
            {
                ColumnReferences = columnReferences,
                CaseExpressions = CollectCaseExpressions(expression)
            };
        }

        private UpdateSetClauseAnalysis AnalyzeUpdateSetClause(ResTarget target, int sequence)
        {
            var targetText = string.IsNullOrWhiteSpace(target.Name)
                ? _formatter.DeparseSelectTarget(new PgNode { ResTarget = target })
                : target.Name;
            var valueText = _formatter.DeparseExpression(target.Val);
            var displayText = $"{targetText} = {valueText}";

            return new UpdateSetClauseAnalysis(
                sequence,
                displayText,
                targetText,
                valueText,
                CreateTextSpan(target.Location, displayText.Length));
        }

        private InsertSourceAnalysis? AnalyzeInsertSource(PgNode? selectNode, IReadOnlyList<string> targetColumns)
        {
            if (selectNode?.SelectStmt is null)
            {
                return null;
            }

            var selectStatement = selectNode.SelectStmt;
            if (selectStatement.ValuesLists.Count > 0)
            {
                var mappingGroups = selectStatement.ValuesLists
                    .Select((node, index) => AnalyzeInsertValuesGroup(node, index + 1, targetColumns))
                    .ToArray();
                var items = mappingGroups
                    .SelectMany(group => group.Mappings.Select(mapping => mapping.ValueText))
                    .ToArray();

                return new InsertSourceAnalysis(
                    InsertSourceKind.Values,
                    "VALUES",
                    items,
                    null,
                    null,
                    mappingGroups,
                    CreateRootSpan());
            }

            var query = AnalyzeQueryExpression(selectStatement);
            return new InsertSourceAnalysis(
                InsertSourceKind.Query,
                "SELECT",
                ExtractExposedColumnNames(query),
                query,
                null,
                [],
                CreateRootSpan());
        }

        private InsertValueMappingGroupAnalysis AnalyzeInsertValuesGroup(
            PgNode node,
            int sequence,
            IReadOnlyList<string> targetColumns)
        {
            var values = node.NodeCase == PgNode.NodeOneofCase.List
                ? node.List.Items
                : new RepeatedField<PgNode> { node };
            var mappings = values
                .Select((valueNode, index) => new InsertValueMappingAnalysis(
                    index + 1,
                    index < targetColumns.Count ? targetColumns[index] : $"#{index + 1}",
                    _formatter.DeparseExpression(valueNode),
                    CreateTextSpan(GetLocation(valueNode), _formatter.DeparseExpression(valueNode).Length)))
                .ToArray();

            return new InsertValueMappingGroupAnalysis(
                $"VALUES #{sequence}",
                mappings,
                MergeSpans(mappings.Select(mapping => mapping.SourceSpan)));
        }

        private CreateTableColumnAnalysis AnalyzeCreateTableColumn(ColumnDef column, int sequence)
        {
            var dataType = BuildTypeName(column.TypeName);
            var displayText = $"{column.Colname} {dataType}";
            if (column.IsNotNull)
            {
                displayText += " NOT NULL";
            }

            return new CreateTableColumnAnalysis(
                sequence,
                column.Colname,
                dataType,
                !column.IsNotNull,
                displayText,
                CreateTextSpan(column.Location, displayText.Length));
        }

        private FromAnalysis AnalyzeFromClause(IEnumerable<PgNode> fromClause)
        {
            SourceAnalysis? mainSource = null;
            var joins = new List<JoinAnalysis>();

            foreach (var node in fromClause)
            {
                var itemAnalysis = AnalyzeFromItemTree(node);
                if (itemAnalysis.MainSource is null)
                {
                    continue;
                }

                if (mainSource is null)
                {
                    mainSource = itemAnalysis.MainSource;
                    joins.AddRange(itemAnalysis.Joins);
                    continue;
                }

                joins.Add(new JoinAnalysis(
                    joins.Count + 1,
                    DomainJoinType.Cross,
                    "CROSS JOIN",
                    itemAnalysis.MainSource,
                    null,
                    [],
                    itemAnalysis.MainSource.SourceSpan));
                joins.AddRange(itemAnalysis.Joins);
            }

            return new FromAnalysis(mainSource, RenumberJoins(joins));
        }

        private FromAnalysis AnalyzeFromItemTree(PgNode node)
        {
            if (node.NodeCase != PgNode.NodeOneofCase.JoinExpr)
            {
                return new FromAnalysis(AnalyzeSource(node), []);
            }

            var joinExpression = node.JoinExpr;
            var left = joinExpression.Larg is not null
                ? AnalyzeFromItemTree(joinExpression.Larg)
                : new FromAnalysis(null, []);
            var target = AnalyzeSource(joinExpression.Rarg);
            var joins = new List<JoinAnalysis>(left.Joins);
            var sourceScope = BuildSourceScope(left.MainSource, joins, target);
            var onConditionParts = AnalyzeJoinConditionParts(joinExpression.Quals, sourceScope);
            var onConditionText = joinExpression.Quals is null
                ? null
                : _formatter.DeparseExpression(joinExpression.Quals);

            joins.Add(new JoinAnalysis(
                joins.Count + 1,
                MapJoinType(joinExpression.Jointype),
                BuildJoinTypeText(joinExpression),
                target,
                onConditionText,
                onConditionParts,
                MergeSpans([target.SourceSpan, CreateTextSpan(GetLocation(joinExpression.Quals), onConditionText?.Length ?? 1)])));

            return new FromAnalysis(left.MainSource ?? target, RenumberJoins(joins));
        }

        private SourceAnalysis AnalyzeSource(PgNode? node)
        {
            if (node is null)
            {
                return new SourceAnalysis("不明", null, SourceKind.Unknown, null);
            }

            return node.NodeCase switch
            {
                PgNode.NodeOneofCase.RangeVar => AnalyzeRangeVar(node.RangeVar),
                PgNode.NodeOneofCase.RangeSubselect => AnalyzeRangeSubselect(node.RangeSubselect),
                _ => new SourceAnalysis(
                    _formatter.DeparseFromItem(node),
                    null,
                    SourceKind.Unknown,
                    null,
                    null,
                    CreateTextSpan(GetLocation(node), _formatter.DeparseFromItem(node).Length))
            };
        }

        private SourceAnalysis AnalyzeRangeVar(RangeVar rangeVar)
        {
            var sourceName = BuildRangeVarName(rangeVar);
            var alias = string.IsNullOrWhiteSpace(rangeVar.Alias?.Aliasname)
                ? null
                : rangeVar.Alias.Aliasname;
            var displayText = alias is null ? sourceName : $"{sourceName} {alias}";
            var sourceKind = _commonTableExpressionNames.Contains(rangeVar.Relname)
                ? SourceKind.CommonTableExpressionReference
                : SourceKind.Object;
            var exposedColumnNames = sourceKind == SourceKind.CommonTableExpressionReference
                && _commonTableExpressionRegistry.TryGetValue(rangeVar.Relname, out var commonTableExpression)
                    ? commonTableExpression.ColumnNames
                    : [];

            return new SourceAnalysis(
                displayText,
                null,
                sourceKind,
                sourceName,
                alias,
                CreateTextSpan(rangeVar.Location, displayText.Length))
            {
                ExposedColumnNames = exposedColumnNames
            };
        }

        private SourceAnalysis AnalyzeRangeSubselect(RangeSubselect rangeSubselect)
        {
            var nestedQuery = rangeSubselect.Subquery?.SelectStmt is not null
                ? AnalyzeQueryExpression(rangeSubselect.Subquery.SelectStmt)
                : CreateFallbackSelect($"未対応の派生テーブル構造です: {rangeSubselect.Subquery?.NodeCase}");
            var alias = string.IsNullOrWhiteSpace(rangeSubselect.Alias?.Aliasname)
                ? null
                : rangeSubselect.Alias.Aliasname;
            var displayText = alias is null ? "(派生テーブル)" : $"(派生テーブル) {alias}";

            return new SourceAnalysis(
                displayText,
                nestedQuery,
                SourceKind.DerivedTable,
                null,
                alias,
                CreateRootSpan())
            {
                ExposedColumnNames = ExtractExposedColumnNames(nestedQuery)
            };
        }

        private IReadOnlyList<JoinConditionPartAnalysis> AnalyzeJoinConditionParts(
            PgNode? condition,
            IReadOnlyList<SourceAnalysis> sources)
        {
            if (condition is null)
            {
                return [];
            }

            var parts = SplitAndConditions(condition);
            return parts
                .Select((part, index) =>
                {
                    var displayText = _formatter.DeparseExpression(part);
                    return new JoinConditionPartAnalysis(
                        index + 1,
                        displayText,
                        CreateTextSpan(GetLocation(part), displayText.Length))
                    {
                        ColumnReferences = ResolveColumnReferences(CollectColumnReferences(part), sources, []),
                        CaseExpressions = CollectCaseExpressions(part)
                    };
                })
                .ToArray();
        }

        private ConditionAnalysis? AnalyzeCondition(PgNode? condition, IReadOnlyList<SourceAnalysis> sources)
        {
            if (condition is null)
            {
                return null;
            }

            var displayText = _formatter.DeparseExpression(condition);
            var markers = CollectConditionMarkers(condition);

            return new ConditionAnalysis(
                displayText,
                AnalyzeConditionNode(condition, sources),
                markers,
                CreateTextSpan(GetLocation(condition), displayText.Length))
            {
                ColumnReferences = ResolveColumnReferences(CollectColumnReferences(condition), sources, []),
                CaseExpressions = CollectCaseExpressions(condition)
            };
        }

        private ConditionNodeAnalysis AnalyzeConditionNode(PgNode condition, IReadOnlyList<SourceAnalysis> sources)
        {
            if (condition.NodeCase == PgNode.NodeOneofCase.BoolExpr)
            {
                var boolExpression = condition.BoolExpr;
                var nodeKind = boolExpression.Boolop switch
                {
                    BoolExprType.AndExpr => ConditionNodeKind.And,
                    BoolExprType.OrExpr => ConditionNodeKind.Or,
                    BoolExprType.NotExpr => ConditionNodeKind.Not,
                    _ => ConditionNodeKind.Predicate
                };
                var displayText = _formatter.DeparseExpression(condition);

                return new ConditionNodeAnalysis(
                    nodeKind,
                    displayText,
                    boolExpression.Args.Select(arg => AnalyzeConditionNode(arg, sources)).ToArray(),
                    ConditionPredicateKind.Unknown,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown,
                    false,
                    null,
                    CreateTextSpan(boolExpression.Location, displayText.Length))
                {
                    ColumnReferences = ResolveColumnReferences(CollectColumnReferences(condition), sources, []),
                    CaseExpressions = CollectCaseExpressions(condition)
                };
            }

            var predicate = AnalyzePredicate(condition);
            var text = _formatter.DeparseExpression(condition);
            return new ConditionNodeAnalysis(
                ConditionNodeKind.Predicate,
                text,
                [],
                predicate.PredicateKind,
                predicate.ComparisonKind,
                predicate.NullCheckKind,
                predicate.BetweenKind,
                predicate.LikeKind,
                false,
                CollectConditionMarkers(condition).FirstOrDefault(),
                CreateTextSpan(GetLocation(condition), text.Length))
            {
                ColumnReferences = ResolveColumnReferences(CollectColumnReferences(condition), sources, []),
                CaseExpressions = CollectCaseExpressions(condition)
            };
        }

        private GroupByAnalysis? AnalyzeGroupBy(IEnumerable<PgNode> groupClause, IReadOnlyList<SourceAnalysis> sources)
        {
            var items = groupClause.ToArray();
            if (items.Length == 0)
            {
                return null;
            }

            var groupingItems = items
                .Select((node, index) =>
                {
                    var expressionText = _formatter.DeparseExpression(node);
                    return new GroupByItemAnalysis(
                        index + 1,
                        expressionText,
                        expressionText,
                        CreateTextSpan(GetLocation(node), expressionText.Length))
                    {
                        ColumnReferences = ResolveColumnReferences(CollectColumnReferences(node), sources, [])
                    };
                })
                .ToArray();
            var displayText = string.Join(", ", groupingItems.Select(item => item.ExpressionText));

            return new GroupByAnalysis(
                groupingItems.Select(item => item.ExpressionText).ToArray(),
                displayText,
                MergeSpans(groupingItems.Select(item => item.SourceSpan)))
            {
                GroupingItems = groupingItems,
                ColumnReferences = groupingItems.SelectMany(item => item.ColumnReferences).ToArray()
            };
        }

        private OrderByAnalysis? AnalyzeOrderBy(
            IEnumerable<PgNode> sortClause,
            IReadOnlyList<SourceAnalysis> sources,
            IReadOnlyList<SelectItemAnalysis> selectItems)
        {
            var items = sortClause
                .Where(node => node.NodeCase == PgNode.NodeOneofCase.SortBy)
                .ToArray();
            if (items.Length == 0)
            {
                return null;
            }

            var orderItems = items
                .Select((node, index) =>
                {
                    var sortBy = node.SortBy;
                    var expressionText = _formatter.DeparseExpression(sortBy.Node);
                    var displayText = sortBy.SortbyDir switch
                    {
                        SortByDir.SortbyAsc => $"{expressionText} ASC",
                        SortByDir.SortbyDesc => $"{expressionText} DESC",
                        _ => expressionText
                    };

                    return new OrderByItemAnalysis(
                        index + 1,
                        displayText,
                        expressionText,
                        MapOrderByDirection(sortBy.SortbyDir),
                        CreateTextSpan(GetLocation(sortBy.Node), displayText.Length))
                    {
                        ColumnReferences = ResolveColumnReferences(CollectColumnReferences(sortBy.Node), sources, selectItems)
                    };
                })
                .ToArray();
            var display = string.Join(", ", orderItems.Select(item => item.DisplayText));

            return new OrderByAnalysis(
                orderItems.Select(item => item.DisplayText).ToArray(),
                display,
                MergeSpans(orderItems.Select(item => item.SourceSpan)))
            {
                OrderItems = orderItems,
                ColumnReferences = orderItems.SelectMany(item => item.ColumnReferences).ToArray()
            };
        }

        private IReadOnlyList<SubqueryAnalysis> CollectSubqueries(PgNode? node, string location)
        {
            var subqueries = new List<SubqueryAnalysis>();

            VisitNodes(
                node,
                current =>
                {
                    if (current.NodeCase == PgNode.NodeOneofCase.SubLink
                        && current.SubLink.Subselect?.SelectStmt is not null)
                    {
                        var query = AnalyzeQueryExpression(current.SubLink.Subselect.SelectStmt);
                        subqueries.Add(new SubqueryAnalysis(
                            location,
                            _formatter.DeparseSelect(current.SubLink.Subselect.SelectStmt),
                            query,
                            CreateTextSpan(current.SubLink.Location, _formatter.DeparseSelect(current.SubLink.Subselect.SelectStmt).Length)));
                    }
                    else if (current.NodeCase == PgNode.NodeOneofCase.RangeSubselect
                        && current.RangeSubselect.Subquery?.SelectStmt is not null)
                    {
                        var query = AnalyzeQueryExpression(current.RangeSubselect.Subquery.SelectStmt);
                        subqueries.Add(new SubqueryAnalysis(
                            location,
                            _formatter.DeparseSelect(current.RangeSubselect.Subquery.SelectStmt),
                            query,
                            CreateRootSpan()));
                    }
                },
                skipNestedQueries: false);

            return subqueries;
        }

        private IReadOnlyList<ConditionMarker> CollectConditionMarkers(PgNode? node)
        {
            var markers = new List<ConditionMarker>();

            VisitNodes(
                node,
                current =>
                {
                    if (current.NodeCase == PgNode.NodeOneofCase.SubLink
                        && current.SubLink.Subselect?.SelectStmt is not null)
                    {
                        var markerType = current.SubLink.SubLinkType == SubLinkType.ExistsSublink
                            ? ConditionMarkerType.Exists
                            : ConditionMarkerType.In;
                        var query = AnalyzeQueryExpression(current.SubLink.Subselect.SelectStmt);
                        var displayText = _formatter.DeparseExpression(current);
                        markers.Add(new ConditionMarker(
                            markerType,
                            displayText,
                            query,
                            CreateTextSpan(current.SubLink.Location, displayText.Length)));
                    }
                    else if (current.NodeCase == PgNode.NodeOneofCase.AExpr
                        && current.AExpr.Kind == A_Expr_Kind.AexprIn)
                    {
                        var displayText = _formatter.DeparseExpression(current);
                        markers.Add(new ConditionMarker(
                            ConditionMarkerType.In,
                            displayText,
                            current.AExpr.Rexpr?.SubLink?.Subselect?.SelectStmt is not null
                                ? AnalyzeQueryExpression(current.AExpr.Rexpr.SubLink.Subselect.SelectStmt)
                                : null,
                            CreateTextSpan(current.AExpr.Location, displayText.Length)));
                    }
                },
                skipNestedQueries: true);

            return markers;
        }

        private IReadOnlyList<ColumnReferenceAnalysis> CollectColumnReferences(PgNode? node)
        {
            var columns = new List<ColumnReferenceAnalysis>();

            VisitNodes(
                node,
                current =>
                {
                    if (current.NodeCase != PgNode.NodeOneofCase.ColumnRef)
                    {
                        return;
                    }

                    var column = AnalyzeColumnReference(current.ColumnRef, columns.Count + 1);
                    if (column is not null)
                    {
                        columns.Add(column);
                    }
                },
                skipNestedQueries: true);

            return columns;
        }

        private IReadOnlyList<CaseExpressionAnalysis> CollectCaseExpressions(PgNode? node)
        {
            var cases = new List<CaseExpressionAnalysis>();

            VisitNodes(
                node,
                current =>
                {
                    if (current.NodeCase != PgNode.NodeOneofCase.CaseExpr)
                    {
                        return;
                    }

                    var caseExpression = current.CaseExpr;
                    var displayText = _formatter.DeparseExpression(current);
                    var kind = caseExpression.Arg is null
                        ? CaseExpressionKind.Searched
                        : CaseExpressionKind.Simple;
                    var whenClauses = caseExpression.Args
                        .Where(arg => arg.NodeCase == PgNode.NodeOneofCase.CaseWhen)
                        .Select((arg, index) => new CaseWhenClauseAnalysis(
                            index + 1,
                            _formatter.DeparseExpression(arg.CaseWhen.Expr),
                            _formatter.DeparseExpression(arg.CaseWhen.Result),
                            CreateTextSpan(arg.CaseWhen.Location, _formatter.DeparseExpression(arg.CaseWhen.Expr).Length)))
                        .ToArray();

                    cases.Add(new CaseExpressionAnalysis(
                        cases.Count + 1,
                        kind,
                        displayText,
                        caseExpression.Arg is null ? null : _formatter.DeparseExpression(caseExpression.Arg),
                        whenClauses,
                        caseExpression.Defresult is null ? null : _formatter.DeparseExpression(caseExpression.Defresult),
                        CreateTextSpan(caseExpression.Location, displayText.Length)));
                },
                skipNestedQueries: true);

            return cases;
        }

        private ColumnReferenceAnalysis? AnalyzeColumnReference(ColumnRef columnRef, int sequence)
        {
            var parts = columnRef.Fields.Select(ReadIdentifier).Where(part => part.Length > 0).ToArray();
            if (parts.Length == 0)
            {
                return null;
            }

            var columnName = parts[^1];
            var qualifier = parts.Length > 1 ? parts[^2] : null;
            var displayText = string.Join(".", parts);

            return new ColumnReferenceAnalysis(
                sequence,
                displayText,
                qualifier,
                columnName,
                CreateTextSpan(columnRef.Location, displayText.Length));
        }

        private IReadOnlyList<ColumnReferenceAnalysis> ResolveColumnReferences(
            IReadOnlyList<ColumnReferenceAnalysis> columns,
            IReadOnlyList<SourceAnalysis> sources,
            IReadOnlyList<SelectItemAnalysis> selectItems)
        {
            return columns
                .Select(column => ResolveColumnReference(column, sources, selectItems))
                .ToArray();
        }

        private static ColumnReferenceAnalysis ResolveColumnReference(
            ColumnReferenceAnalysis column,
            IReadOnlyList<SourceAnalysis> sources,
            IReadOnlyList<SelectItemAnalysis> selectItems)
        {
            if (column.Qualifier is null)
            {
                var aliasMatch = selectItems.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.Alias)
                    && string.Equals(item.Alias, column.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (aliasMatch is not null)
                {
                    return column with
                    {
                        ResolutionStatus = ColumnReferenceResolutionStatus.Resolved,
                        ResolvedTargetKind = ColumnReferenceResolvedTargetKind.SelectAlias,
                        ResolvedTargetDisplayText = aliasMatch.DisplayText,
                        ResolvedSelectItemAlias = aliasMatch.Alias
                    };
                }
            }

            var matches = FindMatchingSources(column, sources);
            if (matches.Count == 1)
            {
                var source = matches[0];
                return column with
                {
                    ResolutionStatus = ColumnReferenceResolutionStatus.Resolved,
                    ResolvedTargetKind = ColumnReferenceResolvedTargetKind.Source,
                    ResolvedTargetDisplayText = source.DisplayText,
                    ResolvedSourceDisplayText = source.DisplayText,
                    ResolvedSourceName = source.SourceName,
                    ResolvedSourceAlias = source.Alias,
                    ResolvedSourceKind = source.SourceKind
                };
            }

            if (matches.Count > 1)
            {
                return column with { ResolutionStatus = ColumnReferenceResolutionStatus.Ambiguous };
            }

            return column;
        }

        private static IReadOnlyList<SourceAnalysis> FindMatchingSources(
            ColumnReferenceAnalysis column,
            IReadOnlyList<SourceAnalysis> sources)
        {
            if (column.Qualifier is not null)
            {
                return sources
                    .Where(source => MatchesQualifier(source, column.Qualifier))
                    .ToArray();
            }

            var explicitColumnMatches = sources
                .Where(source => source.ExposedColumnNames.Any(name =>
                    string.Equals(name, column.ColumnName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (explicitColumnMatches.Length > 0)
            {
                return explicitColumnMatches;
            }

            return sources.Count == 1 ? [sources[0]] : [];
        }

        private static bool MatchesQualifier(SourceAnalysis source, string qualifier)
        {
            if (!string.IsNullOrWhiteSpace(source.Alias)
                && string.Equals(source.Alias, qualifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(source.SourceName))
            {
                return false;
            }

            var lastPart = source.SourceName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return string.Equals(lastPart, qualifier, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<SourceAnalysis> BuildSourceScope(
            SourceAnalysis? mainSource,
            IReadOnlyList<JoinAnalysis> joins)
        {
            return BuildSourceScope(mainSource, joins, null);
        }

        private static IReadOnlyList<SourceAnalysis> BuildSourceScope(
            SourceAnalysis? mainSource,
            IReadOnlyList<JoinAnalysis> joins,
            SourceAnalysis? additionalSource)
        {
            var sources = new List<SourceAnalysis>();
            if (mainSource is not null)
            {
                sources.Add(mainSource);
            }

            sources.AddRange(joins.Select(join => join.TargetSource));
            if (additionalSource is not null)
            {
                sources.Add(additionalSource);
            }

            return sources;
        }

        private static IReadOnlyList<SourceAnalysis> BuildSourceScope(
            SourceAnalysis target,
            SourceAnalysis? mainSource,
            IReadOnlyList<JoinAnalysis> joins)
        {
            return new[] { target }
                .Concat(BuildSourceScope(mainSource, joins))
                .ToArray();
        }

        private static IReadOnlyList<PgNode> SplitAndConditions(PgNode? condition)
        {
            if (condition?.NodeCase == PgNode.NodeOneofCase.BoolExpr
                && condition.BoolExpr.Boolop == BoolExprType.AndExpr)
            {
                return condition.BoolExpr.Args.ToArray();
            }

            return condition is null ? [] : [condition];
        }

        private PredicateAnalysis AnalyzePredicate(PgNode condition)
        {
            if (condition.NodeCase == PgNode.NodeOneofCase.NullTest)
            {
                return new PredicateAnalysis(
                    ConditionPredicateKind.NullCheck,
                    ConditionComparisonKind.Unknown,
                    condition.NullTest.Nulltesttype == NullTestType.IsNotNull
                        ? ConditionNullCheckKind.IsNotNull
                        : ConditionNullCheckKind.IsNull,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown);
            }

            if (condition.NodeCase == PgNode.NodeOneofCase.SubLink)
            {
                return new PredicateAnalysis(
                    condition.SubLink.SubLinkType == SubLinkType.ExistsSublink
                        ? ConditionPredicateKind.Exists
                        : ConditionPredicateKind.In,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown);
            }

            if (condition.NodeCase != PgNode.NodeOneofCase.AExpr)
            {
                return PredicateAnalysis.Unknown;
            }

            var expression = condition.AExpr;
            return expression.Kind switch
            {
                A_Expr_Kind.AexprLike or A_Expr_Kind.AexprIlike or A_Expr_Kind.AexprSimilar => new PredicateAnalysis(
                    ConditionPredicateKind.Like,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Like),
                A_Expr_Kind.AexprBetween => new PredicateAnalysis(
                    ConditionPredicateKind.Between,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Between,
                    ConditionLikeKind.Unknown),
                A_Expr_Kind.AexprNotBetween => new PredicateAnalysis(
                    ConditionPredicateKind.Between,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.NotBetween,
                    ConditionLikeKind.Unknown),
                A_Expr_Kind.AexprIn => new PredicateAnalysis(
                    ConditionPredicateKind.In,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown),
                _ => new PredicateAnalysis(
                    ConditionPredicateKind.Comparison,
                    MapComparisonKind(ReadOperator(expression.Name)),
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown)
            };
        }

        private SelectQueryAnalysis CreateFallbackSelect(string notice)
        {
            _notices.Add(new AnalysisNotice(AnalysisNoticeLevel.Warning, notice));
            return new SelectQueryAnalysis(
                false,
                null,
                [],
                null,
                null,
                [],
                null,
                null,
                null,
                null,
                [],
                CreateRootSpan());
        }

        private string? CreateLimitText(SelectStmt selectStatement)
        {
            if (selectStatement.LimitCount is null)
            {
                return null;
            }

            var limit = _formatter.DeparseExpression(selectStatement.LimitCount);
            if (selectStatement.LimitOffset is null)
            {
                return $"LIMIT {limit}";
            }

            return $"LIMIT {limit} OFFSET {_formatter.DeparseExpression(selectStatement.LimitOffset)}";
        }

        private string? CreateReturningText(RepeatedField<PgNode> returningList)
        {
            return returningList.Count == 0
                ? null
                : $"RETURNING {_formatter.DeparseTargetList(returningList)}";
        }

        private static string[] ExtractExposedColumnNames(QueryExpressionAnalysis query)
        {
            return query switch
            {
                SelectQueryAnalysis selectQuery => selectQuery.SelectItems
                    .Select(item => item.Alias
                        ?? (item.ColumnReferences.Count == 1 ? item.ColumnReferences[0].ColumnName : item.ExpressionText))
                    .Where(name => !string.IsNullOrWhiteSpace(name) && name != "*")
                    .ToArray(),
                SetOperationQueryAnalysis setOperation => ExtractExposedColumnNames(setOperation.LeftQuery),
                _ => []
            };
        }

        private static IReadOnlyList<JoinAnalysis> RenumberJoins(IEnumerable<JoinAnalysis> joins)
        {
            return joins
                .Select((join, index) => join with { Sequence = index + 1 })
                .ToArray();
        }

        private static DomainJoinType MapJoinType(PgSqlParser.JoinType joinType)
        {
            return joinType switch
            {
                PgSqlParser.JoinType.JoinLeft => DomainJoinType.Left,
                PgSqlParser.JoinType.JoinRight => DomainJoinType.Right,
                PgSqlParser.JoinType.JoinFull => DomainJoinType.Full,
                _ => DomainJoinType.Inner
            };
        }

        private static string BuildJoinTypeText(JoinExpr joinExpression)
        {
            if (joinExpression.IsNatural)
            {
                return "NATURAL JOIN";
            }

            return joinExpression.Jointype switch
            {
                PgSqlParser.JoinType.JoinLeft => "LEFT JOIN",
                PgSqlParser.JoinType.JoinRight => "RIGHT JOIN",
                PgSqlParser.JoinType.JoinFull => "FULL JOIN",
                PgSqlParser.JoinType.JoinInner => "INNER JOIN",
                _ => "JOIN"
            };
        }

        private static SetOperationType MapSetOperation(SetOperation operation, bool all)
        {
            return operation switch
            {
                SetOperation.SetopUnion => all ? SetOperationType.UnionAll : SetOperationType.Union,
                SetOperation.SetopExcept => SetOperationType.Except,
                SetOperation.SetopIntersect => SetOperationType.Intersect,
                _ => SetOperationType.Union
            };
        }

        private static OrderByDirection MapOrderByDirection(SortByDir direction)
        {
            return direction switch
            {
                SortByDir.SortbyAsc => OrderByDirection.Ascending,
                SortByDir.SortbyDesc => OrderByDirection.Descending,
                _ => OrderByDirection.Unspecified
            };
        }

        private static ConditionComparisonKind MapComparisonKind(string operatorText)
        {
            return operatorText switch
            {
                "=" => ConditionComparisonKind.Equal,
                "<>" or "!=" => ConditionComparisonKind.NotEqual,
                ">" => ConditionComparisonKind.GreaterThan,
                "<" => ConditionComparisonKind.LessThan,
                ">=" => ConditionComparisonKind.GreaterThanOrEqual,
                "<=" => ConditionComparisonKind.LessThanOrEqual,
                _ => ConditionComparisonKind.Unknown
            };
        }

        private static string? GetAggregateFunctionName(PgNode? expression)
        {
            if (expression?.NodeCase != PgNode.NodeOneofCase.FuncCall)
            {
                return null;
            }

            var functionName = ReadQualifiedIdentifier(expression.FuncCall.Funcname);
            return AggregateFunctionNames.Contains(functionName)
                ? functionName.ToUpperInvariant()
                : null;
        }

        private static WildcardInfo TryGetWildcard(PgNode? expression)
        {
            if (expression?.NodeCase == PgNode.NodeOneofCase.AStar)
            {
                return new WildcardInfo(true, SelectWildcardKind.AllColumns, null);
            }

            if (expression?.NodeCase != PgNode.NodeOneofCase.ColumnRef)
            {
                return WildcardInfo.None;
            }

            var fields = expression.ColumnRef.Fields.Select(ReadIdentifier).ToArray();
            if (fields.Length == 1 && fields[0] == "*")
            {
                return new WildcardInfo(true, SelectWildcardKind.AllColumns, null);
            }

            if (fields.Length > 1 && fields[^1] == "*")
            {
                return new WildcardInfo(true, SelectWildcardKind.QualifiedAllColumns, fields[^2]);
            }

            return WildcardInfo.None;
        }

        private static string ReadQualifiedIdentifier(IEnumerable<PgNode> nodes)
        {
            return string.Join(".", nodes.Select(ReadIdentifier).Where(part => part.Length > 0));
        }

        private static string ReadOperator(IEnumerable<PgNode> nodes)
        {
            return string.Join(" ", nodes.Select(ReadIdentifier).Where(part => part.Length > 0));
        }

        private static string ReadIdentifier(PgNode? node)
        {
            return node?.NodeCase switch
            {
                PgNode.NodeOneofCase.String => node.String.Sval,
                PgNode.NodeOneofCase.AStar => "*",
                PgNode.NodeOneofCase.Integer => node.Integer.Ival.ToString(),
                PgNode.NodeOneofCase.Float => node.Float.Fval,
                _ => string.Empty
            };
        }

        private static string BuildRangeVarName(RangeVar rangeVar)
        {
            return string.Join(
                ".",
                new[] { rangeVar.Catalogname, rangeVar.Schemaname, rangeVar.Relname }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static string BuildTypeName(TypeName typeName)
        {
            var name = ReadQualifiedIdentifier(typeName.Names);
            if (typeName.ArrayBounds.Count > 0)
            {
                name += "[]";
            }

            return string.IsNullOrWhiteSpace(name) ? "不明" : name;
        }

        private TextSpan? CreateRootSpan()
        {
            var start = Math.Max(0, _rawStatement.StmtLocation);
            var length = _rawStatement.StmtLen > 0
                ? _rawStatement.StmtLen
                : Math.Max(0, _sql.Length - start);

            return length > 0 ? new TextSpan(start, Math.Min(length, _sql.Length - start)) : null;
        }

        private TextSpan? CreateTextSpan(int location, int length)
        {
            if (location < 0 || location >= _sql.Length)
            {
                return null;
            }

            var safeLength = Math.Max(1, Math.Min(length, _sql.Length - location));
            return new TextSpan(location, safeLength);
        }

        private static TextSpan? MergeSpans(IEnumerable<TextSpan?> spans)
        {
            var values = spans.Where(span => span is not null).Select(span => span!).ToArray();
            if (values.Length == 0)
            {
                return null;
            }

            var start = values.Min(span => span.Start);
            var end = values.Max(span => span.End);
            return new TextSpan(start, end - start);
        }

        private static int GetLocation(PgNode? node)
        {
            return node?.NodeCase switch
            {
                PgNode.NodeOneofCase.RangeVar => node.RangeVar.Location,
                PgNode.NodeOneofCase.ColumnRef => node.ColumnRef.Location,
                PgNode.NodeOneofCase.AExpr => node.AExpr.Location,
                PgNode.NodeOneofCase.BoolExpr => node.BoolExpr.Location,
                PgNode.NodeOneofCase.NullTest => node.NullTest.Location,
                PgNode.NodeOneofCase.SubLink => node.SubLink.Location,
                PgNode.NodeOneofCase.CaseExpr => node.CaseExpr.Location,
                PgNode.NodeOneofCase.CaseWhen => node.CaseWhen.Location,
                PgNode.NodeOneofCase.AConst => node.AConst.Location,
                PgNode.NodeOneofCase.FuncCall => node.FuncCall.Location,
                PgNode.NodeOneofCase.ResTarget => node.ResTarget.Location,
                PgNode.NodeOneofCase.SortBy => node.SortBy.Location,
                PgNode.NodeOneofCase.TypeCast => node.TypeCast.Location,
                _ => -1
            };
        }

        private static void VisitNodes(PgNode? node, Action<PgNode> visitor, bool skipNestedQueries)
        {
            if (node is null)
            {
                return;
            }

            visitor(node);

            if (skipNestedQueries
                && (node.NodeCase == PgNode.NodeOneofCase.SelectStmt
                    || node.NodeCase == PgNode.NodeOneofCase.RangeSubselect))
            {
                return;
            }

            if (skipNestedQueries && node.NodeCase == PgNode.NodeOneofCase.SubLink)
            {
                VisitNodes(node.SubLink.Testexpr, visitor, skipNestedQueries);
                return;
            }

            var payload = GetActivePayload(node);
            if (payload is null)
            {
                return;
            }

            VisitMessage(payload, visitor, skipNestedQueries);
        }

        private static void VisitMessage(object message, Action<PgNode> visitor, bool skipNestedQueries)
        {
            foreach (var property in message.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (skipNestedQueries && property.Name is "Subselect" or "Subquery" or "Ctequery" or "Query")
                {
                    continue;
                }

                var value = property.GetValue(message);
                if (value is PgNode node)
                {
                    VisitNodes(node, visitor, skipNestedQueries);
                }
                else if (value is IEnumerable<PgNode> nodes)
                {
                    foreach (var child in nodes)
                    {
                        VisitNodes(child, visitor, skipNestedQueries);
                    }
                }
            }
        }

        private static object? GetActivePayload(PgNode node)
        {
            return node.NodeCase switch
            {
                PgNode.NodeOneofCase.Alias => node.Alias,
                PgNode.NodeOneofCase.RangeVar => node.RangeVar,
                PgNode.NodeOneofCase.IntoClause => node.IntoClause,
                PgNode.NodeOneofCase.BoolExpr => node.BoolExpr,
                PgNode.NodeOneofCase.SubLink => node.SubLink,
                PgNode.NodeOneofCase.CaseExpr => node.CaseExpr,
                PgNode.NodeOneofCase.CaseWhen => node.CaseWhen,
                PgNode.NodeOneofCase.NullTest => node.NullTest,
                PgNode.NodeOneofCase.ColumnRef => node.ColumnRef,
                PgNode.NodeOneofCase.AExpr => node.AExpr,
                PgNode.NodeOneofCase.TypeCast => node.TypeCast,
                PgNode.NodeOneofCase.FuncCall => node.FuncCall,
                PgNode.NodeOneofCase.ResTarget => node.ResTarget,
                PgNode.NodeOneofCase.SortBy => node.SortBy,
                PgNode.NodeOneofCase.RangeSubselect => node.RangeSubselect,
                PgNode.NodeOneofCase.JoinExpr => node.JoinExpr,
                PgNode.NodeOneofCase.WithClause => node.WithClause,
                PgNode.NodeOneofCase.CommonTableExpr => node.CommonTableExpr,
                PgNode.NodeOneofCase.RawStmt => node.RawStmt,
                PgNode.NodeOneofCase.InsertStmt => node.InsertStmt,
                PgNode.NodeOneofCase.DeleteStmt => node.DeleteStmt,
                PgNode.NodeOneofCase.UpdateStmt => node.UpdateStmt,
                PgNode.NodeOneofCase.SelectStmt => node.SelectStmt,
                PgNode.NodeOneofCase.CreateStmt => node.CreateStmt,
                PgNode.NodeOneofCase.ColumnDef => node.ColumnDef,
                PgNode.NodeOneofCase.ViewStmt => node.ViewStmt,
                PgNode.NodeOneofCase.CreateTableAsStmt => node.CreateTableAsStmt,
                PgNode.NodeOneofCase.List => node.List,
                _ => null
            };
        }

        private sealed record FromAnalysis(SourceAnalysis? MainSource, IReadOnlyList<JoinAnalysis> Joins);

        private sealed record PredicateAnalysis(
            ConditionPredicateKind PredicateKind,
            ConditionComparisonKind ComparisonKind,
            ConditionNullCheckKind NullCheckKind,
            ConditionBetweenKind BetweenKind,
            ConditionLikeKind LikeKind)
        {
            public static PredicateAnalysis Unknown { get; } = new(
                ConditionPredicateKind.Unknown,
                ConditionComparisonKind.Unknown,
                ConditionNullCheckKind.Unknown,
                ConditionBetweenKind.Unknown,
                ConditionLikeKind.Unknown);
        }

        private sealed record WildcardInfo(bool IsWildcard, SelectWildcardKind Kind, string? Qualifier)
        {
            public static WildcardInfo None { get; } = new(false, SelectWildcardKind.None, null);
        }
    }

    private sealed class PostgreSqlTextFormatter
    {
        public string DeparseSelect(SelectStmt selectStatement)
        {
            return DeparseStatement(new PgNode { SelectStmt = selectStatement });
        }

        public string DeparseExpression(PgNode? expression)
        {
            if (expression is null)
            {
                return string.Empty;
            }

            var text = DeparseStatement(new PgNode
            {
                SelectStmt = new SelectStmt
                {
                    TargetList =
                    {
                        new PgNode { ResTarget = new ResTarget { Val = expression } }
                    }
                }
            });

            return TrimSelectPrefix(text);
        }

        public string DeparseSelectTarget(PgNode target)
        {
            var text = DeparseStatement(new PgNode
            {
                SelectStmt = new SelectStmt
                {
                    TargetList = { target }
                }
            });

            return TrimSelectPrefix(text);
        }

        public string DeparseTargetList(IEnumerable<PgNode> targetList)
        {
            var selectStatement = new SelectStmt();
            selectStatement.TargetList.AddRange(targetList);
            return TrimSelectPrefix(DeparseStatement(new PgNode { SelectStmt = selectStatement }));
        }

        public string DeparseFromItem(PgNode fromItem)
        {
            var text = DeparseStatement(new PgNode
            {
                SelectStmt = new SelectStmt
                {
                    TargetList =
                    {
                        new PgNode
                        {
                            ResTarget = new ResTarget
                            {
                                Val = new PgNode { ColumnRef = new ColumnRef { Fields = { new PgNode { AStar = new A_Star() } } } }
                            }
                        }
                    },
                    FromClause = { fromItem }
                }
            });

            const string prefix = "SELECT * FROM ";
            return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? text[prefix.Length..]
                : text;
        }

        private static string DeparseStatement(PgNode statement)
        {
            var result = Parser.Deparse(new ParseResult
            {
                Version = Parser.PgVersionNum,
                Stmts =
                {
                    new RawStmt
                    {
                        Stmt = statement
                    }
                }
            });

            return result.Error is null ? result.Value ?? string.Empty : statement.ToString();
        }

        private static string TrimSelectPrefix(string text)
        {
            const string prefix = "SELECT ";
            return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? text[prefix.Length..]
                : text;
        }
    }
}
