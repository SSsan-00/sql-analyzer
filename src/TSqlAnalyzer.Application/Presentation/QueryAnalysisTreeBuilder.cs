using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Presentation;

/// <summary>
/// 解析結果を TreeView 向けの表示モデルへ変換する。
/// 実際の WinForms TreeNode とは切り離し、UI 非依存な木構造へ整形する。
/// </summary>
public sealed class QueryAnalysisTreeBuilder
{
    /// <summary>
    /// 解析結果を表示木へ変換する。
    /// </summary>
    public DisplayTreeNode Build(QueryAnalysisResult result)
    {
        var children = new List<DisplayTreeNode>
        {
            Node($"文種別: {BuildStatementCategoryText(result)}"),
            BuildOverviewNode(result),
        };

        if (result.Query is not null)
        {
            children.Add(BuildCommonTableExpressionsNode(result));
            children.Add(BuildQueryNode("主構造", result.Query));
            children.Add(BuildSetOperationNode(result.Query));
            children.Add(BuildSubqueryNode(result.Query));
        }
        else
        {
            children.Add(Node("共通テーブル式", Node("なし")));
            children.Add(Node("主構造", Node("解析対象なし")));
            children.Add(Node("集合演算", Node("なし")));
            children.Add(Node("サブクエリ", Node("なし")));
        }

        children.Add(BuildNoticeNode(result));

        return new DisplayTreeNode("クエリ解析結果", children);
    }

    /// <summary>
    /// 文種別の表示文字列を作る。
    /// </summary>
    private static string BuildStatementCategoryText(QueryAnalysisResult result)
    {
        return result.StatementCategory switch
        {
            QueryStatementCategory.Empty => "未入力",
            QueryStatementCategory.Select => "SELECT",
            QueryStatementCategory.SetOperation => "集合演算",
            QueryStatementCategory.Unsupported => "未対応",
            QueryStatementCategory.ParseError => "構文エラー",
            _ => result.StatementCategory.ToString()
        };
    }

    /// <summary>
    /// 概要ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildOverviewNode(QueryAnalysisResult result)
    {
        var children = new List<DisplayTreeNode>
        {
            Node($"CTE数: {result.CommonTableExpressions.Count}")
        };

        if (result.Query is SelectQueryAnalysis selectQuery)
        {
            children.Add(Node(selectQuery.IsDistinct ? "DISTINCT: あり" : "DISTINCT: なし"));
            children.Add(Node($"TOP: {selectQuery.TopExpressionText ?? "なし"}"));
            children.Add(Node($"取得項目数: {selectQuery.SelectItems.Count}"));
            children.Add(Node($"JOIN数: {selectQuery.Joins.Count}"));
            children.Add(Node($"サブクエリ数: {selectQuery.Subqueries.Count}"));
        }
        else if (result.Query is SetOperationQueryAnalysis setOperationQuery)
        {
            children.Add(Node($"集合演算種別: {BuildSetOperationText(setOperationQuery.OperationType)}"));
            children.Add(Node("左右のクエリ構造を個別に確認できます。"));
        }
        else
        {
            children.Add(Node(BuildFallbackOverview(result)));
        }

        return Node("概要", children.ToArray());
    }

    /// <summary>
    /// CTE ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildCommonTableExpressionsNode(QueryAnalysisResult result)
    {
        if (result.CommonTableExpressions.Count == 0)
        {
            return Node("共通テーブル式", Node("なし"));
        }

        return Node(
            "共通テーブル式",
            result.CommonTableExpressions
                .Select((cte, index) => Node(
                    $"CTE #{index + 1}: {cte.Name}",
                    BuildCteColumnsNode(cte),
                    BuildQueryNode("内部構造", cte.Query)))
                .Concat([BuildCteReferenceSummaryNode(result)])
                .ToArray());
    }

