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
                null,
                [],
                notices);
        }

        if (statement is not SelectStatement selectStatement || selectStatement.QueryExpression is null)
        {
            notices.Add(new AnalysisNotice(AnalysisNoticeLevel.Warning, "初期版では SELECT 系以外の文は未対応です。"));

            return new QueryAnalysisResult(
                QueryStatementCategory.Unsupported,
                null,
                [],
                notices);
        }

        if (selectStatement.WithCtesAndXmlNamespaces?.CommonTableExpressions.Count > 0)
        {
            notices.Add(new AnalysisNotice(
                AnalysisNoticeLevel.Information,
                "WITH句(CTE) が含まれています。初期版ではクエリ本体の構造表示を優先し、CTE定義の詳細表示は今後の拡張対象です。"));
        }

        var core = new AnalyzerCore(sql, notices);
        var query = core.AnalyzeQueryExpression(selectStatement.QueryExpression);
        var category = query.Kind == QueryExpressionKind.SetOperation
            ? QueryStatementCategory.SetOperation
            : QueryStatementCategory.Select;

        return new QueryAnalysisResult(category, query, [], notices);
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
                    "複数の文が入力されています。初期版では先頭の文を解析対象にします。"));
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
        private readonly SqlTextExtractor _textExtractor;
        private readonly ICollection<AnalysisNotice> _notices;

        public AnalyzerCore(string sql, ICollection<AnalysisNotice> notices)
        {
            _textExtractor = new SqlTextExtractor(sql);
            _notices = notices;
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
        /// 単一の SELECT 文を解析する。
        /// </summary>
        private SelectQueryAnalysis AnalyzeSelect(QuerySpecification querySpecification)
        {
            var selectItems = querySpecification.SelectElements
                .Select((element, index) => new SelectItemAnalysis(index + 1, _textExtractor.Normalize(element)))
                .ToArray();

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
                        "FROM句に複数のソースが含まれています。初期版では先頭ソースを基準に解析し、残りは今後の拡張対象です。"));
                }
            }

            ConditionAnalysis? whereCondition = null;
            if (querySpecification.WhereClause?.SearchCondition is { } searchCondition)
            {
                whereCondition = AnalyzeCondition(searchCondition, "WHERE句", subqueries);
            }

            GroupByAnalysis? groupBy = null;
            if (querySpecification.GroupByClause is { GroupingSpecifications.Count: > 0 } groupByClause)
            {
                var items = groupByClause.GroupingSpecifications
                    .Select(grouping => _textExtractor.Normalize(grouping))
                    .ToArray();

                groupBy = new GroupByAnalysis(items, _textExtractor.Normalize(groupByClause));
                CollectImmediateSubqueries(groupByClause, "GROUP BY句", subqueries);
            }

            string? havingText = null;
            if (querySpecification.HavingClause?.SearchCondition is { } havingCondition)
            {
                havingText = _textExtractor.Normalize(havingCondition);
                CollectImmediateSubqueries(havingCondition, "HAVING句", subqueries);
            }

            OrderByAnalysis? orderBy = null;
            if (querySpecification.OrderByClause is { OrderByElements.Count: > 0 } orderByClause)
            {
                var items = orderByClause.OrderByElements
                    .Select(orderByElement => _textExtractor.Normalize(orderByElement))
                    .ToArray();

                orderBy = new OrderByAnalysis(items, _textExtractor.Normalize(orderByClause));
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
                mainSource,
                joins,
                whereCondition,
                groupBy,
                havingText,
                orderBy,
                subqueries.ToArray());
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
                AnalyzeQueryExpression(binaryQueryExpression.SecondQueryExpression));
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
                    joins.Add(new JoinAnalysis(
                        joins.Count + 1,
                        MapJoinType(qualifiedJoin.QualifiedJoinType),
                        FormatJoinType(qualifiedJoin.QualifiedJoinType),
                        right.Source,
                        NormalizeOrNull(qualifiedJoin.SearchCondition)));

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
                        null));

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
                var source = new SourceAnalysis(_textExtractor.Normalize(tableReference), nestedQuery);
                var subquery = new SubqueryAnalysis(
                    locationLabel,
                    _textExtractor.CreatePreview(derivedTable.QueryExpression),
                    nestedQuery);

                return new SourceAnalysisResult(source, [subquery]);
            }

            return new SourceAnalysisResult(
                new SourceAnalysis(_textExtractor.Normalize(tableReference), null),
                []);
        }

        /// <summary>
        /// WHERE / HAVING 条件内の注目ポイントを抽出する。
        /// </summary>
        private ConditionAnalysis AnalyzeCondition(BooleanExpression condition, string locationLabel, SubqueryAccumulator accumulator)
        {
            var collector = new ConditionCollector(this, _textExtractor, locationLabel, accumulator);
            condition.Accept(collector);

            return new ConditionAnalysis(
                _textExtractor.Normalize(condition),
                collector.Markers.ToArray());
        }

        /// <summary>
        /// SELECT 項目などに含まれる一般的なサブクエリを拾う。
        /// 即時の子サブクエリだけを集め、孫以下はそのサブクエリ自身に任せる。
        /// </summary>
        private void CollectImmediateSubqueries(TSqlFragment fragment, string locationLabel, SubqueryAccumulator accumulator)
        {
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
                    query));
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
        /// WHERE 条件の EXISTS / IN などを収集する visitor。
        /// </summary>
        private sealed class ConditionCollector : TSqlFragmentVisitor
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

            public override void ExplicitVisit(BooleanNotExpression node)
            {
                if (node.Expression is ExistsPredicate existsPredicate)
                {
                    AddExistsMarker(ConditionMarkerType.NotExists, node, existsPredicate.Subquery);
                    return;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(ExistsPredicate node)
            {
                AddExistsMarker(ConditionMarkerType.Exists, node, node.Subquery);
            }

            public override void ExplicitVisit(InPredicate node)
            {
                if (node.Subquery is not null)
                {
                    var nestedQuery = _core.AnalyzeQueryExpression(node.Subquery.QueryExpression);
                    var markerType = node.NotDefined ? ConditionMarkerType.NotIn : ConditionMarkerType.In;
                    Markers.Add(new ConditionMarker(
                        markerType,
                        _textExtractor.Normalize(node),
                        nestedQuery));

                    _subqueries.Add(_locationLabel, node.Subquery, nestedQuery);
                    return;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(ScalarSubquery node)
            {
                var nestedQuery = _core.AnalyzeQueryExpression(node.QueryExpression);
                _subqueries.Add(_locationLabel, node, nestedQuery);
            }

            private void AddExistsMarker(ConditionMarkerType markerType, TSqlFragment markerFragment, ScalarSubquery subquery)
            {
                var nestedQuery = _core.AnalyzeQueryExpression(subquery.QueryExpression);
                Markers.Add(new ConditionMarker(
                    markerType,
                    _textExtractor.Normalize(markerFragment),
                    nestedQuery));

                _subqueries.Add(_locationLabel, subquery, nestedQuery);
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
