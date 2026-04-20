using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.RegularExpressions;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Analysis;

/// <summary>
/// ScriptDom を使って T-SQL を解析する実装。
/// ScriptDom の構文木を、そのまま UI に渡さず独自モデルへ変換する。
/// </summary>
public sealed class ScriptDomQueryAnalyzer : ISqlQueryAnalyzer
{
    /// <summary>
    /// SQL を解析して独自モデルへ変換する。
    /// </summary>
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

        using var reader = new StringReader(sql);
        var parser = new TSql160Parser(initialQuotedIdentifiers: false);
        var fragment = parser.Parse(reader, out IList<ParseError> parseErrors);

        if (parseErrors.Count > 0)
        {
            var issues = parseErrors
                .Select(error => new ParseIssue(error.Line, error.Column, error.Message))
                .ToArray();

            return new QueryAnalysisResult(
                QueryStatementCategory.ParseError,
                [],
                null,
                issues,
                [new AnalysisNotice(AnalysisNoticeLevel.Error, "SQL を構文解析できませんでした。")]);
        }

        var notices = new List<AnalysisNotice>();
        var statement = GetFirstStatement(fragment, notices);
        if (statement is null)
        {
            notices.Add(new AnalysisNotice(AnalysisNoticeLevel.Warning, "解析対象の文が見つかりませんでした。"));

            return new QueryAnalysisResult(
                QueryStatementCategory.Unsupported,
                [],
                null,
                [],
                notices);
        }

        if (statement is StatementWithCtesAndXmlNamespaces statementWithCtes
            && statementWithCtes.WithCtesAndXmlNamespaces?.XmlNamespaces is not null)
        {
            notices.Add(new AnalysisNotice(
                AnalysisNoticeLevel.Information,
                "XML 名前空間定義が含まれています。クエリ本体と CTE を優先して表示します。"));
        }

        var withClause = (statement as StatementWithCtesAndXmlNamespaces)?.WithCtesAndXmlNamespaces;
        var commonTableExpressionNames = withClause?.CommonTableExpressions?
            .Select(commonTableExpression => commonTableExpression.ExpressionName.Value)
            .ToArray() ?? [];
        var core = new AnalyzerCore(sql, notices, commonTableExpressionNames);
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

        switch (statement)
        {
            case SelectStatement selectStatement when selectStatement.QueryExpression is not null:
                query = core.AnalyzeSelectStatement(selectStatement);
                category = query.Kind == QueryExpressionKind.SetOperation
                    ? QueryStatementCategory.SetOperation
                    : QueryStatementCategory.Select;
                break;

            case CreateViewStatement createViewStatement when createViewStatement.SelectStatement?.QueryExpression is not null:
                createStatement = core.AnalyzeCreateView(createViewStatement);
                category = QueryStatementCategory.Create;
                break;

            case CreateTableStatement createTableStatement:
                createStatement = core.AnalyzeCreateTable(createTableStatement);
                category = QueryStatementCategory.Create;
                break;

            case UpdateStatement updateStatement when updateStatement.UpdateSpecification is not null:
                dataModification = core.AnalyzeUpdate(updateStatement.UpdateSpecification);
                category = QueryStatementCategory.Update;
                break;

            case InsertStatement insertStatement when insertStatement.InsertSpecification is not null:
                dataModification = core.AnalyzeInsert(insertStatement.InsertSpecification);
                category = QueryStatementCategory.Insert;
                break;

            case DeleteStatement deleteStatement when deleteStatement.DeleteSpecification is not null:
                dataModification = core.AnalyzeDelete(deleteStatement.DeleteSpecification);
                category = QueryStatementCategory.Delete;
                break;

            default:
                notices.Add(new AnalysisNotice(AnalysisNoticeLevel.Warning, "この文種別は未対応です。"));
                return new QueryAnalysisResult(
                    QueryStatementCategory.Unsupported,
                    commonTableExpressions,
                    null,
                    [],
                    notices);
        }