    /// <summary>
    /// SELECT / 集合演算に応じて主構造ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildQueryNode(string title, QueryExpressionAnalysis query)
    {
        return query switch
        {
            SelectQueryAnalysis selectQuery => BuildSelectNode(title, selectQuery),
            SetOperationQueryAnalysis setOperationQuery => BuildSetOperationStructureNode(title, setOperationQuery),
            _ => Node(title, Node("未対応のクエリ構造です。"))
        };
    }

    /// <summary>
    /// SELECT 文の主構造ノードを組み立てる。
    /// </summary>
    private static DisplayTreeNode BuildSelectNode(string title, SelectQueryAnalysis query)
    {
        var children = new List<DisplayTreeNode>
        {
            BuildSelectItemsNode(query),
            BuildMainSourceNode(query),
            BuildJoinNode(query),
            BuildWhereNode(query),
            BuildAggregationNode(query),
            BuildOrderByNode(query)
        };

        return Node(title, children.ToArray());
    }

    /// <summary>
    /// 取得項目ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildSelectItemsNode(SelectQueryAnalysis query)
    {
        if (query.SelectItems.Count == 0)
        {
            return Node("取得項目", Node("なし"));
        }

        return Node(
            "取得項目",
            query.SelectItems
                .Select(item => Node($"項目 #{item.Sequence}: {item.DisplayText}"))
                .ToArray());
    }

    /// <summary>
    /// 主テーブルノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildMainSourceNode(SelectQueryAnalysis query)
    {
        if (query.MainSource is null)
        {
            return Node("主テーブル", Node("なし"));
        }

        if (query.MainSource.NestedQuery is null)
        {
            return Node(
                "主テーブル",
                Node(query.MainSource.DisplayText),
                BuildSourceKindNode(query.MainSource));
        }

        return Node(
            "主テーブル",
            Node(query.MainSource.DisplayText),
            BuildSourceKindNode(query.MainSource),
            BuildQueryNode("主テーブルの内部構造", query.MainSource.NestedQuery));
    }

    /// <summary>
    /// JOIN ノードを仕様に合わせて構築する。
    /// </summary>
    private static DisplayTreeNode BuildJoinNode(SelectQueryAnalysis query)
    {
        if (query.Joins.Count == 0)
        {
            return Node("結合", Node("なし"));
        }

        return Node(
            "結合",
            query.Joins.Select(BuildJoinDetailNode).ToArray());
    }

    /// <summary>
    /// WHERE ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildWhereNode(SelectQueryAnalysis query)
    {
        if (query.WhereCondition is null)
        {
            return Node("抽出条件", Node("なし"));
        }

        var children = new List<DisplayTreeNode>
        {
            Node($"条件式: {query.WhereCondition.DisplayText}"),
            BuildConditionLogicNode(query.WhereCondition),
            BuildConditionMarkersNode(query.WhereCondition)
        };

        return Node("抽出条件", children.ToArray());
    }

    /// <summary>
    /// GROUP BY / HAVING ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildAggregationNode(SelectQueryAnalysis query)
    {
        var children = new List<DisplayTreeNode>();

        if (query.GroupBy is not null)
        {
            children.Add(Node("GROUP BY", query.GroupBy.Items.Select(item => Node(item)).ToArray()));
        }

        if (query.HavingCondition is not null)
        {
            children.Add(Node(
                "HAVING",
                Node($"条件式: {query.HavingCondition.DisplayText}"),
                BuildConditionLogicNode(query.HavingCondition),
                BuildConditionMarkersNode(query.HavingCondition)));
        }

        if (children.Count == 0)
        {
            children.Add(Node("なし"));
        }

        return Node("集計", children.ToArray());
    }

    /// <summary>
    /// ORDER BY ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildOrderByNode(SelectQueryAnalysis query)
    {
        if (query.OrderBy is null || query.OrderBy.Items.Count == 0)
        {
            return Node("並び順", Node("なし"));
        }

        return Node(
            "並び順",
            query.OrderBy.Items
                .Select((item, index) => Node($"項目 #{index + 1}: {item}"))
                .ToArray());
    }

