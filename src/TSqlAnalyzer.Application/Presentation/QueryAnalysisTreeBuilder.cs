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
            children.Add(BuildQueryNode("主構造", result.Query));
            children.Add(BuildSetOperationNode(result.Query));
            children.Add(BuildSubqueryNode(result.Query));
        }
        else
        {
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
        var children = new List<DisplayTreeNode>();

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
    /// SELECT / 集合演算に応じて主構造ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildQueryNode(string title, QueryExpressionAnalysis query)
    {
        return query switch
        {
            SelectQueryAnalysis selectQuery => BuildSelectNode(title, selectQuery),
            SetOperationQueryAnalysis setOperationQuery => Node(
                title,
                BuildQueryNode("左クエリ", setOperationQuery.LeftQuery),
                BuildQueryNode("右クエリ", setOperationQuery.RightQuery)),
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

        return Node("主テーブル", Node(query.MainSource.DisplayText));
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
            query.Joins
                .Select(join => Node(
                    $"JOIN #{join.Sequence}",
                    Node($"種別: {join.JoinTypeText}"),
                    Node($"結合先: {join.TargetSource.DisplayText}"),
                    Node($"ON条件: {join.OnConditionText ?? "なし"}")))
                .ToArray());
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
            Node($"条件式: {query.WhereCondition.DisplayText}")
        };

        if (query.WhereCondition.Markers.Count == 0)
        {
            children.Add(Node("条件種別", Node("特記事項なし")));
        }
        else
        {
            children.Add(Node(
                "条件種別",
                query.WhereCondition.Markers
                    .Select(marker => Node($"{BuildMarkerText(marker.MarkerType)}: {marker.DisplayText}"))
                    .ToArray()));
        }

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

        if (!string.IsNullOrWhiteSpace(query.HavingText))
        {
            children.Add(Node($"HAVING: {query.HavingText}"));
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

        return Node(
            "集合演算",
            Node($"種別: {BuildSetOperationText(setOperationQuery.OperationType)}"),
            BuildQueryNode("左クエリ", setOperationQuery.LeftQuery),
            BuildQueryNode("右クエリ", setOperationQuery.RightQuery));
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