        return new QueryAnalysisResult(category, commonTableExpressions, query, [], notices, dataModification, createStatement);
    }

    /// <summary>
    /// ScriptDom が返した断片から、先頭の文だけを取り出す。
    /// 初期版では複数文対応を広げすぎず、先頭文を対象にする。
    /// </summary>
    private static TSqlStatement? GetFirstStatement(TSqlFragment fragment, ICollection<AnalysisNotice> notices)
    {
        if (fragment is TSqlScript script)
        {
            var statements = script.Batches
                .SelectMany(batch => batch.Statements)
                .ToArray();

            if (statements.Length == 0)
            {
                return null;
            }

            if (statements.Length > 1)
            {
                notices.Add(new AnalysisNotice(
                    AnalysisNoticeLevel.Information,
                    "複数の文が入力されています。先頭の文を解析対象にします。"));
            }

            return statements[0];
        }

        return fragment as TSqlStatement;
    }

    /// <summary>
    /// 再帰解析の実装本体。
    /// SQL 原文の切り出しや、注意情報の蓄積をこのクラスへ閉じ込める。
    /// </summary>
    private sealed class AnalyzerCore
    {
        private static readonly HashSet<string> AggregateFunctionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "APPROX_COUNT_DISTINCT",
            "AVG",
            "CHECKSUM_AGG",
            "COUNT",
            "COUNT_BIG",
            "GROUPING",
            "GROUPING_ID",
            "MAX",
            "MIN",
            "STDEV",
            "STDEVP",
            "STRING_AGG",
            "SUM",
            "VAR",
            "VARP"
        };

        private readonly SqlTextExtractor _textExtractor;
        private readonly ICollection<AnalysisNotice> _notices;
        private readonly HashSet<string> _commonTableExpressionNames;
        private readonly Dictionary<string, CommonTableExpressionAnalysis> _commonTableExpressionRegistry;

        public AnalyzerCore(string sql, ICollection<AnalysisNotice> notices, IEnumerable<string> commonTableExpressionNames)
        {
            _textExtractor = new SqlTextExtractor(sql);
            _notices = notices;
            _commonTableExpressionNames = new HashSet<string>(commonTableExpressionNames, StringComparer.OrdinalIgnoreCase);
            _commonTableExpressionRegistry = new Dictionary<string, CommonTableExpressionAnalysis>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// QueryExpression を再帰的に独自モデルへ変換する。
        /// </summary>
        public QueryExpressionAnalysis AnalyzeQueryExpression(QueryExpression queryExpression)
        {
            return queryExpression switch
            {
                QuerySpecification querySpecification => AnalyzeSelect(querySpecification),
                BinaryQueryExpression binaryQueryExpression => AnalyzeSetOperation(binaryQueryExpression),
                QueryParenthesisExpression parenthesisExpression => AnalyzeQueryExpression(parenthesisExpression.QueryExpression),
                _ => CreateFallbackSelect($"未対応の QueryExpression を検出しました: {queryExpression.GetType().Name}")
            };
        }

        /// <summary>
        /// WITH 句の CTE 定義を解析する。
        /// </summary>
        public IReadOnlyList<CommonTableExpressionAnalysis> AnalyzeCommonTableExpressions(WithCtesAndXmlNamespaces? withClause)
        {
            if (withClause?.CommonTableExpressions is not { Count: > 0 } commonTableExpressions)
            {
                return [];
            }

            var items = new List<CommonTableExpressionAnalysis>();

            foreach (var commonTableExpression in commonTableExpressions)
            {
                var query = AnalyzeQueryExpression(commonTableExpression.QueryExpression);
                var columnNames = commonTableExpression.Columns?.Select(column => column.Value).ToArray() ?? [];
                if (columnNames.Length == 0)
                {
                    columnNames = ExtractExposedColumnNames(query);
                }

                var analysis = new CommonTableExpressionAnalysis(
                    commonTableExpression.ExpressionName.Value,
                    columnNames,
                    query,
                    CreateTextSpan(commonTableExpression));

                items.Add(analysis);
                _commonTableExpressionRegistry[analysis.Name] = analysis;
            }

            return items;
        }

        /// <summary>
        /// SelectStatement 全体を解析する。
        /// QueryExpression に加えて、SELECT INTO の出力先もここで補足する。
        /// </summary>
        public QueryExpressionAnalysis AnalyzeSelectStatement(SelectStatement selectStatement)
        {
            if (selectStatement.QueryExpression is null)
            {
                return CreateFallbackSelect("SELECT 文の QueryExpression が見つかりませんでした。");
            }

            var query = AnalyzeQueryExpression(selectStatement.QueryExpression);
            if (query is not SelectQueryAnalysis selectQuery || selectStatement.Into is null)
            {
                return query;
            }

            return selectQuery with
            {
                IntoTarget = CreateSchemaObjectSource(selectStatement.Into),
                SourceSpan = CreateTextSpan(selectStatement)
            };
        }

        /// <summary>
        /// 単一の SELECT 文を解析する。
        /// </summary>
        private SelectQueryAnalysis AnalyzeSelect(QuerySpecification querySpecification)
        {
            var subqueries = new SubqueryAccumulator(_textExtractor);
            SourceAnalysis? mainSource = null;
            IReadOnlyList<JoinAnalysis> joins = [];

            if (querySpecification.FromClause is { TableReferences.Count: > 0 } fromClause)
            {
                var fromResult = AnalyzeTableReference(fromClause.TableReferences[0], "FROM句");
                mainSource = fromResult.MainSource;
                joins = fromResult.Joins;
                subqueries.AddRange(fromResult.Subqueries);

                if (fromClause.TableReferences.Count > 1)
                {
                    _notices.Add(new AnalysisNotice(
                        AnalysisNoticeLevel.Information,
                        "FROM句に複数のソースが含まれています。先頭ソースを基準に解析し、残りは今後の拡張対象です。"));
                }
            }

            var sourceIndex = BuildSourceIndex(mainSource, joins);

            var selectItems = querySpecification.SelectElements
                .Select((element, index) => ResolveColumnReferences(AnalyzeSelectItem(element, index + 1), sourceIndex))
                .ToArray();
            var selectAliasIndex = BuildSelectAliasIndex(selectItems);

            ConditionAnalysis? whereCondition = null;
            if (querySpecification.WhereClause?.SearchCondition is { } searchCondition)
            {
                whereCondition = ResolveColumnReferences(AnalyzeCondition(searchCondition, "WHERE句", subqueries), sourceIndex);
            }

            GroupByAnalysis? groupBy = null;
            if (querySpecification.GroupByClause is { GroupingSpecifications.Count: > 0 } groupByClause)
            {
                var groupingItems = groupByClause.GroupingSpecifications
                    .Select((grouping, index) => AnalyzeGroupByItem(grouping, index + 1))
                    .ToArray();
                var items = groupingItems
                    .Select(groupingItem => groupingItem.DisplayText)
                    .ToArray();

                groupBy = new GroupByAnalysis(items, _textExtractor.Normalize(groupByClause), CreateTextSpan(groupByClause))
                {
                    GroupingItems = groupingItems
                        .Select(groupingItem => ResolveColumnReferences(groupingItem, sourceIndex))
                        .ToArray(),
                    ColumnReferences = ResolveColumnReferences(CollectColumnReferences(groupByClause), sourceIndex)
                };
                CollectImmediateSubqueries(groupByClause, "GROUP BY句", subqueries);
            }

            ConditionAnalysis? havingConditionAnalysis = null;
            if (querySpecification.HavingClause?.SearchCondition is { } havingCondition)
            {
                havingConditionAnalysis = ResolveColumnReferences(AnalyzeCondition(havingCondition, "HAVING句", subqueries), sourceIndex);
            }

            OrderByAnalysis? orderBy = null;
            if (querySpecification.OrderByClause is { OrderByElements.Count: > 0 } orderByClause)
            {
                var orderItems = orderByClause.OrderByElements
                    .Select((orderByElement, index) => AnalyzeOrderByItem(orderByElement, index + 1))
                    .ToArray();
                var items = orderItems
                    .Select(orderItem => orderItem.DisplayText)
                    .ToArray();

                orderBy = new OrderByAnalysis(items, _textExtractor.Normalize(orderByClause), CreateTextSpan(orderByClause))
                {
                    OrderItems = orderItems
                        .Select(orderItem => ResolveColumnReferences(orderItem, sourceIndex, selectAliasIndex))
                        .ToArray(),
                    ColumnReferences = ResolveColumnReferences(CollectColumnReferences(orderByClause), sourceIndex, selectAliasIndex)
                };
                CollectImmediateSubqueries(orderByClause, "ORDER BY句", subqueries);
            }

            foreach (var element in querySpecification.SelectElements)
            {
                CollectImmediateSubqueries(element, "SELECT項目", subqueries);
            }

            return new SelectQueryAnalysis(
                IsDistinct(querySpecification),
                BuildTopExpression(querySpecification.TopRowFilter),
                selectItems,
                null,
                mainSource,
                joins,
                whereCondition,
                groupBy,
                havingConditionAnalysis,
                orderBy,
                subqueries.ToArray(),
                CreateTextSpan(querySpecification));
        }

        /// <summary>
        /// CREATE VIEW 文を解析する。
        /// ビュー名と内部 SELECT を保持し、ビュー定義の中身を追えるようにする。
        /// </summary>
        public CreateViewAnalysis AnalyzeCreateView(CreateViewStatement createViewStatement)
        {
            var query = AnalyzeSelectStatement(createViewStatement.SelectStatement);
            var columnNames = createViewStatement.Columns?
                .Select(column => column.Value)
                .ToArray() ?? [];

            return new CreateViewAnalysis(
                NormalizeSchemaObjectName(createViewStatement.SchemaObjectName),
                columnNames,
                query,
                createViewStatement.WithCheckOption,
                CreateTextSpan(createViewStatement));
        }

        /// <summary>
        /// CREATE TABLE 文を解析する。
        /// 通常の列定義と CTAS の内部 SELECT を、どちらも失わない形で保持する。
        /// </summary>
        public CreateTableAnalysis AnalyzeCreateTable(CreateTableStatement createTableStatement)
        {
            var columns = createTableStatement.Definition?.ColumnDefinitions?
                .Select((column, index) => AnalyzeCreateTableColumn(column, index + 1))
                .ToArray() ?? [];
            var query = createTableStatement.SelectStatement?.QueryExpression is not null
                ? AnalyzeSelectStatement(createTableStatement.SelectStatement)
                : null;

            return new CreateTableAnalysis(
                NormalizeSchemaObjectName(createTableStatement.SchemaObjectName),
                columns,
                query,
                CreateTextSpan(createTableStatement));
        }

        /// <summary>
        /// UPDATE 文を解析する。
        /// 既存の FROM / JOIN / WHERE 解析を再利用しつつ、SET と対象を加える。
        /// </summary>
        public UpdateStatementAnalysis AnalyzeUpdate(UpdateSpecification updateSpecification)
        {
            var target = AnalyzeSource(updateSpecification.Target, "UPDATE対象");
            var subqueries = new SubqueryAccumulator(_textExtractor);
            subqueries.AddRange(target.Subqueries);

            var fromResult = AnalyzeFromClause(updateSpecification.FromClause, subqueries);
            var setClauses = updateSpecification.SetClauses
                .Select((setClause, index) => AnalyzeUpdateSetClause(setClause, index + 1, subqueries))
                .ToArray();

            ConditionAnalysis? whereCondition = null;
            if (updateSpecification.WhereClause?.SearchCondition is { } searchCondition)
            {
                whereCondition = AnalyzeCondition(searchCondition, "WHERE句", subqueries);
            }

            CollectImmediateSubqueries(updateSpecification.OutputClause, "OUTPUT句", subqueries);
            CollectImmediateSubqueries(updateSpecification.OutputIntoClause, "OUTPUT INTO句", subqueries);

            return new UpdateStatementAnalysis(
                target.Source,
                BuildTopExpression(updateSpecification.TopRowFilter),
                setClauses,
                fromResult.MainSource,
                fromResult.Joins,
                whereCondition,
                NormalizeOrNull(updateSpecification.OutputClause),
                NormalizeOrNull(updateSpecification.OutputIntoClause),
                subqueries.ToArray(),
                CreateTextSpan(updateSpecification));
        }

        /// <summary>
        /// INSERT 文を解析する。
        /// 挿入先列と入力元を分けて保持し、VALUES と SELECT の両方へ対応する。
        /// </summary>
        public InsertStatementAnalysis AnalyzeInsert(InsertSpecification insertSpecification)
        {
            var target = AnalyzeSource(insertSpecification.Target, "INSERT対象");
            var subqueries = new SubqueryAccumulator(_textExtractor);
            subqueries.AddRange(target.Subqueries);

            var targetColumns = insertSpecification.Columns?
                .Select(column => _textExtractor.Normalize(column))
                .ToArray() ?? [];
            var insertSource = AnalyzeInsertSource(insertSpecification.InsertSource, targetColumns, subqueries);

            CollectImmediateSubqueries(insertSpecification.OutputClause, "OUTPUT句", subqueries);
            CollectImmediateSubqueries(insertSpecification.OutputIntoClause, "OUTPUT INTO句", subqueries);

            return new InsertStatementAnalysis(
                target.Source,
                BuildTopExpression(insertSpecification.TopRowFilter),
                BuildInsertOptionText(insertSpecification),
                targetColumns,
                insertSource,
                NormalizeOrNull(insertSpecification.OutputClause),
                NormalizeOrNull(insertSpecification.OutputIntoClause),
                subqueries.ToArray(),
                CreateTextSpan(insertSpecification));
        }

        /// <summary>
        /// DELETE 文を解析する。
        /// UPDATE と同様に対象と FROM / JOIN / WHERE を分離して保持する。
        /// </summary>
        public DeleteStatementAnalysis AnalyzeDelete(DeleteSpecification deleteSpecification)
        {
            var target = AnalyzeSource(deleteSpecification.Target, "DELETE対象");
            var subqueries = new SubqueryAccumulator(_textExtractor);
            subqueries.AddRange(target.Subqueries);

            var fromResult = AnalyzeFromClause(deleteSpecification.FromClause, subqueries);

            ConditionAnalysis? whereCondition = null;
            if (deleteSpecification.WhereClause?.SearchCondition is { } searchCondition)
            {
                whereCondition = AnalyzeCondition(searchCondition, "WHERE句", subqueries);
            }

            CollectImmediateSubqueries(deleteSpecification.OutputClause, "OUTPUT句", subqueries);
            CollectImmediateSubqueries(deleteSpecification.OutputIntoClause, "OUTPUT INTO句", subqueries);

            return new DeleteStatementAnalysis(
                target.Source,
                BuildTopExpression(deleteSpecification.TopRowFilter),
                fromResult.MainSource,
                fromResult.Joins,
                whereCondition,
                NormalizeOrNull(deleteSpecification.OutputClause),
                NormalizeOrNull(deleteSpecification.OutputIntoClause),
                subqueries.ToArray(),
                CreateTextSpan(deleteSpecification));
        }

        /// <summary>
        /// SELECT 項目を表示向けの詳細モデルへ変換する。
        /// </summary>
        private SelectItemAnalysis AnalyzeSelectItem(SelectElement element, int sequence)
        {
            return element switch
            {
                SelectScalarExpression scalarExpression => new SelectItemAnalysis(
                    sequence,
                    _textExtractor.Normalize(scalarExpression),
                    SelectItemKind.Expression,
                    _textExtractor.Normalize(scalarExpression.Expression),
                    BuildSelectItemAlias(scalarExpression.ColumnName),
                    DetectAggregateFunctionName(scalarExpression.Expression),
                    SelectWildcardKind.None,
                    null,
                    CreateTextSpan(scalarExpression))
                {
                    ColumnReferences = CollectColumnReferences(scalarExpression.Expression)
                },
                SelectStarExpression starExpression => new SelectItemAnalysis(
                    sequence,
                    _textExtractor.Normalize(starExpression),
                    SelectItemKind.Wildcard,
                    _textExtractor.Normalize(starExpression),
                    null,
                    null,
                    starExpression.Qualifier is null ? SelectWildcardKind.AllColumns : SelectWildcardKind.QualifiedAllColumns,
                    starExpression.Qualifier is null ? null : _textExtractor.Normalize(starExpression.Qualifier),
                    CreateTextSpan(starExpression)),
                SelectSetVariable setVariable => new SelectItemAnalysis(
                    sequence,
                    _textExtractor.Normalize(setVariable),
                    SelectItemKind.VariableAssignment,
                    _textExtractor.Normalize(setVariable.Expression),
                    _textExtractor.Normalize(setVariable.Variable),
                    DetectAggregateFunctionName(setVariable.Expression),
                    SelectWildcardKind.None,
                    null,
                    CreateTextSpan(setVariable))
                {
                    ColumnReferences = CollectColumnReferences(setVariable.Expression)
                },
                _ => new SelectItemAnalysis(
                    sequence,
                    _textExtractor.Normalize(element),
                    SelectItemKind.Unknown,
                    _textExtractor.Normalize(element),
                    null,
                    null,
                    SelectWildcardKind.None,
                    null,
                    CreateTextSpan(element))
                {
                    ColumnReferences = CollectColumnReferences(element)
                }
            };
        }

        /// <summary>
        /// SET 句 1 件分を表示向けへ変換する。
        /// 代入先と値を分けられない場合でも、全文だけは失わないようにする。
        /// </summary>
        private UpdateSetClauseAnalysis AnalyzeUpdateSetClause(SetClause setClause, int sequence, SubqueryAccumulator subqueries)
        {
            CollectImmediateSubqueries(setClause, "SET句", subqueries);

            return setClause switch
            {
                AssignmentSetClause assignmentSetClause => new UpdateSetClauseAnalysis(
                    sequence,
                    _textExtractor.Normalize(setClause),
                    assignmentSetClause.Column is not null
                        ? _textExtractor.Normalize(assignmentSetClause.Column)
                        : NormalizeOrNull(assignmentSetClause.Variable) ?? "(変数)",
                    NormalizeOrNull(assignmentSetClause.NewValue) ?? "NULL",
                    CreateTextSpan(setClause)),
                FunctionCallSetClause functionCallSetClause => new UpdateSetClauseAnalysis(
                    sequence,
                    _textExtractor.Normalize(setClause),
                    "関数呼び出し",
                    NormalizeOrNull(functionCallSetClause.MutatorFunction) ?? _textExtractor.Normalize(setClause),
                    CreateTextSpan(setClause)),
                _ => new UpdateSetClauseAnalysis(
                    sequence,
                    _textExtractor.Normalize(setClause),
                    "(未分類)",
                    _textExtractor.Normalize(setClause),
                    CreateTextSpan(setClause))
            };
        }

        /// <summary>
        /// INSERT 入力元を解析する。
        /// SELECT 入力元では内部クエリも保持し、TreeView から深掘りできるようにする。
        /// </summary>
        private InsertSourceAnalysis? AnalyzeInsertSource(
            InsertSource? insertSource,
            IReadOnlyList<string> targetColumns,
            SubqueryAccumulator subqueries)
        {
            if (insertSource is null)
            {
                return null;
            }

            switch (insertSource)
            {
                case ValuesInsertSource valuesInsertSource:
                {
                    var items = valuesInsertSource.IsDefaultValues
                        ? ["DEFAULT VALUES"]
                        : valuesInsertSource.RowValues
                            .Select(rowValue => _textExtractor.Normalize(rowValue))
                            .ToArray();

                    foreach (var rowValue in valuesInsertSource.RowValues)
                    {
                        CollectImmediateSubqueries(rowValue, "INSERT入力元", subqueries);
                    }

                    return new InsertSourceAnalysis(
                        InsertSourceKind.Values,
                        _textExtractor.Normalize(insertSource),
                        items,
                        null,
                        null,
                        BuildValuesInsertMappingGroups(valuesInsertSource, targetColumns),
                        CreateTextSpan(insertSource));
                }

                case SelectInsertSource selectInsertSource:
                {
                    var query = AnalyzeSelectInsertSourceQuery(selectInsertSource);
                    if (query is not null)
                    {
                        subqueries.Add("INSERT入力元", selectInsertSource.Select, query);
                    }

                    return new InsertSourceAnalysis(
                        InsertSourceKind.Query,
                        _textExtractor.Normalize(insertSource),
                        [],
                        query,
                        null,
                        BuildQueryInsertMappingGroups(query, targetColumns),
                        CreateTextSpan(insertSource));
                }

                case ExecuteInsertSource executeInsertSource:
                    CollectImmediateSubqueries(executeInsertSource.Execute, "INSERT入力元", subqueries);

                    return new InsertSourceAnalysis(
                        InsertSourceKind.Execute,
                        _textExtractor.Normalize(insertSource),
                        [],
                        null,
                        NormalizeOrNull(executeInsertSource.Execute),
                        [],
                        CreateTextSpan(insertSource));

                default:
                    CollectImmediateSubqueries(insertSource, "INSERT入力元", subqueries);

                    return new InsertSourceAnalysis(
                        InsertSourceKind.Unknown,
                        _textExtractor.Normalize(insertSource),
                        [],
                        null,
                        null,
                        [],
                        CreateTextSpan(insertSource));
            }
        }

        /// <summary>
        /// VALUES 入力元の各行を、挿入先列との対応表へ変換する。
        /// </summary>
        private IReadOnlyList<InsertValueMappingGroupAnalysis> BuildValuesInsertMappingGroups(
            ValuesInsertSource valuesInsertSource,
            IReadOnlyList<string> targetColumns)
        {
            if (valuesInsertSource.IsDefaultValues)
            {
                return [];
            }

            return valuesInsertSource.RowValues
                .Select((rowValue, index) => new InsertValueMappingGroupAnalysis(
                    $"行 #{index + 1}",
                    BuildInsertValueMappings(rowValue.ColumnValues, targetColumns),
                    CreateTextSpan(rowValue)))
                .ToArray();
        }

        /// <summary>
        /// INSERT ... SELECT の取得列を、挿入先列との対応表へ変換する。
        /// </summary>
        private IReadOnlyList<InsertValueMappingGroupAnalysis> BuildQueryInsertMappingGroups(
            QueryExpressionAnalysis? query,
            IReadOnlyList<string> targetColumns)
        {
            if (query is not SelectQueryAnalysis selectQuery || selectQuery.SelectItems.Count == 0)
            {
                return [];
            }

            var mappings = selectQuery.SelectItems
                .Select((item, index) => new InsertValueMappingAnalysis(
                    index + 1,
                    ResolveInsertTargetColumn(targetColumns, index, item.Alias),
                    item.ExpressionText,
                    item.SourceSpan))
                .ToArray();

            return
            [
                new InsertValueMappingGroupAnalysis(
                    "列対応",
                    mappings,
                    selectQuery.SourceSpan)
            ];
        }

        /// <summary>
        /// VALUES の 1 行分を列と値の対応へ変換する。
        /// </summary>
        private IReadOnlyList<InsertValueMappingAnalysis> BuildInsertValueMappings(
            IList<ScalarExpression> values,
            IReadOnlyList<string> targetColumns)
        {
            return values
                .Select((value, index) => new InsertValueMappingAnalysis(
                    index + 1,
                    ResolveInsertTargetColumn(targetColumns, index, null),
                    NormalizeOrNull(value) ?? "NULL",
                    CreateTextSpan(value)))
                .ToArray();
        }

        /// <summary>
        /// 挿入先列名が省略されている場合でも、表示用の列名を決める。
        /// </summary>
        private static string ResolveInsertTargetColumn(
            IReadOnlyList<string> targetColumns,
            int index,
            string? fallbackName)
        {
            if (index < targetColumns.Count && !string.IsNullOrWhiteSpace(targetColumns[index]))
            {
                return targetColumns[index];
            }

            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                return fallbackName;
            }

            return $"列 #{index + 1}";
        }

        /// <summary>
        /// SELECT 項目の別名を文字列へ変換する。
        /// </summary>
        private string? BuildSelectItemAlias(IdentifierOrValueExpression? aliasExpression)
        {
            if (aliasExpression is null)
            {
                return null;
            }

            var aliasText = _textExtractor.Normalize(aliasExpression);
            return string.IsNullOrWhiteSpace(aliasText) ? null : aliasText;
        }

        /// <summary>
        /// 式の中で最初に見つかった集計関数名を返す。
        /// </summary>
        private static string? DetectAggregateFunctionName(ScalarExpression? expression)
        {
            if (expression is null)
            {
                return null;
            }

            var visitor = new AggregateFunctionVisitor();
            expression.Accept(visitor);
            return visitor.FirstAggregateFunctionName;
        }

        /// <summary>
        /// GROUP BY 項目 1 件分を解析する。
        /// 項目ごとの式と参照列を分けて保持し、表示を細かくできるようにする。
        /// </summary>
        private GroupByItemAnalysis AnalyzeGroupByItem(GroupingSpecification groupingSpecification, int sequence)
        {
            var expressionText = groupingSpecification switch
            {
                ExpressionGroupingSpecification expressionGrouping => _textExtractor.Normalize(expressionGrouping.Expression),
                _ => _textExtractor.Normalize(groupingSpecification)
            };

            return new GroupByItemAnalysis(
                sequence,
                _textExtractor.Normalize(groupingSpecification),
                expressionText,
                CreateTextSpan(groupingSpecification))
            {
                ColumnReferences = CollectColumnReferences(groupingSpecification)
            };
        }

        /// <summary>
        /// ORDER BY 項目 1 件分を解析する。
        /// 式と方向を構造化し、列参照も別で保持する。
        /// </summary>
        private OrderByItemAnalysis AnalyzeOrderByItem(ExpressionWithSortOrder orderByElement, int sequence)
        {
            return new OrderByItemAnalysis(
                sequence,
                _textExtractor.Normalize(orderByElement),
                _textExtractor.Normalize(orderByElement.Expression),
                MapOrderByDirection(orderByElement.SortOrder),
                CreateTextSpan(orderByElement))
            {
                ColumnReferences = CollectColumnReferences(orderByElement.Expression)
            };
        }

        /// <summary>
        /// 任意の断片に含まれる列参照を順序付きで集める。
        /// サブクエリ内部は別構造で追えるため、ここでは潜らずに止める。
        /// </summary>
        private IReadOnlyList<ColumnReferenceAnalysis> CollectColumnReferences(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return [];
            }

            var collector = new ColumnReferenceCollector(_textExtractor);
            fragment.Accept(collector);
            return collector.Items;
        }

        /// <summary>
        /// 現在のクエリで利用できるソース一覧から、列解決用の索引を作る。
        /// 修飾子付きだけでなく、単一ソースや明示列による未修飾列解決にも使う。
        /// </summary>
        private static SourceResolutionIndex BuildSourceIndex(
            SourceAnalysis? mainSource,
            IReadOnlyList<JoinAnalysis> joins,
            params SourceAnalysis[] additionalSources)
        {
            var entries = new Dictionary<string, List<SourceAnalysis>>(StringComparer.OrdinalIgnoreCase);
            var explicitColumnEntries = new Dictionary<string, List<SourceAnalysis>>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<SourceAnalysis>();

            AddSourceToIndex(entries, explicitColumnEntries, sources, mainSource);

            foreach (var join in joins)
            {
                AddSourceToIndex(entries, explicitColumnEntries, sources, join.TargetSource);
            }

            foreach (var source in additionalSources)
            {
                AddSourceToIndex(entries, explicitColumnEntries, sources, source);
            }

            return new SourceResolutionIndex(entries, explicitColumnEntries, sources);
        }

        /// <summary>
        /// SELECT 項目別名の索引を作る。
        /// ORDER BY で別名参照が使われた場合に、元の SELECT 項目へ戻れるようにする。
        /// </summary>
        private static SelectAliasResolutionIndex BuildSelectAliasIndex(IReadOnlyList<SelectItemAnalysis> selectItems)
        {
            var entries = new Dictionary<string, List<SelectItemAnalysis>>(StringComparer.OrdinalIgnoreCase);

            foreach (var selectItem in selectItems)
            {
                if (string.IsNullOrWhiteSpace(selectItem.Alias))
                {
                    continue;
                }

                if (!entries.TryGetValue(selectItem.Alias, out var items))
                {
                    items = [];
                    entries[selectItem.Alias] = items;
                }

                items.Add(selectItem);
            }

            return new SelectAliasResolutionIndex(entries);
        }

        /// <summary>
        /// SELECT 項目の列参照をソースへ解決する。
        /// </summary>
        private static SelectItemAnalysis ResolveColumnReferences(SelectItemAnalysis selectItem, SourceResolutionIndex sourceIndex)
        {
            return selectItem with
            {
                ColumnReferences = ResolveColumnReferences(selectItem.ColumnReferences, sourceIndex)
            };
        }

        /// <summary>
        /// GROUP BY 項目の列参照をソースへ解決する。
        /// </summary>
        private static GroupByItemAnalysis ResolveColumnReferences(GroupByItemAnalysis groupByItem, SourceResolutionIndex sourceIndex)
        {
            return groupByItem with
            {
                ColumnReferences = ResolveColumnReferences(groupByItem.ColumnReferences, sourceIndex)
            };
        }

        /// <summary>
        /// ORDER BY 項目の列参照をソースまたは SELECT 別名へ解決する。
        /// </summary>
        private static OrderByItemAnalysis ResolveColumnReferences(
            OrderByItemAnalysis orderByItem,
            SourceResolutionIndex sourceIndex,
            SelectAliasResolutionIndex selectAliasIndex)
        {
            return orderByItem with
            {
                ColumnReferences = ResolveColumnReferences(orderByItem.ColumnReferences, sourceIndex, selectAliasIndex)
            };
        }

        /// <summary>
        /// JOIN 条件分割の列参照をソースへ解決する。
        /// </summary>
        private static IReadOnlyList<JoinConditionPartAnalysis> ResolveColumnReferences(
            IReadOnlyList<JoinConditionPartAnalysis> joinConditionParts,
            SourceResolutionIndex sourceIndex)
        {
            return joinConditionParts
                .Select(joinConditionPart => joinConditionPart with
                {
                    ColumnReferences = ResolveColumnReferences(joinConditionPart.ColumnReferences, sourceIndex)
                })
                .ToArray();
        }

        /// <summary>
        /// 条件式全体の列参照をソースへ解決する。
        /// </summary>
        private static ConditionAnalysis ResolveColumnReferences(ConditionAnalysis condition, SourceResolutionIndex sourceIndex)
        {
            return condition with
            {
                ColumnReferences = ResolveColumnReferences(condition.ColumnReferences, sourceIndex),
                RootNode = ResolveColumnReferences(condition.RootNode, sourceIndex)
            };
        }

        /// <summary>
        /// 条件式ノードの列参照を再帰的にソースへ解決する。
        /// </summary>
        private static ConditionNodeAnalysis ResolveColumnReferences(ConditionNodeAnalysis node, SourceResolutionIndex sourceIndex)
        {
            return node with
            {
                ColumnReferences = ResolveColumnReferences(node.ColumnReferences, sourceIndex),
                Children = node.Children
                    .Select(child => ResolveColumnReferences(child, sourceIndex))
                    .ToArray()
            };
        }

        /// <summary>
        /// 生の列参照一覧を解決済みへ変換する。
        /// </summary>
        private static IReadOnlyList<ColumnReferenceAnalysis> ResolveColumnReferences(
            IReadOnlyList<ColumnReferenceAnalysis> columnReferences,
            SourceResolutionIndex sourceIndex,
            SelectAliasResolutionIndex? selectAliasIndex = null)
        {
            return columnReferences
                .Select(columnReference => ResolveColumnReference(columnReference, sourceIndex, selectAliasIndex))
                .ToArray();
        }

        /// <summary>
        /// 列参照 1 件をソースまたは SELECT 別名へ解決する。
        /// </summary>
        private static ColumnReferenceAnalysis ResolveColumnReference(
            ColumnReferenceAnalysis columnReference,
            SourceResolutionIndex sourceIndex,
            SelectAliasResolutionIndex? selectAliasIndex = null)
        {
            if (!string.IsNullOrWhiteSpace(columnReference.Qualifier))
            {
                return ResolveToSource(columnReference, sourceIndex.Resolve(columnReference.Qualifier));
            }

            if (selectAliasIndex is not null)
            {
                var aliasCandidates = selectAliasIndex.Resolve(columnReference.ColumnName);
                if (aliasCandidates.Count > 1)
                {
                    return columnReference with
                    {
                        ResolutionStatus = ColumnReferenceResolutionStatus.Ambiguous
                    };
                }

                if (aliasCandidates.Count == 1)
                {
                    return ResolveToSelectAlias(columnReference, aliasCandidates[0]);
                }
            }

            return ResolveToSource(columnReference, sourceIndex.ResolveUnqualified(columnReference.ColumnName));
        }

        /// <summary>
        /// 解決候補がソースの場合の列参照モデルを作る。
        /// </summary>
        private static ColumnReferenceAnalysis ResolveToSource(
            ColumnReferenceAnalysis columnReference,
            IReadOnlyList<SourceAnalysis> candidates)
        {
            if (candidates.Count == 0)
            {
                return columnReference with
                {
                    ResolutionStatus = ColumnReferenceResolutionStatus.Unresolved
                };
            }

            if (candidates.Count > 1)
            {
                return columnReference with
                {
                    ResolutionStatus = ColumnReferenceResolutionStatus.Ambiguous
                };
            }

            var source = candidates[0];
            return columnReference with
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

        /// <summary>
        /// ORDER BY の SELECT 項目別名解決結果を作る。
        /// 元 SELECT 項目が 1 ソースだけを参照している場合は、そのソース情報も引き継ぐ。
        /// </summary>
        private static ColumnReferenceAnalysis ResolveToSelectAlias(
            ColumnReferenceAnalysis columnReference,
            SelectItemAnalysis selectItem)
        {
            var resolvedSources = selectItem.ColumnReferences
                .Where(reference => reference.ResolutionStatus == ColumnReferenceResolutionStatus.Resolved
                    && !string.IsNullOrWhiteSpace(reference.ResolvedSourceDisplayText))
                .GroupBy(reference => reference.ResolvedSourceDisplayText!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            if (resolvedSources.Length == 1)
            {
                var sourceReference = resolvedSources[0];
                return columnReference with
                {
                    ResolutionStatus = ColumnReferenceResolutionStatus.Resolved,
                    ResolvedTargetKind = ColumnReferenceResolvedTargetKind.SelectAlias,
                    ResolvedTargetDisplayText = $"SELECT別名 {selectItem.Alias}: {selectItem.ExpressionText}",
                    ResolvedSelectItemAlias = selectItem.Alias,
                    ResolvedSourceDisplayText = sourceReference.ResolvedSourceDisplayText,
                    ResolvedSourceName = sourceReference.ResolvedSourceName,
                    ResolvedSourceAlias = sourceReference.ResolvedSourceAlias,
                    ResolvedSourceKind = sourceReference.ResolvedSourceKind
                };
            }

            return columnReference with
            {
                ResolutionStatus = ColumnReferenceResolutionStatus.Resolved,
                ResolvedTargetKind = ColumnReferenceResolvedTargetKind.SelectAlias,
                ResolvedTargetDisplayText = $"SELECT別名 {selectItem.Alias}: {selectItem.ExpressionText}",
                ResolvedSelectItemAlias = selectItem.Alias
            };
        }

        /// <summary>
        /// 解決対象となるソースを索引へ追加する。
        /// 別名、完全名、末尾名をキーとして登録する。
        /// </summary>
        private static void AddSourceToIndex(
            IDictionary<string, List<SourceAnalysis>> entries,
            IDictionary<string, List<SourceAnalysis>> explicitColumnEntries,
            ICollection<SourceAnalysis> sources,
            SourceAnalysis? source)
        {
            if (source is null)
            {
                return;
            }

            sources.Add(source);

            foreach (var key in EnumerateSourceKeys(source))
            {
                if (!entries.TryGetValue(key, out var matchedSources))
                {
                    matchedSources = [];
                    entries[key] = matchedSources;
                }

                matchedSources.Add(source);
            }

            foreach (var columnName in source.ExposedColumnNames)
            {
                if (!explicitColumnEntries.TryGetValue(columnName, out var matchedSources))
                {
                    matchedSources = [];
                    explicitColumnEntries[columnName] = matchedSources;
                }

                matchedSources.Add(source);
            }
        }

        /// <summary>
        /// ソース解決に使うキー一覧を返す。
        /// </summary>
        private static IEnumerable<string> EnumerateSourceKeys(SourceAnalysis source)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(source.Alias))
            {
                keys.Add(source.Alias);
            }

            if (!string.IsNullOrWhiteSpace(source.SourceName))
            {
                keys.Add(source.SourceName);

                var segments = source.SourceName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Length > 0)
                {
                    keys.Add(segments[^1]);
                }
            }

            return keys;
        }

        /// <summary>
        /// CTE 名から、その CTE が公開している列名一覧を返す。
        /// 解析済み定義がなければ空配列にする。
        /// </summary>
        private string[] ResolveCommonTableExpressionColumns(string sourceName)
        {
            return _commonTableExpressionRegistry.TryGetValue(sourceName, out var commonTableExpression)
                ? commonTableExpression.ColumnNames
                    .Where(columnName => !string.IsNullOrWhiteSpace(columnName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }

        /// <summary>
        /// クエリ式が外側へ公開する列名一覧を返す。
        /// ワイルドカードが含まれる場合は安全側で空配列にする。
        /// </summary>
        private static string[] ExtractExposedColumnNames(QueryExpressionAnalysis query)
        {
            return query switch
            {
                SelectQueryAnalysis selectQuery => ExtractSelectExposedColumnNames(selectQuery),
                SetOperationQueryAnalysis setOperationQuery => ExtractExposedColumnNames(setOperationQuery.LeftQuery),
                _ => []
            };
        }

        /// <summary>
        /// SELECT 文の出力列名を抽出する。
        /// 項目名を確定できない式が混ざる場合は、その項目だけ除外する。
        /// </summary>
        private static string[] ExtractSelectExposedColumnNames(SelectQueryAnalysis query)
        {
            if (query.SelectItems.Any(selectItem => selectItem.Kind == SelectItemKind.Wildcard))
            {
                return [];
            }

            return query.SelectItems
                .Select(TryGetSelectOutputColumnName)
                .Where(columnName => !string.IsNullOrWhiteSpace(columnName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }

        /// <summary>
        /// SELECT 項目から外側へ見える列名を推定する。
        /// 別名を優先し、単純な列参照なら列名を引き継ぐ。
        /// </summary>
        private static string? TryGetSelectOutputColumnName(SelectItemAnalysis selectItem)
        {
            if (!string.IsNullOrWhiteSpace(selectItem.Alias))
            {
                return selectItem.Alias;
            }

            if (selectItem.Kind != SelectItemKind.Expression)
            {
                return null;
            }

            if (selectItem.ColumnReferences.Count == 1
                && string.Equals(selectItem.ExpressionText, selectItem.ColumnReferences[0].DisplayText, StringComparison.OrdinalIgnoreCase))
            {
                return selectItem.ColumnReferences[0].ColumnName;
            }

            return null;
        }

        /// <summary>
        /// 別名識別子を正規化して返す。
        /// </summary>
        private string? NormalizeAlias(Identifier? alias)
        {
            if (alias is null)
            {
                return null;
            }

            var aliasText = _textExtractor.Normalize(alias);
            return string.IsNullOrWhiteSpace(aliasText) ? null : aliasText;
        }

        /// <summary>
        /// 集合演算を解析する。
        /// </summary>
        private SetOperationQueryAnalysis AnalyzeSetOperation(BinaryQueryExpression binaryQueryExpression)
        {
            if (binaryQueryExpression.OrderByClause is not null)
            {
                _notices.Add(new AnalysisNotice(
                    AnalysisNoticeLevel.Information,
                    "集合演算の末尾にある ORDER BY は解析済みですが、初期版の表示では集合演算ノード内の主要情報を優先しています。"));
            }

            return new SetOperationQueryAnalysis(
                MapSetOperationType(binaryQueryExpression),
                AnalyzeQueryExpression(binaryQueryExpression.FirstQueryExpression),
                AnalyzeQueryExpression(binaryQueryExpression.SecondQueryExpression),
                CreateTextSpan(binaryQueryExpression));
        }

        /// <summary>
        /// FROM 句や JOIN ツリーを平坦化し、主ソースと JOIN 一覧へ分解する。
        /// </summary>
        private TableReferenceAnalysisResult AnalyzeTableReference(TableReference tableReference, string locationLabel)
        {
            switch (tableReference)
            {
                case QualifiedJoin qualifiedJoin:
                {
                    var left = AnalyzeTableReference(qualifiedJoin.FirstTableReference, locationLabel);
                    var right = AnalyzeSource(qualifiedJoin.SecondTableReference, "JOIN句");
                    var joins = left.Joins.ToList();
                    var sourceIndex = BuildSourceIndex(left.MainSource, left.Joins, right.Source);
                    joins.Add(new JoinAnalysis(
                        joins.Count + 1,
                        MapJoinType(qualifiedJoin.QualifiedJoinType),
                        FormatJoinType(qualifiedJoin.QualifiedJoinType),
                        right.Source,
                        NormalizeOrNull(qualifiedJoin.SearchCondition),
                        ResolveColumnReferences(BuildJoinConditionParts(qualifiedJoin.SearchCondition), sourceIndex),
                        CreateTextSpan(qualifiedJoin)));

                    return new TableReferenceAnalysisResult(
                        left.MainSource ?? right.Source,
                        joins,
                        left.Subqueries.Concat(right.Subqueries).ToArray());
                }
                case UnqualifiedJoin unqualifiedJoin:
                {
                    var left = AnalyzeTableReference(unqualifiedJoin.FirstTableReference, locationLabel);
                    var right = AnalyzeSource(unqualifiedJoin.SecondTableReference, "JOIN句");
                    var joins = left.Joins.ToList();
                    var joinType = unqualifiedJoin.UnqualifiedJoinType switch
                    {
                        UnqualifiedJoinType.CrossJoin => JoinType.Cross,
                        _ => JoinType.Unknown
                    };
                    var joinTypeText = unqualifiedJoin.UnqualifiedJoinType switch
                    {
                        UnqualifiedJoinType.CrossJoin => "CROSS JOIN",
                        UnqualifiedJoinType.CrossApply => "CROSS APPLY (未対応)",
                        UnqualifiedJoinType.OuterApply => "OUTER APPLY (未対応)",
                        _ => $"{unqualifiedJoin.UnqualifiedJoinType} (未対応)"
                    };

                    if (unqualifiedJoin.UnqualifiedJoinType is UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply)
                    {
                        _notices.Add(new AnalysisNotice(
                            AnalysisNoticeLevel.Warning,
                            "APPLY は初期版の対象外です。JOIN 一覧には補足として表示します。"));
                    }

                    joins.Add(new JoinAnalysis(
                        joins.Count + 1,
                        joinType,
                        joinTypeText,
                        right.Source,
                        null,
                        [],
                        CreateTextSpan(unqualifiedJoin)));

                    return new TableReferenceAnalysisResult(
                        left.MainSource ?? right.Source,
                        joins,
                        left.Subqueries.Concat(right.Subqueries).ToArray());
                }
                default:
                {
                    var source = AnalyzeSource(tableReference, locationLabel);
                    return new TableReferenceAnalysisResult(source.Source, [], source.Subqueries);
                }
            }
        }

        /// <summary>
        /// 個別のソースを解析する。
        /// ここでは通常テーブルだけでなく、派生テーブルの内部クエリも拾う。
        /// </summary>
        private SourceAnalysisResult AnalyzeSource(TableReference tableReference, string locationLabel)
        {
            if (tableReference is QueryDerivedTable derivedTable)
            {
                var nestedQuery = AnalyzeQueryExpression(derivedTable.QueryExpression);
                var explicitColumnNames = derivedTable.Columns?.Select(column => column.Value).ToArray() ?? [];
                var source = new SourceAnalysis(
                    _textExtractor.Normalize(tableReference),
                    nestedQuery,
                    SourceKind.DerivedTable,
                    null,
                    NormalizeAlias(derivedTable.Alias),
                    CreateTextSpan(tableReference))
                {
                    ExposedColumnNames = explicitColumnNames.Length > 0
                        ? explicitColumnNames
                        : ExtractExposedColumnNames(nestedQuery)
                };
                var subquery = new SubqueryAnalysis(
                    locationLabel,
                    _textExtractor.CreatePreview(derivedTable.QueryExpression),
                    nestedQuery,
                    CreateTextSpan(derivedTable.QueryExpression));

                return new SourceAnalysisResult(source, [subquery]);
            }

            if (tableReference is NamedTableReference namedTableReference)
            {
                var sourceName = NormalizeSchemaObjectName(namedTableReference.SchemaObject);
                var sourceKind = IsCommonTableExpressionReference(namedTableReference.SchemaObject)
                    ? SourceKind.CommonTableExpressionReference
                    : SourceKind.Object;
                var exposedColumnNames = sourceKind == SourceKind.CommonTableExpressionReference
                    ? ResolveCommonTableExpressionColumns(sourceName)
                    : [];

                return new SourceAnalysisResult(
                    new SourceAnalysis(
                        _textExtractor.Normalize(tableReference),
                        null,
                        sourceKind,
                        sourceName,
                        NormalizeAlias(namedTableReference.Alias),
                        CreateTextSpan(tableReference))
                    {
                        ExposedColumnNames = exposedColumnNames
                    },
                    []);
            }

            return new SourceAnalysisResult(
                new SourceAnalysis(
                    _textExtractor.Normalize(tableReference),
                    null,
                    SourceKind.Unknown,
                    null,
                    null,
                    CreateTextSpan(tableReference)),
                []);
        }

        /// <summary>
        /// SchemaObjectName を通常ソースとして扱う表示モデルへ変換する。
        /// SELECT INTO や CREATE 対象など、TableReference ではない名前付き対象で使う。
        /// </summary>
        private SourceAnalysis CreateSchemaObjectSource(SchemaObjectName schemaObjectName)
        {
            var sourceName = NormalizeSchemaObjectName(schemaObjectName);
            var sourceKind = IsCommonTableExpressionReference(schemaObjectName)
                ? SourceKind.CommonTableExpressionReference
                : SourceKind.Object;

            return new SourceAnalysis(
                sourceName,
                null,
                sourceKind,
                sourceName,
                null,
                CreateTextSpan(schemaObjectName));
        }

        /// <summary>
        /// FROM 句を解析し、先頭ソースと JOIN 一覧へ分解する。
        /// UPDATE / DELETE でも同じ見せ方を使えるように共通化する。
        /// </summary>
        private TableReferenceAnalysisResult AnalyzeFromClause(FromClause? fromClause, SubqueryAccumulator subqueries)
        {
            if (fromClause?.TableReferences is not { Count: > 0 })
            {
                return new TableReferenceAnalysisResult(null, [], []);
            }

            var fromResult = AnalyzeTableReference(fromClause.TableReferences[0], "FROM句");
            subqueries.AddRange(fromResult.Subqueries);

            if (fromClause.TableReferences.Count > 1)
            {
                _notices.Add(new AnalysisNotice(
                    AnalysisNoticeLevel.Information,
                    "FROM句に複数のソースが含まれています。先頭ソースを基準に解析し、残りは今後の拡張対象です。"));
            }

            return fromResult;
        }

        /// <summary>
        /// SchemaObjectName をユーザー向けの 1 行表記へ整える。
        /// </summary>
        private static string NormalizeSchemaObjectName(SchemaObjectName schemaObjectName)
        {
            return string.Join(".", schemaObjectName.Identifiers.Select(identifier => identifier.Value));
        }

        /// <summary>
        /// CREATE TABLE の列定義 1 件分を解析する。
        /// 初期版では列名、データ型、NULL 許可の有無に絞って保持する。
        /// </summary>
        private CreateTableColumnAnalysis AnalyzeCreateTableColumn(ColumnDefinition columnDefinition, int sequence)
        {
            var nullableConstraint = columnDefinition.Constraints
                .OfType<NullableConstraintDefinition>()
                .LastOrDefault();
            var isNullable = nullableConstraint?.Nullable ?? true;

            return new CreateTableColumnAnalysis(
                sequence,
                columnDefinition.ColumnIdentifier.Value,
                NormalizeOrNull(columnDefinition.DataType) ?? "不明",
                isNullable,
                _textExtractor.Normalize(columnDefinition),
                CreateTextSpan(columnDefinition));
        }

        /// <summary>
        /// NamedTableReference が CTE 参照かどうかを判定する。
        /// CTE 名は通常 1 パート名なので、その場合だけ CTE とみなす。
        /// </summary>
        private bool IsCommonTableExpressionReference(SchemaObjectName schemaObjectName)
        {
            if (schemaObjectName.Identifiers.Count != 1)
            {
                return false;
            }

            return _commonTableExpressionNames.Contains(schemaObjectName.BaseIdentifier.Value);
        }

        /// <summary>
        /// WHERE / HAVING 条件内の注目ポイントを抽出する。
        /// </summary>
        private ConditionAnalysis AnalyzeCondition(BooleanExpression condition, string locationLabel, SubqueryAccumulator accumulator)
        {
            var collector = new ConditionCollector(this, _textExtractor, locationLabel, accumulator);
            var rootNode = collector.Build(condition);

            return new ConditionAnalysis(
                _textExtractor.Normalize(condition),
                rootNode,
                collector.Markers.ToArray(),
                CreateTextSpan(condition))
            {
                ColumnReferences = CollectColumnReferences(condition)
            };
        }

        /// <summary>
        /// SELECT 項目などに含まれる一般的なサブクエリを拾う。
        /// 即時の子サブクエリだけを集め、孫以下はそのサブクエリ自身に任せる。
        /// </summary>
        private void CollectImmediateSubqueries(TSqlFragment? fragment, string locationLabel, SubqueryAccumulator accumulator)
        {
            if (fragment is null)
            {
                return;
            }

            var collector = new ImmediateSubqueryCollector(this, _textExtractor, locationLabel, accumulator);
            fragment.Accept(collector);
        }

        /// <summary>
        /// DISTINCT の有無を判定する。
        /// </summary>
        private static bool IsDistinct(QuerySpecification querySpecification)
        {
            return querySpecification.UniqueRowFilter == UniqueRowFilter.Distinct;
        }

        /// <summary>
        /// TOP 句を画面向け文字列へ整形する。
        /// </summary>
        private string? BuildTopExpression(TopRowFilter? topRowFilter)
        {
            if (topRowFilter?.Expression is null)
            {
                return null;
            }

            var topText = _textExtractor.Normalize(topRowFilter.Expression);
            if (topRowFilter.Percent)
            {
                topText = $"{topText} PERCENT";
            }

            if (topRowFilter.WithTies)
            {
                topText = $"{topText} WITH TIES";
            }

            return topText;
        }

        /// <summary>
        /// INSERT オプションを表示向け文字列へ整える。
        /// 既定値相当は null とし、明示された INTO / OVER だけを出す。
        /// </summary>
        private static string? BuildInsertOptionText(InsertSpecification insertSpecification)
        {
            var text = insertSpecification.InsertOption.ToString();
            return string.Equals(text, "None", StringComparison.OrdinalIgnoreCase) ? null : text.ToUpperInvariant();
        }

        /// <summary>
        /// INSERT ... SELECT の入力元から内部クエリを取り出す。
        /// ScriptDom の型差を吸収して、SELECT 本体だけを再帰解析へ渡す。
        /// </summary>
        private QueryExpressionAnalysis? AnalyzeSelectInsertSourceQuery(SelectInsertSource selectInsertSource)
        {
            return selectInsertSource.Select switch
            {
                QueryExpression queryExpression => AnalyzeQueryExpression(queryExpression),
                _ => null
            };
        }

        /// <summary>
        /// QualifiedJoinType を表示文字列へ変換する。
        /// </summary>
        private static string FormatJoinType(QualifiedJoinType qualifiedJoinType)
        {
            return qualifiedJoinType switch
            {
                QualifiedJoinType.Inner => "INNER JOIN",
                QualifiedJoinType.LeftOuter => "LEFT JOIN",
                QualifiedJoinType.RightOuter => "RIGHT JOIN",
                QualifiedJoinType.FullOuter => "FULL JOIN",
                _ => $"{qualifiedJoinType} JOIN"
            };
        }

        /// <summary>
        /// QualifiedJoinType を内部 enum へ変換する。
        /// </summary>
        private static JoinType MapJoinType(QualifiedJoinType qualifiedJoinType)
        {
            return qualifiedJoinType switch
            {
                QualifiedJoinType.Inner => JoinType.Inner,
                QualifiedJoinType.LeftOuter => JoinType.Left,
                QualifiedJoinType.RightOuter => JoinType.Right,
                QualifiedJoinType.FullOuter => JoinType.Full,
                _ => JoinType.Unknown
            };
        }

        /// <summary>
        /// BinaryQueryExpression を集合演算 enum へ変換する。
        /// </summary>
        private static SetOperationType MapSetOperationType(BinaryQueryExpression binaryQueryExpression)
        {
            return binaryQueryExpression.BinaryQueryExpressionType switch
            {
                BinaryQueryExpressionType.Union when binaryQueryExpression.All => SetOperationType.UnionAll,
                BinaryQueryExpressionType.Union => SetOperationType.Union,
                BinaryQueryExpressionType.Except => SetOperationType.Except,
                BinaryQueryExpressionType.Intersect => SetOperationType.Intersect,
                _ => SetOperationType.Union
            };
        }

        /// <summary>
        /// ORDER BY の方向を内部 enum へ変換する。
        /// 方向が省略されている場合は ASC 扱いだが、未指定だった事実は残す。
        /// </summary>
        private static OrderByDirection MapOrderByDirection(SortOrder sortOrder)
        {
            return sortOrder switch
            {
                SortOrder.Descending => OrderByDirection.Descending,
                SortOrder.Ascending => OrderByDirection.Ascending,
                SortOrder.NotSpecified => OrderByDirection.Ascending,
                _ => OrderByDirection.Unspecified
            };
        }

        /// <summary>
        /// 予期しない QueryExpression を見つけたときの退避用ノード。
        /// </summary>
        private SelectQueryAnalysis CreateFallbackSelect(string message)
        {
            _notices.Add(new AnalysisNotice(AnalysisNoticeLevel.Warning, message));

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
                []);
        }

        /// <summary>
        /// null 許容の断片を正規化して返す。
        /// </summary>
        private string? NormalizeOrNull(TSqlFragment? fragment)
        {
            if (fragment is null)
            {
                return null;
            }

            var text = _textExtractor.Normalize(fragment);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>
        /// ScriptDom 断片から相互ハイライト用の位置情報を作る。
        /// </summary>
        private static TextSpan? CreateTextSpan(TSqlFragment? fragment)
        {
            if (fragment is null || fragment.StartOffset < 0 || fragment.FragmentLength <= 0)
            {
                return null;
            }

            return new TextSpan(fragment.StartOffset, fragment.FragmentLength);
        }

        /// <summary>
        /// JOIN の ON 条件を AND 単位で分割し、見やすい一覧へ変換する。
        /// </summary>
        private IReadOnlyList<JoinConditionPartAnalysis> BuildJoinConditionParts(BooleanExpression? searchCondition)
        {
            if (searchCondition is null)
            {
                return [];
            }

            var parts = new List<BooleanExpression>();
            CollectJoinConditionParts(searchCondition, parts);

            return parts
                .Select((part, index) => new JoinConditionPartAnalysis(
                    index + 1,
                    _textExtractor.Normalize(part),
                    CreateTextSpan(part))
                {
                    ColumnReferences = CollectColumnReferences(part)
                })
                .ToArray();
        }

        /// <summary>
        /// ON 条件の AND 連結を平坦化する。
        /// OR を含む式は 1 まとまりとして残し、読み違えを避ける。
        /// </summary>
        private static void CollectJoinConditionParts(BooleanExpression expression, ICollection<BooleanExpression> parts)
        {
            var current = expression is BooleanParenthesisExpression parenthesisExpression
                ? parenthesisExpression.Expression
                : expression;

            if (current is BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } binaryExpression)
            {
                CollectJoinConditionParts(binaryExpression.FirstExpression, parts);
                CollectJoinConditionParts(binaryExpression.SecondExpression, parts);
                return;
            }

            parts.Add(expression);
        }

        /// <summary>
        /// FROM / JOIN 解析の戻り値。
        /// </summary>
        private sealed record TableReferenceAnalysisResult(
            SourceAnalysis? MainSource,
            IReadOnlyList<JoinAnalysis> Joins,
            IReadOnlyList<SubqueryAnalysis> Subqueries);

        /// <summary>
        /// 個別ソース解析の戻り値。
        /// </summary>
        private sealed record SourceAnalysisResult(
            SourceAnalysis Source,
            IReadOnlyList<SubqueryAnalysis> Subqueries);

        /// <summary>
        /// 別名やソース名からソース定義を引くための索引。
        /// 未修飾列では、単一ソース解決と明示列ベース解決もここで扱う。
        /// </summary>
        private sealed record SourceResolutionIndex(
            IReadOnlyDictionary<string, List<SourceAnalysis>> Entries,
            IReadOnlyDictionary<string, List<SourceAnalysis>> ExplicitColumnEntries,
            IReadOnlyList<SourceAnalysis> Sources)
        {
            public IReadOnlyList<SourceAnalysis> Resolve(string qualifier)
            {
                return Entries.TryGetValue(qualifier, out var sources)
                    ? sources
                    : [];
            }

            public IReadOnlyList<SourceAnalysis> ResolveUnqualified(string columnName)
            {
                if (Sources.Count == 1)
                {
                    return Sources;
                }

                if (Sources.Count == 0 || Sources.Any(source => source.ExposedColumnNames.Count == 0))
                {
                    return [];
                }

                return ExplicitColumnEntries.TryGetValue(columnName, out var sources)
                    ? sources
                    : [];
            }
        }

        /// <summary>
        /// SELECT 項目別名から元の SELECT 項目を引くための索引。
        /// ORDER BY 別名解決専用に絞ることで責務を小さく保つ。
        /// </summary>
        private sealed record SelectAliasResolutionIndex(
            IReadOnlyDictionary<string, List<SelectItemAnalysis>> Entries)
        {
            public IReadOnlyList<SelectItemAnalysis> Resolve(string alias)
            {
                return Entries.TryGetValue(alias, out var selectItems)
                    ? selectItems
                    : [];
            }
        }

        /// <summary>
        /// サブクエリの重複登録を防ぎつつ蓄積する。
        /// </summary>
        private sealed class SubqueryAccumulator
        {
            private readonly SqlTextExtractor _textExtractor;
            private readonly List<SubqueryAnalysis> _items = [];
            private readonly HashSet<string> _keys = [];

            public SubqueryAccumulator(SqlTextExtractor textExtractor)
            {
                _textExtractor = textExtractor;
            }

            public void Add(string location, TSqlFragment fragment, QueryExpressionAnalysis query)
            {
                var key = $"{location}:{fragment.StartOffset}:{fragment.FragmentLength}";
                if (!_keys.Add(key))
                {
                    return;
                }

                _items.Add(new SubqueryAnalysis(
                    location,
                    _textExtractor.CreatePreview(fragment),
                    query,
                    CreateTextSpan(fragment)));
            }

            public void AddRange(IEnumerable<SubqueryAnalysis> subqueries)
            {
                foreach (var subquery in subqueries)
                {
                    var key = $"{subquery.Location}:{subquery.DisplayText}";
                    if (_keys.Add(key))
                    {
                        _items.Add(subquery);
                    }
                }
            }

            public IReadOnlyList<SubqueryAnalysis> ToArray()
            {
                return _items.ToArray();
            }
        }

        /// <summary>
        /// WHERE / HAVING 条件の論理木と EXISTS / IN などを組み立てる。
        /// </summary>
        private sealed class ConditionCollector
        {
            private readonly AnalyzerCore _core;
            private readonly SqlTextExtractor _textExtractor;
            private readonly string _locationLabel;
            private readonly SubqueryAccumulator _subqueries;

            public ConditionCollector(
                AnalyzerCore core,
                SqlTextExtractor textExtractor,
                string locationLabel,
                SubqueryAccumulator subqueries)
            {
                _core = core;
                _textExtractor = textExtractor;
                _locationLabel = locationLabel;
                _subqueries = subqueries;
            }

            public List<ConditionMarker> Markers { get; } = [];

            /// <summary>
            /// BooleanExpression を再帰的な条件ノードへ変換する。
            /// </summary>
            public ConditionNodeAnalysis Build(BooleanExpression expression)
            {
                return BuildNode(expression);
            }

            private ConditionNodeAnalysis BuildNode(BooleanExpression expression)
            {
                return expression switch
                {
                    BooleanBinaryExpression binaryExpression => BuildBinaryNode(binaryExpression),
                    BooleanParenthesisExpression parenthesisExpression => MarkParenthesized(BuildNode(parenthesisExpression.Expression)),
                    BooleanNotExpression notExpression => BuildNotNode(notExpression),
                    ExistsPredicate existsPredicate => CreateExistsPredicateNode(ConditionMarkerType.Exists, existsPredicate, existsPredicate.Subquery),
                    InPredicate inPredicate when inPredicate.Subquery is not null => CreateInPredicateNode(inPredicate),
                    _ => CreatePlainPredicateNode(expression)
                };
            }

            private ConditionNodeAnalysis BuildBinaryNode(BooleanBinaryExpression node)
            {
                var nodeKind = node.BinaryExpressionType switch
                {
                    BooleanBinaryExpressionType.And => ConditionNodeKind.And,
                    BooleanBinaryExpressionType.Or => ConditionNodeKind.Or,
                    _ => ConditionNodeKind.Predicate
                };

                if (nodeKind == ConditionNodeKind.Predicate)
                {
                    return CreatePlainPredicateNode(node);
                }

                return new ConditionNodeAnalysis(
                    nodeKind,
                    _textExtractor.Normalize(node),
                    [BuildNode(node.FirstExpression), BuildNode(node.SecondExpression)],
                    ConditionPredicateKind.Unknown,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown,
                    false,
                    null,
                    CreateTextSpan(node));
            }

            private ConditionNodeAnalysis BuildNotNode(BooleanNotExpression node)
            {
                var innerExpression = UnwrapParentheses(node.Expression);
                if (innerExpression is ExistsPredicate existsPredicate)
                {
                    return CreateExistsPredicateNode(ConditionMarkerType.NotExists, node, existsPredicate.Subquery);
                }

                return new ConditionNodeAnalysis(
                    ConditionNodeKind.Not,
                    _textExtractor.Normalize(node),
                    [BuildNode(node.Expression)],
                    ConditionPredicateKind.Unknown,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown,
                    false,
                    null,
                    CreateTextSpan(node));
            }

            private ConditionNodeAnalysis CreateExistsPredicateNode(
                ConditionMarkerType markerType,
                TSqlFragment markerFragment,
                ScalarSubquery subquery)
            {
                var nestedQuery = _core.AnalyzeQueryExpression(subquery.QueryExpression);
                var marker = new ConditionMarker(
                    markerType,
                    _textExtractor.Normalize(markerFragment),
                    nestedQuery,
                    CreateTextSpan(markerFragment));
                Markers.Add(marker);

                _subqueries.Add(_locationLabel, subquery, nestedQuery);

                return new ConditionNodeAnalysis(
                    ConditionNodeKind.Predicate,
                    marker.DisplayText,
                    [],
                    ConditionPredicateKind.Exists,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown,
                    false,
                    marker,
                    CreateTextSpan(markerFragment))
                {
                    ColumnReferences = _core.CollectColumnReferences(markerFragment)
                };
            }

            private ConditionNodeAnalysis CreateInPredicateNode(InPredicate node)
            {
                var nestedQuery = _core.AnalyzeQueryExpression(node.Subquery!.QueryExpression);
                var marker = new ConditionMarker(
                    node.NotDefined ? ConditionMarkerType.NotIn : ConditionMarkerType.In,
                    _textExtractor.Normalize(node),
                    nestedQuery,
                    CreateTextSpan(node));
                Markers.Add(marker);

                _subqueries.Add(_locationLabel, node.Subquery, nestedQuery);

                return new ConditionNodeAnalysis(
                    ConditionNodeKind.Predicate,
                    marker.DisplayText,
                    [],
                    ConditionPredicateKind.In,
                    ConditionComparisonKind.Unknown,
                    ConditionNullCheckKind.Unknown,
                    ConditionBetweenKind.Unknown,
                    ConditionLikeKind.Unknown,
                    false,
                    marker,
                    CreateTextSpan(node))
                {
                    ColumnReferences = _core.CollectColumnReferences(node)
                };
            }

            private ConditionNodeAnalysis CreatePlainPredicateNode(BooleanExpression expression)
            {
                _core.CollectImmediateSubqueries(expression, _locationLabel, _subqueries);

                return new ConditionNodeAnalysis(
                    ConditionNodeKind.Predicate,
                    _textExtractor.Normalize(expression),
                    [],
                    ClassifyPredicateKind(expression),
                    ClassifyComparisonKind(expression),
                    ClassifyNullCheckKind(expression),
                    ClassifyBetweenKind(expression),
                    ClassifyLikeKind(expression),
                    false,
                    null,
                    CreateTextSpan(expression))
                {
                    ColumnReferences = _core.CollectColumnReferences(expression)
                };
            }

            private static ConditionNodeAnalysis MarkParenthesized(ConditionNodeAnalysis node)
            {
                return node.IsParenthesized
                    ? node
                    : node with { IsParenthesized = true };
            }

            private static ConditionPredicateKind ClassifyPredicateKind(BooleanExpression expression)
            {
                return expression switch
                {
                    BooleanComparisonExpression => ConditionPredicateKind.Comparison,
                    BooleanIsNullExpression => ConditionPredicateKind.NullCheck,
                    LikePredicate => ConditionPredicateKind.Like,
                    BooleanTernaryExpression ternaryExpression when ternaryExpression.TernaryExpressionType is BooleanTernaryExpressionType.Between or BooleanTernaryExpressionType.NotBetween => ConditionPredicateKind.Between,
                    ExistsPredicate => ConditionPredicateKind.Exists,
                    InPredicate => ConditionPredicateKind.In,
                    _ => ConditionPredicateKind.Unknown
                };
            }

            private static ConditionComparisonKind ClassifyComparisonKind(BooleanExpression expression)
            {
                if (expression is not BooleanComparisonExpression comparisonExpression)
                {
                    return ConditionComparisonKind.Unknown;
                }

                return comparisonExpression.ComparisonType switch
                {
                    BooleanComparisonType.Equals => ConditionComparisonKind.Equal,
                    BooleanComparisonType.NotEqualToBrackets => ConditionComparisonKind.NotEqual,
                    BooleanComparisonType.NotEqualToExclamation => ConditionComparisonKind.NotEqual,
                    BooleanComparisonType.GreaterThan => ConditionComparisonKind.GreaterThan,
                    BooleanComparisonType.LessThan => ConditionComparisonKind.LessThan,
                    BooleanComparisonType.GreaterThanOrEqualTo => ConditionComparisonKind.GreaterThanOrEqual,
                    BooleanComparisonType.LessThanOrEqualTo => ConditionComparisonKind.LessThanOrEqual,
                    BooleanComparisonType.NotLessThan => ConditionComparisonKind.NotLessThan,
                    BooleanComparisonType.NotGreaterThan => ConditionComparisonKind.NotGreaterThan,
                    BooleanComparisonType.IsDistinctFrom => ConditionComparisonKind.IsDistinctFrom,
                    BooleanComparisonType.IsNotDistinctFrom => ConditionComparisonKind.IsNotDistinctFrom,
                    _ => ConditionComparisonKind.Unknown
                };
            }

            private static ConditionNullCheckKind ClassifyNullCheckKind(BooleanExpression expression)
            {
                if (expression is not BooleanIsNullExpression isNullExpression)
                {
                    return ConditionNullCheckKind.Unknown;
                }

                return isNullExpression.IsNot
                    ? ConditionNullCheckKind.IsNotNull
                    : ConditionNullCheckKind.IsNull;
            }

            private static ConditionBetweenKind ClassifyBetweenKind(BooleanExpression expression)
            {
                if (expression is not BooleanTernaryExpression ternaryExpression)
                {
                    return ConditionBetweenKind.Unknown;
                }

                return ternaryExpression.TernaryExpressionType switch
                {
                    BooleanTernaryExpressionType.Between => ConditionBetweenKind.Between,
                    BooleanTernaryExpressionType.NotBetween => ConditionBetweenKind.NotBetween,
                    _ => ConditionBetweenKind.Unknown
                };
            }

            private static ConditionLikeKind ClassifyLikeKind(BooleanExpression expression)
            {
                if (expression is not LikePredicate likePredicate)
                {
                    return ConditionLikeKind.Unknown;
                }

                return likePredicate.NotDefined
                    ? ConditionLikeKind.NotLike
                    : ConditionLikeKind.Like;
            }

            private static BooleanExpression UnwrapParentheses(BooleanExpression expression)
            {
                var current = expression;
                while (current is BooleanParenthesisExpression parenthesisExpression)
                {
                    current = parenthesisExpression.Expression;
                }

                return current;
            }
        }

        /// <summary>
        /// 式ツリーから最初の集計関数を検出する visitor。
        /// </summary>
        private sealed class AggregateFunctionVisitor : TSqlFragmentVisitor
        {
            public string? FirstAggregateFunctionName { get; private set; }

            public override void ExplicitVisit(FunctionCall node)
            {
                if (FirstAggregateFunctionName is not null)
                {
                    return;
                }

                var functionName = node.FunctionName?.Value;
                if (!string.IsNullOrWhiteSpace(functionName) && AggregateFunctionNames.Contains(functionName))
                {
                    FirstAggregateFunctionName = functionName.ToUpperInvariant();
                    return;
                }

                base.ExplicitVisit(node);
            }
        }

        /// <summary>
        /// 式から列参照だけを取り出す visitor。
        /// サブクエリ内部は別構造で解析するため、ここでは潜らない。
        /// </summary>
        private sealed class ColumnReferenceCollector : TSqlFragmentVisitor
        {
            private readonly SqlTextExtractor _textExtractor;
            private readonly List<ColumnReferenceAnalysis> _items = [];
            private readonly HashSet<string> _keys = [];

            public ColumnReferenceCollector(SqlTextExtractor textExtractor)
            {
                _textExtractor = textExtractor;
            }

            public IReadOnlyList<ColumnReferenceAnalysis> Items => _items;

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                var displayText = _textExtractor.Normalize(node);
                if (string.IsNullOrWhiteSpace(displayText))
                {
                    return;
                }

                var key = $"{node.StartOffset}:{node.FragmentLength}:{displayText}";
                if (!_keys.Add(key))
                {
                    return;
                }

                var identifiers = node.MultiPartIdentifier?.Identifiers
                    .Select(identifier => identifier.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray() ?? [];

                var columnName = identifiers.Length > 0
                    ? identifiers[^1]
                    : displayText;
                var qualifier = identifiers.Length > 1
                    ? string.Join(".", identifiers.Take(identifiers.Length - 1))
                    : null;

                _items.Add(new ColumnReferenceAnalysis(
                    _items.Count + 1,
                    displayText,
                    qualifier,
                    columnName,
                    CreateTextSpan(node)));
            }

            public override void ExplicitVisit(ScalarSubquery node)
            {
            }

            public override void ExplicitVisit(QueryDerivedTable node)
            {
            }
        }

        /// <summary>
        /// 即時のサブクエリだけを集める visitor。
        /// </summary>
        private sealed class ImmediateSubqueryCollector : TSqlFragmentVisitor
        {
            private readonly AnalyzerCore _core;
            private readonly SqlTextExtractor _textExtractor;
            private readonly string _locationLabel;
            private readonly SubqueryAccumulator _subqueries;

            public ImmediateSubqueryCollector(
                AnalyzerCore core,
                SqlTextExtractor textExtractor,
                string locationLabel,
                SubqueryAccumulator subqueries)
            {
                _core = core;
                _textExtractor = textExtractor;
                _locationLabel = locationLabel;
                _subqueries = subqueries;
            }

            public override void ExplicitVisit(ScalarSubquery node)
            {
                var nestedQuery = _core.AnalyzeQueryExpression(node.QueryExpression);
                _subqueries.Add(_locationLabel, node, nestedQuery);
            }

            public override void ExplicitVisit(QueryDerivedTable node)
            {
                var nestedQuery = _core.AnalyzeQueryExpression(node.QueryExpression);
                _subqueries.Add(_locationLabel, node.QueryExpression, nestedQuery);
            }
        }
    }

    /// <summary>
    /// ScriptDom 断片から SQL 原文を安全に切り出す補助。
    /// 行末や改行が多い SQL でも、表示用に整えた文字列を一元的に作れる。
    /// </summary>
    private sealed class SqlTextExtractor
    {
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private readonly string _sql;

        public SqlTextExtractor(string sql)
        {
            _sql = sql;
        }

        /// <summary>
        /// 断片の原文を切り出して返す。
        /// </summary>
        public string Extract(TSqlFragment fragment)
        {
            if (fragment.StartOffset < 0 || fragment.FragmentLength <= 0)
            {
                return string.Empty;
            }

            if (fragment.StartOffset + fragment.FragmentLength > _sql.Length)
            {
                return string.Empty;
            }

            return _sql.Substring(fragment.StartOffset, fragment.FragmentLength);
        }

        /// <summary>
        /// 改行や余分な空白を潰した表示文字列を返す。
        /// </summary>
        public string Normalize(TSqlFragment fragment)
        {
            return Normalize(Extract(fragment));
        }

        /// <summary>
        /// 任意文字列を1行プレビューへ整形する。
        /// </summary>
        public string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return WhitespaceRegex.Replace(text, " ").Trim();
        }

        /// <summary>
        /// TreeView 向けの短いプレビュー文字列を返す。
        /// </summary>
        public string CreatePreview(TSqlFragment fragment, int maxLength = 100)
        {
            var normalized = Normalize(fragment);
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return $"{normalized[..(maxLength - 3)]}...";
        }
    }
}