    /// <summary>
    /// 集合演算ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildSetOperationNode(QueryExpressionAnalysis query)
    {
        if (query is not SetOperationQueryAnalysis setOperationQuery)
        {
            return Node("集合演算", Node("なし"));
        }

        return BuildSetOperationStructureNode("集合演算", setOperationQuery);
    }

    /// <summary>
    /// サブクエリノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildSubqueryNode(QueryExpressionAnalysis query)
    {
        if (query is not SelectQueryAnalysis selectQuery || selectQuery.Subqueries.Count == 0)
        {
            return Node("サブクエリ", Node("なし"));
        }

        return Node(
            "サブクエリ",
            selectQuery.Subqueries
                .Select((subquery, index) => Node(
                    $"サブクエリ #{index + 1}",
                    Node($"場所: {subquery.Location}"),
                    Node($"概要: {subquery.DisplayText}"),
                    BuildQueryNode("内部構造", subquery.Query)))
                .ToArray());
    }

    /// <summary>
    /// CTE の列一覧ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildCteColumnsNode(CommonTableExpressionAnalysis cte)
    {
        if (cte.ColumnNames.Count == 0)
        {
            return Node("列定義", Node("省略"));
        }

        return Node("列定義", cte.ColumnNames.Select(columnName => Node(columnName)).ToArray());
    }

    /// <summary>
    /// CTE 参照関係の要約ノードを作る。
    /// メインクエリと各 CTE が、どの CTE 名を参照しているかを一覧化する。
    /// </summary>
    private static DisplayTreeNode BuildCteReferenceSummaryNode(QueryAnalysisResult result)
    {
        var children = new List<DisplayTreeNode>
        {
            Node($"メインクエリ: {BuildReferencedCteNamesText(result.Query)}")
        };

        children.AddRange(result.CommonTableExpressions.Select(cte =>
            Node($"CTE {cte.Name}: {BuildReferencedCteNamesText(cte.Query)}")));

        return Node("参照関係", children.ToArray());
    }

    /// <summary>
    /// ソースが内部クエリを持つ場合だけ補助ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildNestedSourceNode(string title, SourceAnalysis source)
    {
        if (source.NestedQuery is null)
        {
            return Node(title);
        }

        return BuildQueryNode(title, source.NestedQuery);
    }

    /// <summary>
    /// ソース種別ノードを作る。
    /// CTE 参照や派生テーブルを明示して、主テーブルや JOIN 先の意味を追いやすくする。
    /// </summary>
    private static DisplayTreeNode BuildSourceKindNode(SourceAnalysis source)
    {
        var text = source.SourceKind switch
        {
            SourceKind.CommonTableExpressionReference => "CTE参照",
            SourceKind.DerivedTable => "派生テーブル",
            SourceKind.Object => "通常ソース",
            _ => "不明"
        };

        if (source.SourceKind == SourceKind.CommonTableExpressionReference && !string.IsNullOrWhiteSpace(source.SourceName))
        {
            return Node($"種別: {text}", Node($"名前: {source.SourceName}"));
        }

        return Node($"種別: {text}");
    }

    /// <summary>
    /// JOIN 詳細ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildJoinDetailNode(JoinAnalysis join)
    {
        var children = new List<DisplayTreeNode>
        {
            Node($"種別: {join.JoinTypeText}"),
            Node($"結合先: {join.TargetSource.DisplayText}"),
            BuildSourceKindNode(join.TargetSource),
            Node($"ON条件: {join.OnConditionText ?? "なし"}")
        };

        if (join.TargetSource.NestedQuery is not null)
        {
            children.Add(BuildNestedSourceNode("結合先の内部構造", join.TargetSource));
        }

        return Node($"JOIN #{join.Sequence}", children.ToArray());
    }

    /// <summary>
    /// 条件マーカー一覧ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildConditionMarkersNode(ConditionAnalysis condition)
    {
        if (condition.Markers.Count == 0)
        {
            return Node("条件種別", Node("特記事項なし"));
        }

        return Node(
            "条件種別",
            condition.Markers
                .Select((marker, index) => BuildConditionMarkerNode(marker, index + 1))
                .ToArray());
    }

    /// <summary>
    /// 条件式の論理木ノードを作る。
    /// 複雑な AND / OR / NOT を上から辿れるようにする。
    /// </summary>
    private static DisplayTreeNode BuildConditionLogicNode(ConditionAnalysis condition)
    {
        return Node("条件論理", BuildConditionLogicTreeNode(condition.RootNode));
    }

    /// <summary>
    /// 条件論理木 1 ノード分を再帰的に変換する。
    /// </summary>
    private static DisplayTreeNode BuildConditionLogicTreeNode(ConditionNodeAnalysis node)
    {
        if (node.NodeKind == ConditionNodeKind.Predicate)
        {
            var children = new List<DisplayTreeNode>
            {
                Node($"式: {node.DisplayText}")
            };

            if (node.Marker is not null)
            {
                children.Add(Node($"種別: {BuildMarkerText(node.Marker.MarkerType)}"));

                if (node.Marker.NestedQuery is not null)
                {
                    children.Add(BuildQueryNode("内部クエリ", node.Marker.NestedQuery));
                }
            }

            return Node(BuildConditionNodeTitle(node), children.ToArray());
        }

        return Node(
            BuildConditionNodeTitle(node),
            node.Children.Select(BuildConditionLogicTreeNode).ToArray());
    }

    /// <summary>
    /// 条件マーカー 1 件分のノードを作る。
    /// EXISTS / IN の内部クエリがある場合は、その構造も辿れるようにする。
    /// </summary>
    private static DisplayTreeNode BuildConditionMarkerNode(ConditionMarker marker, int sequence)
    {
        var children = new List<DisplayTreeNode>
        {
            Node($"種別: {BuildMarkerText(marker.MarkerType)}"),
            Node($"条件式: {marker.DisplayText}")
        };

        if (marker.NestedQuery is not null)
        {
            children.Add(BuildQueryNode("内部クエリ", marker.NestedQuery));
        }

        return Node($"条件 #{sequence}", children.ToArray());
    }

    /// <summary>
    /// 条件論理木ノードの見出しを返す。
    /// </summary>
    private static string BuildConditionNodeTitle(ConditionNodeAnalysis node)
    {
        return node.NodeKind switch
        {
            ConditionNodeKind.And => "AND",
            ConditionNodeKind.Or => "OR",
            ConditionNodeKind.Not => "NOT",
            ConditionNodeKind.Predicate when node.Marker is not null => BuildMarkerText(node.Marker.MarkerType),
            _ => "条件"
        };
    }

    /// <summary>
    /// クエリが参照している CTE 名の一覧を文字列化する。
    /// </summary>
    private static string BuildReferencedCteNamesText(QueryExpressionAnalysis? query)
    {
        if (query is null)
        {
            return "なし";
        }

        var names = CollectReferencedCteNames(query)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names.Length == 0 ? "なし" : string.Join(", ", names);
    }

    /// <summary>
    /// クエリ以下から参照されている CTE 名を再帰的に集める。
    /// </summary>
    private static HashSet<string> CollectReferencedCteNames(QueryExpressionAnalysis query)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectReferencedCteNames(query, names);
        return names;
    }

    /// <summary>
    /// クエリ以下から参照されている CTE 名を蓄積する。
    /// </summary>
    private static void CollectReferencedCteNames(QueryExpressionAnalysis query, ISet<string> names)
    {
        switch (query)
        {
            case SelectQueryAnalysis selectQuery:
                AddSourceReference(selectQuery.MainSource, names);

                foreach (var join in selectQuery.Joins)
                {
                    AddSourceReference(join.TargetSource, names);
                }

                foreach (var subquery in selectQuery.Subqueries)
                {
                    CollectReferencedCteNames(subquery.Query, names);
                }

                break;

            case SetOperationQueryAnalysis setOperationQuery:
                CollectReferencedCteNames(setOperationQuery.LeftQuery, names);
                CollectReferencedCteNames(setOperationQuery.RightQuery, names);
                break;
        }
    }

    /// <summary>
    /// ソースから CTE 参照名を拾う。
    /// </summary>
    private static void AddSourceReference(SourceAnalysis? source, ISet<string> names)
    {
        if (source is null)
        {
            return;
        }

        if (source.SourceKind == SourceKind.CommonTableExpressionReference && !string.IsNullOrWhiteSpace(source.SourceName))
        {
            names.Add(source.SourceName);
        }

        if (source.NestedQuery is not null)
        {
            CollectReferencedCteNames(source.NestedQuery, names);
        }
    }

    /// <summary>
    /// 集合演算ノードを再帰的に構築する。
    /// 入れ子の集合演算でも種別が失われないよう、左クエリ・右クエリの上に種別を置く。
    /// </summary>
    private static DisplayTreeNode BuildSetOperationStructureNode(string title, SetOperationQueryAnalysis setOperationQuery)
    {
        return Node(
            title,
            Node($"種別: {BuildSetOperationText(setOperationQuery.OperationType)}"),
            BuildQueryNode("左クエリ", setOperationQuery.LeftQuery),
            BuildQueryNode("右クエリ", setOperationQuery.RightQuery));
    }

    /// <summary>
    /// 注意点ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildNoticeNode(QueryAnalysisResult result)
    {
        var children = new List<DisplayTreeNode>();

        children.AddRange(result.ParseIssues.Select(issue =>
            Node($"構文エラー ({issue.Line}, {issue.Column}): {issue.Message}")));

        children.AddRange(result.Notices.Select(notice =>
            Node($"{BuildNoticeLevelText(notice.Level)}: {notice.Message}")));

        if (children.Count == 0)
        {
            children.Add(Node("なし"));
        }

        return Node("注意点", children.ToArray());
    }

    /// <summary>
    /// 空入力や未対応時の概要を作る。
    /// </summary>
    private static string BuildFallbackOverview(QueryAnalysisResult result)
    {
        return result.StatementCategory switch
        {
            QueryStatementCategory.Empty => "SQL の入力待ちです。",
            QueryStatementCategory.Unsupported => "初期版では未対応の文種別です。",
            QueryStatementCategory.ParseError => "構文エラーのため構造を展開できません。",
            _ => "解析結果はありません。"
        };
    }

    /// <summary>
    /// 条件マーカーの表示名。
    /// </summary>
    private static string BuildMarkerText(ConditionMarkerType markerType)
    {
        return markerType switch
        {
            ConditionMarkerType.Exists => "EXISTS",
            ConditionMarkerType.NotExists => "NOT EXISTS",
            ConditionMarkerType.In => "IN",
            ConditionMarkerType.NotIn => "NOT IN",
            _ => markerType.ToString()
        };
    }

    /// <summary>
    /// 集合演算 enum を表示文字列へ変換する。
    /// </summary>
    private static string BuildSetOperationText(SetOperationType operationType)
    {
        return operationType switch
        {
            SetOperationType.Union => "UNION",
            SetOperationType.UnionAll => "UNION ALL",
            SetOperationType.Except => "EXCEPT",
            SetOperationType.Intersect => "INTERSECT",
            _ => operationType.ToString()
        };
    }

    /// <summary>
    /// 注意レベルの表示名。
    /// </summary>
    private static string BuildNoticeLevelText(AnalysisNoticeLevel level)
    {
        return level switch
        {
            AnalysisNoticeLevel.Information => "補足",
            AnalysisNoticeLevel.Warning => "警告",
            AnalysisNoticeLevel.Error => "エラー",
            _ => level.ToString()
        };
    }

    /// <summary>
    /// 子ノード付きノードを簡潔に作るヘルパー。
    /// </summary>
    private static DisplayTreeNode Node(string text, params DisplayTreeNode[] children)
    {
        return new DisplayTreeNode(text, children);
    }
}
