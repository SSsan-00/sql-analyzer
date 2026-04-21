using TSqlAnalyzer.Application.Analysis;
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
            Node($"文: {BuildStatementCategoryText(result)}"),
            BuildOverviewNode(result),
        };

        if (result.Query is not null || result.DataModification is not null || result.CreateStatement is not null)
        {
            children.Add(BuildCommonTableExpressionsNode(result));
            children.Add(BuildMainStructureNode(result));
            children.Add(result.Query is not null ? BuildSetOperationNode(result.Query) : Node(DisplayTreeNodeKind.SetOperation, "集合演算", Node("なし")));
            children.Add(BuildSubqueryNode(result));
        }
        else
        {
            children.Add(Node(DisplayTreeNodeKind.CommonTableExpression, "共通テーブル式", Node("なし")));
            children.Add(Node(DisplayTreeNodeKind.Section, "主構造", Node("解析対象なし")));
            children.Add(Node(DisplayTreeNodeKind.SetOperation, "集合演算", Node("なし")));
            children.Add(Node(DisplayTreeNodeKind.Subquery, "サブクエリ", Node("なし")));
        }

        return new DisplayTreeNode("クエリ解析結果", children, Kind: DisplayTreeNodeKind.Root);
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
            QueryStatementCategory.Create => "CREATE",
            QueryStatementCategory.Update => "UPDATE",
            QueryStatementCategory.Insert => "INSERT",
            QueryStatementCategory.Delete => "DELETE",
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
            children.Add(Node($"INTO: {selectQuery.IntoTarget?.DisplayText ?? "なし"}"));
            children.Add(Node($"取得項目数: {selectQuery.SelectItems.Count}"));
            children.Add(Node($"JOIN数: {selectQuery.Joins.Count}"));
            children.Add(Node($"サブクエリ数: {selectQuery.Subqueries.Count}"));
        }
        else if (result.Query is SetOperationQueryAnalysis setOperationQuery)
        {
            children.Add(Node($"集合演算: {BuildSetOperationText(setOperationQuery.OperationType)}"));
            children.Add(Node($"子集合演算数: {CountNestedSetOperations(setOperationQuery)}"));
            children.Add(Node("左右のクエリ構造を個別に確認できます。"));
        }
        else if (result.DataModification is UpdateStatementAnalysis updateStatement)
        {
            children.Add(Node($"TOP: {updateStatement.TopExpressionText ?? "なし"}"));
            children.Add(Node($"SET数: {updateStatement.SetClauses.Count}"));
            children.Add(Node($"JOIN数: {updateStatement.Joins.Count}"));
            children.Add(Node($"サブクエリ数: {updateStatement.Subqueries.Count}"));
        }
        else if (result.DataModification is InsertStatementAnalysis insertStatement)
        {
            children.Add(Node($"TOP: {insertStatement.TopExpressionText ?? "なし"}"));
            children.Add(Node($"挿入列数: {insertStatement.TargetColumns.Count}"));
            children.Add(Node($"入力元: {BuildInsertSourceKindText(insertStatement.InsertSource?.SourceKind)}"));
            children.Add(Node($"サブクエリ数: {insertStatement.Subqueries.Count}"));
        }
        else if (result.DataModification is DeleteStatementAnalysis deleteStatement)
        {
            children.Add(Node($"TOP: {deleteStatement.TopExpressionText ?? "なし"}"));
            children.Add(Node($"JOIN数: {deleteStatement.Joins.Count}"));
            children.Add(Node($"サブクエリ数: {deleteStatement.Subqueries.Count}"));
        }
        else if (result.CreateStatement is CreateViewAnalysis createView)
        {
            children.Add(Node("作成形式: VIEW"));
            children.Add(Node($"名前: {createView.Name}"));
            children.Add(Node($"列数: {createView.ColumnNames.Count}"));
        }
        else if (result.CreateStatement is CreateTableAnalysis createTable)
        {
            children.Add(Node("作成形式: TABLE"));
            children.Add(Node($"名前: {createTable.Name}"));
            children.Add(Node($"列数: {createTable.Columns.Count}"));
            children.Add(Node($"内部クエリ: {(createTable.Query is null ? "なし" : "あり")}"));
        }
        else
        {
            children.Add(Node(BuildFallbackOverview(result)));
        }

        return Node(DisplayTreeNodeKind.Section, "概要", children.ToArray());
    }

    /// <summary>
    /// CTE ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildCommonTableExpressionsNode(QueryAnalysisResult result)
    {
        if (result.CommonTableExpressions.Count == 0)
        {
            return Node(DisplayTreeNodeKind.CommonTableExpression, "共通テーブル式", Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.CommonTableExpression,
            "共通テーブル式",
            result.CommonTableExpressions
                .Select((cte, index) => Node(
                    DisplayTreeNodeKind.CommonTableExpression,
                    $"CTE #{index + 1}: {cte.Name}",
                    BuildCteColumnsNode(cte),
                    BuildQueryNode("内部構造", cte.Query)))
                .Concat([BuildCteReferenceSummaryNode(result), BuildCteDependencyOrderNode(result)])
                .ToArray());
    }

    /// <summary>
    /// ルート解析結果から主構造ノードを作る。
    /// SELECT 系は既存のクエリ表示を使い、DML は専用表示へ切り替える。
    /// </summary>
    private static DisplayTreeNode BuildMainStructureNode(QueryAnalysisResult result)
    {
        if (result.Query is not null)
        {
            return BuildQueryNode("主構造", result.Query);
        }

        if (result.CreateStatement is not null)
        {
            return BuildCreateStatementNode(result.CreateStatement);
        }

        return result.DataModification is not null
            ? BuildDataModificationNode(result.DataModification)
            : Node(DisplayTreeNodeKind.Section, "主構造", Node("解析対象なし"));
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
            _ => Node(DisplayTreeNodeKind.Section, title, Node("未対応のクエリ構造です。"))
        };
    }

    /// <summary>
    /// DML 文の主構造ノードを作る。
    /// 更新・挿入・削除で見出しを分けつつ、共通部分は同じ補助メソッドで構築する。
    /// </summary>
    private static DisplayTreeNode BuildDataModificationNode(DataModificationAnalysis dataModification)
    {
        return dataModification switch
        {
            UpdateStatementAnalysis updateStatement => BuildUpdateNode(updateStatement),
            InsertStatementAnalysis insertStatement => BuildInsertNode(insertStatement),
            DeleteStatementAnalysis deleteStatement => BuildDeleteNode(deleteStatement),
            _ => Node(DisplayTreeNodeKind.DataModification, "主構造", Node("未対応の文構造です。"))
        };
    }

    /// <summary>
    /// CREATE 文の主構造ノードを作る。
    /// VIEW と TABLE を分けて表示しつつ、内部クエリは既存の SELECT 表示を再利用する。
    /// </summary>
    private static DisplayTreeNode BuildCreateStatementNode(CreateStatementAnalysis createStatement)
    {
        return createStatement switch
        {
            CreateViewAnalysis createView => BuildCreateViewNode(createView),
            CreateTableAnalysis createTable => BuildCreateTableNode(createTable),
            _ => Node(DisplayTreeNodeKind.Create, "主構造", Node("未対応の CREATE 構造です。"))
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
            BuildSelectIntoNode(query),
            BuildMainSourceNode(query),
            BuildJoinNode(query),
            BuildWhereNode(query),
            BuildAggregationNode(query),
            BuildOrderByNode(query)
        };

        return NodeWithSpan(DisplayTreeNodeKind.Section, title, query.SourceSpan, children.ToArray());
    }

    /// <summary>
    /// CREATE VIEW 文の主構造ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildCreateViewNode(CreateViewAnalysis createView)
    {
        return NodeWithSpan(
            DisplayTreeNodeKind.Create,
            "主構造",
            createView.SourceSpan,
            Node(
                DisplayTreeNodeKind.Create,
                "作成対象",
                Node("作成形式: VIEW"),
                Node($"名前: {createView.Name}")),
            BuildCreateViewColumnsNode(createView),
            Node($"WITH CHECK OPTION: {(createView.WithCheckOption ? "あり" : "なし")}"),
            BuildQueryNode("内部クエリ", createView.Query));
    }

    /// <summary>
    /// CREATE TABLE 文の主構造ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildCreateTableNode(CreateTableAnalysis createTable)
    {
        var children = new List<DisplayTreeNode>
        {
            Node(
                DisplayTreeNodeKind.Create,
                "作成対象",
                Node("作成形式: TABLE"),
                Node($"名前: {createTable.Name}")),
            BuildCreateTableColumnsNode(createTable)
        };

        if (createTable.Query is not null)
        {
            children.Add(BuildQueryNode("内部クエリ", createTable.Query));
        }

        return NodeWithSpan(DisplayTreeNodeKind.Create, "主構造", createTable.SourceSpan, children.ToArray());
    }

    /// <summary>
    /// UPDATE 文の主構造ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildUpdateNode(UpdateStatementAnalysis updateStatement)
    {
        return NodeWithSpan(
            DisplayTreeNodeKind.DataModification,
            "主構造",
            updateStatement.SourceSpan,
            BuildSourceSectionNode("更新対象", updateStatement.Target),
            BuildUpdateSetClausesNode(updateStatement),
            BuildSourceSectionNode("参照ソース", updateStatement.MainSource, "なし"),
            BuildJoinNode(updateStatement.Joins),
            BuildConditionSectionNode("抽出条件", updateStatement.WhereCondition),
            BuildOutputNode(updateStatement));
    }

    /// <summary>
    /// INSERT 文の主構造ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildInsertNode(InsertStatementAnalysis insertStatement)
    {
        return NodeWithSpan(
            DisplayTreeNodeKind.DataModification,
            "主構造",
            insertStatement.SourceSpan,
            BuildSourceSectionNode("挿入対象", insertStatement.Target),
            BuildInsertColumnsNode(insertStatement),
            BuildInsertSourceNode(insertStatement),
            BuildOutputNode(insertStatement));
    }

    /// <summary>
    /// DELETE 文の主構造ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildDeleteNode(DeleteStatementAnalysis deleteStatement)
    {
        return NodeWithSpan(
            DisplayTreeNodeKind.DataModification,
            "主構造",
            deleteStatement.SourceSpan,
            BuildSourceSectionNode("削除対象", deleteStatement.Target),
            BuildSourceSectionNode("参照ソース", deleteStatement.MainSource, "なし"),
            BuildJoinNode(deleteStatement.Joins),
            BuildConditionSectionNode("抽出条件", deleteStatement.WhereCondition),
            BuildOutputNode(deleteStatement));
    }

    /// <summary>
    /// 取得項目ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildSelectItemsNode(SelectQueryAnalysis query)
    {
        if (query.SelectItems.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Section, "取得項目", Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.Section,
            "取得項目",
            query.SelectItems
                .Select(BuildSelectItemNode)
                .ToArray());
    }

    /// <summary>
    /// SELECT INTO の出力先ノードを作る。
    /// INTO がない通常 SELECT では「なし」を表示する。
    /// </summary>
    private static DisplayTreeNode BuildSelectIntoNode(SelectQueryAnalysis query)
    {
        return BuildSourceSectionNode("出力先", query.IntoTarget, "なし");
    }

    /// <summary>
    /// SELECT 項目 1 件分のノードを作る。
    /// ワイルドカード項目では、利用者が追う必要のある修飾子だけを表示する。
    /// </summary>
    private static DisplayTreeNode BuildSelectItemNode(SelectItemAnalysis item)
    {
        var children = new List<DisplayTreeNode>
        {
            NodeWithSpan($"式: {item.ExpressionText}", item.SourceSpan),
            Node($"別名: {item.Alias ?? "なし"}")
        };

        if (item.ColumnReferences.Count > 0)
        {
            children.Add(BuildColumnReferencesNode(item.ColumnReferences));
        }

        if (item.Kind == SelectItemKind.Wildcard)
        {
            children.Add(Node($"修飾子: {item.WildcardQualifier ?? "なし"}"));
        }

        return NodeWithSpan(DisplayTreeNodeKind.Select, $"項目 #{item.Sequence}: {item.DisplayText}", item.SourceSpan, children.ToArray());
    }

    /// <summary>
    /// ソース表示用の共通ノードを作る。
    /// 主テーブル、更新対象、削除対象、参照ソースで共通利用する。
    /// </summary>
    private static DisplayTreeNode BuildSourceSectionNode(string title, SourceAnalysis? source, string emptyText = "なし")
    {
        if (source is null)
        {
            return Node(DisplayTreeNodeKind.Source, title, Node(emptyText));
        }

        var children = new List<DisplayTreeNode>
        {
            Node(source.DisplayText),
            Node($"別名: {source.Alias ?? "なし"}"),
            BuildSourceKindNode(source)
        };

        if (source.NestedQuery is not null)
        {
            children.Add(BuildQueryNode($"{title}の内部構造", source.NestedQuery));
        }

        return NodeWithSpan(DisplayTreeNodeKind.Source, title, source.SourceSpan, children.ToArray());
    }

    /// <summary>
    /// 条件表示用の共通ノードを作る。
    /// WHERE と DML の抽出条件で同じ見せ方を使う。
    /// </summary>
    private static DisplayTreeNode BuildConditionSectionNode(string title, ConditionAnalysis? condition)
    {
        if (condition is null)
        {
            return Node(DisplayTreeNodeKind.Condition, title, Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.Condition,
            title,
            NodeWithSpan($"条件式: {condition.DisplayText}", condition.SourceSpan),
            BuildConditionLogicNode(condition));
    }

    /// <summary>
    /// UPDATE の SET 一覧ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildUpdateSetClausesNode(UpdateStatementAnalysis updateStatement)
    {
        if (updateStatement.SetClauses.Count == 0)
        {
            return Node(DisplayTreeNodeKind.DataModification, "更新内容", Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.DataModification,
            "更新内容",
            updateStatement.SetClauses
                .Select(setClause => Node(
                    DisplayTreeNodeKind.DataModification,
                    $"SET #{setClause.Sequence}",
                    NodeWithSpan($"式: {setClause.DisplayText}", setClause.SourceSpan),
                    Node($"列: {setClause.TargetText}"),
                    Node($"値: {setClause.ValueText}")))
                .ToArray());
    }

    /// <summary>
    /// INSERT の列一覧ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildInsertColumnsNode(InsertStatementAnalysis insertStatement)
    {
        if (insertStatement.TargetColumns.Count == 0)
        {
            return Node(DisplayTreeNodeKind.DataModification, "挿入列", Node("省略"));
        }

        return Node(
            DisplayTreeNodeKind.DataModification,
            "挿入列",
            insertStatement.TargetColumns
                .Select((column, index) => Node($"列 #{index + 1}: {column}"))
                .ToArray());
    }

    /// <summary>
    /// INSERT の入力元ノードを作る。
    /// SELECT 入力元では内部クエリも辿れるようにする。
    /// </summary>
    private static DisplayTreeNode BuildInsertSourceNode(InsertStatementAnalysis insertStatement)
    {
        if (insertStatement.InsertSource is null)
        {
            return Node(DisplayTreeNodeKind.DataModification, "入力元", Node("なし"));
        }

        var children = new List<DisplayTreeNode>
        {
            Node($"入力形式: {BuildInsertSourceKindText(insertStatement.InsertSource.SourceKind)}"),
            Node($"式: {insertStatement.InsertSource.DisplayText}")
        };

        if (insertStatement.InsertSource.Items.Count > 0)
        {
            children.Add(Node(
                "項目",
                insertStatement.InsertSource.Items
                    .Select((item, index) => Node($"項目 #{index + 1}: {item}"))
                    .ToArray()));
        }

        if (insertStatement.InsertSource.MappingGroups.Count > 0)
        {
            children.Add(BuildInsertValueMappingsNode(insertStatement.InsertSource));
        }

        if (insertStatement.InsertSource.Query is not null)
        {
            children.Add(BuildQueryNode("内部クエリ", insertStatement.InsertSource.Query));
        }

        if (!string.IsNullOrWhiteSpace(insertStatement.InsertSource.ExecuteText))
        {
            children.Add(Node($"実行文: {insertStatement.InsertSource.ExecuteText}"));
        }

        return NodeWithSpan(DisplayTreeNodeKind.DataModification, "入力元", insertStatement.InsertSource.SourceSpan, children.ToArray());
    }

    /// <summary>
    /// CREATE VIEW の列一覧ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildCreateViewColumnsNode(CreateViewAnalysis createView)
    {
        if (createView.ColumnNames.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Create, "列定義", Node("省略"));
        }

        return Node(
            DisplayTreeNodeKind.Create,
            "列定義",
            createView.ColumnNames
                .Select((column, index) => Node($"列 #{index + 1}: {column}"))
                .ToArray());
    }

    /// <summary>
    /// CREATE TABLE の列一覧ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildCreateTableColumnsNode(CreateTableAnalysis createTable)
    {
        if (createTable.Columns.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Create, "列定義", Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.Create,
            "列定義",
            createTable.Columns
                .Select(column => Node(
                    DisplayTreeNodeKind.Create,
                    $"列 #{column.Sequence}: {column.Name}",
                    NodeWithSpan($"式: {column.DisplayText}", column.SourceSpan),
                    Node($"データ型: {column.DataType}"),
                    Node($"NULL許可: {(column.IsNullable ? "あり" : "なし")}")))
                .ToArray());
    }

    /// <summary>
    /// INSERT の列と値の対応ノードを作る。
    /// 列ごとの値を 1 行で並べ、VALUES の複数行や SELECT 列対応を追いやすくする。
    /// </summary>
    private static DisplayTreeNode BuildInsertValueMappingsNode(InsertSourceAnalysis insertSource)
    {
        return Node(
            DisplayTreeNodeKind.DataModification,
            "列と値の対応",
            insertSource.MappingGroups
                .Select(group => NodeWithSpan(
                    DisplayTreeNodeKind.DataModification,
                    group.Title,
                    group.SourceSpan,
                    group.Mappings
                        .Select(mapping => NodeWithSpan(
                            DisplayTreeNodeKind.DataModification,
                            $"対応 #{mapping.Sequence}: {mapping.TargetColumn} ← {mapping.ValueText}",
                            mapping.SourceSpan))
                        .ToArray()))
                .ToArray());
    }

    /// <summary>
    /// DML の OUTPUT 情報ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildOutputNode(DataModificationAnalysis dataModification)
    {
        if (string.IsNullOrWhiteSpace(dataModification.OutputClauseText)
            && string.IsNullOrWhiteSpace(dataModification.OutputIntoClauseText))
        {
            return Node(DisplayTreeNodeKind.DataModification, "出力", Node("なし"));
        }

        var children = new List<DisplayTreeNode>();

        if (!string.IsNullOrWhiteSpace(dataModification.OutputClauseText))
        {
            children.Add(Node($"OUTPUT: {dataModification.OutputClauseText}"));
        }

        if (!string.IsNullOrWhiteSpace(dataModification.OutputIntoClauseText))
        {
            children.Add(Node($"OUTPUT INTO: {dataModification.OutputIntoClauseText}"));
        }

        return Node(DisplayTreeNodeKind.DataModification, "出力", children.ToArray());
    }

    /// <summary>
    /// 主テーブルノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildMainSourceNode(SelectQueryAnalysis query)
    {
        return BuildSourceSectionNode("主テーブル", query.MainSource, "なし");
    }

    /// <summary>
    /// JOIN ノードを仕様に合わせて構築する。
    /// </summary>
    private static DisplayTreeNode BuildJoinNode(SelectQueryAnalysis query)
    {
        return BuildJoinNode(query.Joins);
    }

    /// <summary>
    /// JOIN 一覧から結合ノードを作る。
    /// SELECT と DML の両方で同じ表示仕様を使う。
    /// </summary>
    private static DisplayTreeNode BuildJoinNode(IReadOnlyList<JoinAnalysis> joins)
    {
        if (joins.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Join, "結合", Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.Join,
            "結合",
            joins.Select(BuildJoinDetailNode).ToArray());
    }

    /// <summary>
    /// WHERE ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildWhereNode(SelectQueryAnalysis query)
    {
        return BuildConditionSectionNode("抽出条件", query.WhereCondition);
    }

    /// <summary>
    /// GROUP BY / HAVING ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildAggregationNode(SelectQueryAnalysis query)
    {
        var children = new List<DisplayTreeNode>();

        if (query.GroupBy is not null)
        {
            children.Add(BuildGroupByNode(query.GroupBy));
        }

        if (query.HavingCondition is not null)
        {
            children.Add(Node(
                "HAVING",
                NodeWithSpan($"条件式: {query.HavingCondition.DisplayText}", query.HavingCondition.SourceSpan),
                BuildConditionLogicNode(query.HavingCondition)));
        }

        if (children.Count == 0)
        {
            children.Add(Node("なし"));
        }

        return Node(DisplayTreeNodeKind.Section, "集計", children.ToArray());
    }

    /// <summary>
    /// ORDER BY ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildOrderByNode(SelectQueryAnalysis query)
    {
        if (query.OrderBy is null || query.OrderBy.Items.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Section, "並び順", Node("なし"));
        }

        return BuildOrderBySectionNode(query.OrderBy);
    }

    /// <summary>
    /// 集合演算ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildSetOperationNode(QueryExpressionAnalysis query)
    {
        if (query is not SetOperationQueryAnalysis setOperationQuery)
        {
            return Node(DisplayTreeNodeKind.SetOperation, "集合演算", Node("なし"));
        }

        return BuildSetOperationStructureNode("集合演算", setOperationQuery);
    }

    /// <summary>
    /// サブクエリノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildSubqueryNode(QueryAnalysisResult result)
    {
        if (result.Query is SelectQueryAnalysis selectQuery)
        {
            return BuildSubqueryListNode(selectQuery.Subqueries);
        }

        if (result.DataModification is not null)
        {
            return BuildSubqueryListNode(result.DataModification.Subqueries);
        }

        if (result.CreateStatement is CreateViewAnalysis createView)
        {
            return BuildSubqueryListNode(GetSubqueries(createView.Query));
        }

        if (result.CreateStatement is CreateTableAnalysis createTable && createTable.Query is not null)
        {
            return BuildSubqueryListNode(GetSubqueries(createTable.Query));
        }

        return Node(DisplayTreeNodeKind.Subquery, "サブクエリ", Node("なし"));
    }

    /// <summary>
    /// サブクエリ一覧ノードを作る。
    /// SELECT と DML の両方で同じ一覧表示を使う。
    /// </summary>
    private static DisplayTreeNode BuildSubqueryListNode(IReadOnlyList<SubqueryAnalysis> subqueries)
    {
        if (subqueries.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Subquery, "サブクエリ", Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.Subquery,
            "サブクエリ",
            subqueries
                .Select((subquery, index) => NodeWithSpan(
                    DisplayTreeNodeKind.Subquery,
                    $"サブクエリ #{index + 1}",
                    subquery.SourceSpan,
                    Node($"場所: {subquery.Location}"),
                    NodeWithSpan($"概要: {subquery.DisplayText}", subquery.SourceSpan),
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
            return Node(DisplayTreeNodeKind.CommonTableExpression, "列定義", Node("省略"));
        }

        return Node(DisplayTreeNodeKind.CommonTableExpression, "列定義", cte.ColumnNames.Select(columnName => Node(columnName)).ToArray());
    }

    /// <summary>
    /// CTE 参照関係の要約ノードを作る。
    /// メインクエリと各 CTE が、どの CTE 名を参照しているかを一覧化する。
    /// </summary>
    private static DisplayTreeNode BuildCteReferenceSummaryNode(QueryAnalysisResult result)
    {
        var children = new List<DisplayTreeNode>
        {
            Node($"メインクエリ: {BuildReferencedCteNamesText(result)}")
        };

        children.AddRange(result.CommonTableExpressions.Select(cte =>
            Node($"CTE {cte.Name}: {BuildReferencedCteNamesText(cte.Query)}")));

        return Node(DisplayTreeNodeKind.CommonTableExpression, "参照関係", children.ToArray());
    }

    /// <summary>
    /// CTE の依存順ノードを作る。
    /// 非循環な CTE は読み始める順に並べ、循環がある場合は別ノードで明示する。
    /// </summary>
    private static DisplayTreeNode BuildCteDependencyOrderNode(QueryAnalysisResult result)
    {
        var dependencyReport = CommonTableExpressionDependencyAnalyzer.Analyze(result.CommonTableExpressions);
        var children = dependencyReport.DependencyOrder
            .Select((name, index) => Node($"手順 {index + 1}: {name}"))
            .ToList();

        if (dependencyReport.CyclicNames.Count > 0)
        {
            children.Add(Node($"循環あり: {string.Join(", ", dependencyReport.CyclicNames)}"));
        }

        if (children.Count == 0)
        {
            children.Add(Node("依存なし"));
        }

        return Node(DisplayTreeNodeKind.CommonTableExpression, "依存順", children.ToArray());
    }

    /// <summary>
    /// ソースが内部クエリを持つ場合だけ補助ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildNestedSourceNode(string title, SourceAnalysis source)
    {
        if (source.NestedQuery is null)
        {
            return Node(DisplayTreeNodeKind.Subquery, title);
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
            return Node(DisplayTreeNodeKind.Source, $"分類: {text}", Node($"名前: {source.SourceName}"));
        }

        return Node(DisplayTreeNodeKind.Source, $"分類: {text}");
    }

    /// <summary>
    /// JOIN 詳細ノードを作る。
    /// </summary>
    private static DisplayTreeNode BuildJoinDetailNode(JoinAnalysis join)
    {
        var children = new List<DisplayTreeNode>
        {
            Node($"結合形式: {join.JoinTypeText}"),
            Node($"結合先: {join.TargetSource.DisplayText}"),
            BuildSourceKindNode(join.TargetSource),
            BuildJoinOnConditionNode(join)
        };

        if (join.TargetSource.NestedQuery is not null)
        {
            children.Add(BuildNestedSourceNode("結合先の内部構造", join.TargetSource));
        }

        return NodeWithSpan(DisplayTreeNodeKind.Join, $"JOIN #{join.Sequence}", join.SourceSpan, children.ToArray());
    }

    /// <summary>
    /// JOIN の ON 条件を見やすい一覧へ変換する。
    /// 1 件でも一覧形式に揃え、複数条件時の見通しを安定させる。
    /// </summary>
    private static DisplayTreeNode BuildJoinOnConditionNode(JoinAnalysis join)
    {
        if (join.OnConditionParts.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Join, "ON条件", Node("なし"));
        }

        return Node(
            DisplayTreeNodeKind.Join,
            "ON条件",
            join.OnConditionParts
                .Select(part =>
                {
                    var children = part.ColumnReferences.Count > 0
                        ? new[] { BuildColumnReferencesNode(part.ColumnReferences) }
                        : Array.Empty<DisplayTreeNode>();
                    var conditionText = part.Sequence == 1
                        ? $"条件 #{part.Sequence}: {part.DisplayText}"
                        : $"AND 条件 #{part.Sequence}: {part.DisplayText}";
                    return NodeWithSpan(DisplayTreeNodeKind.Condition, conditionText, part.SourceSpan, children);
                })
                .ToArray());
    }

    /// <summary>
    /// 条件式の論理木ノードを作る。
    /// 複雑な AND / OR / NOT を上から辿れるようにする。
    /// </summary>
    private static DisplayTreeNode BuildConditionLogicNode(ConditionAnalysis condition)
    {
        return NodeWithSpan(DisplayTreeNodeKind.Condition, "条件論理", condition.SourceSpan, BuildConditionLogicTreeNode(condition.RootNode));
    }

    /// <summary>
    /// 条件論理木 1 ノード分を再帰的に変換する。
    /// </summary>
    private static DisplayTreeNode BuildConditionLogicTreeNode(ConditionNodeAnalysis node)
    {
        if (node.NodeKind == ConditionNodeKind.Predicate)
        {
            var children = new List<DisplayTreeNode>();

            children.Add(NodeWithSpan($"式: {node.DisplayText}", node.SourceSpan));

            if (node.ColumnReferences.Count > 0)
            {
                children.Add(BuildColumnReferencesNode(node.ColumnReferences));
            }

            if (node.PredicateKind == ConditionPredicateKind.Between)
            {
                children.Add(Node($"範囲: {BuildBetweenKindText(node.BetweenKind)}"));
            }

            if (node.PredicateKind == ConditionPredicateKind.Like)
            {
                children.Add(Node($"LIKE: {BuildLikeKindText(node.LikeKind)}"));
            }

            if (node.Marker?.NestedQuery is not null)
            {
                children.Add(BuildQueryNode("内部クエリ", node.Marker.NestedQuery));
            }

            return NodeWithSpan(DisplayTreeNodeKind.Condition, BuildConditionNodeTitle(node), node.SourceSpan, children.ToArray());
        }

        var logicalChildren = new List<DisplayTreeNode>();
        logicalChildren.AddRange(node.Children.Select(BuildConditionLogicTreeNode));

        return NodeWithSpan(
            DisplayTreeNodeKind.Condition,
            BuildConditionNodeTitle(node),
            node.SourceSpan,
            logicalChildren.ToArray());
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
    /// BETWEEN 種別の表示名を返す。
    /// </summary>
    private static string BuildBetweenKindText(ConditionBetweenKind betweenKind)
    {
        return betweenKind switch
        {
            ConditionBetweenKind.Between => "BETWEEN",
            ConditionBetweenKind.NotBetween => "NOT BETWEEN",
            _ => "不明"
        };
    }

    /// <summary>
    /// INSERT 入力元種別の表示名を返す。
    /// </summary>
    private static string BuildInsertSourceKindText(InsertSourceKind? kind)
    {
        return kind switch
        {
            InsertSourceKind.Values => "VALUES",
            InsertSourceKind.Query => "SELECTクエリ",
            InsertSourceKind.Execute => "EXECUTE",
            InsertSourceKind.Unknown => "不明",
            _ => "なし"
        };
    }

    /// <summary>
    /// ORDER BY 方向の表示名を返す。
    /// 省略時も既定の ASC として画面に出す。
    /// </summary>
    private static string BuildOrderByDirectionText(OrderByDirection direction)
    {
        return direction switch
        {
            OrderByDirection.Descending => "DESC",
            OrderByDirection.Ascending => "ASC",
            _ => "未指定"
        };
    }

    /// <summary>
    /// LIKE 種別の表示名を返す。
    /// </summary>
    private static string BuildLikeKindText(ConditionLikeKind likeKind)
    {
        return likeKind switch
        {
            ConditionLikeKind.Like => "LIKE",
            ConditionLikeKind.NotLike => "NOT LIKE",
            _ => "不明"
        };
    }

    /// <summary>
    /// クエリが参照している CTE 名の一覧を文字列化する。
    /// </summary>
    private static string BuildReferencedCteNamesText(QueryAnalysisResult result)
    {
        var names = result.Query is not null
            ? CommonTableExpressionDependencyAnalyzer.GetReferencedNames(result.Query)
            : result.DataModification is not null
                ? CommonTableExpressionDependencyAnalyzer.GetReferencedNames(result.DataModification)
                : CommonTableExpressionDependencyAnalyzer.GetReferencedNames(result.CreateStatement);
        return names.Count == 0 ? "なし" : string.Join(", ", names);
    }

    /// <summary>
    /// クエリが参照している CTE 名の一覧を文字列化する。
    /// </summary>
    private static string BuildReferencedCteNamesText(QueryExpressionAnalysis? query)
    {
        var names = CommonTableExpressionDependencyAnalyzer.GetReferencedNames(query);
        return names.Count == 0 ? "なし" : string.Join(", ", names);
    }

    /// <summary>
    /// 集合演算ノードを再帰的に構築する。
    /// 入れ子の集合演算でも種別が失われないよう、左クエリ・右クエリの上に種別を置く。
    /// </summary>
    private static DisplayTreeNode BuildSetOperationStructureNode(string title, SetOperationQueryAnalysis setOperationQuery)
    {
        return NodeWithSpan(
            DisplayTreeNodeKind.SetOperation,
            title,
            setOperationQuery.SourceSpan,
            Node($"集合演算: {BuildSetOperationText(setOperationQuery.OperationType)}"),
            Node(
                DisplayTreeNodeKind.Section,
                "概要",
                Node($"子集合演算数: {CountNestedSetOperations(setOperationQuery)}"),
                BuildQuerySummaryNode("左概要", setOperationQuery.LeftQuery),
                BuildQuerySummaryNode("右概要", setOperationQuery.RightQuery)),
            BuildQueryNode("左クエリ", setOperationQuery.LeftQuery),
            BuildQueryNode("右クエリ", setOperationQuery.RightQuery));
    }

    /// <summary>
    /// 任意のクエリ式の概要ノードを作る。
    /// 集合演算の左右差を先に掴めるよう、要点だけを短く並べる。
    /// </summary>
    private static DisplayTreeNode BuildQuerySummaryNode(string title, QueryExpressionAnalysis query)
    {
        return query switch
        {
            SelectQueryAnalysis selectQuery => Node(
                DisplayTreeNodeKind.Select,
                title,
                Node("クエリ: SELECT"),
                Node(selectQuery.IsDistinct ? "DISTINCT: あり" : "DISTINCT: なし"),
                Node($"INTO: {selectQuery.IntoTarget?.DisplayText ?? "なし"}"),
                Node($"取得項目数: {selectQuery.SelectItems.Count}"),
                Node($"JOIN数: {selectQuery.Joins.Count}"),
                Node($"サブクエリ数: {selectQuery.Subqueries.Count}"),
                Node($"主ソース: {selectQuery.MainSource?.DisplayText ?? "なし"}")),
            SetOperationQueryAnalysis setOperationQuery => Node(
                DisplayTreeNodeKind.SetOperation,
                title,
                Node("クエリ: 集合演算"),
                Node($"集合演算: {BuildSetOperationText(setOperationQuery.OperationType)}"),
                Node($"子集合演算数: {CountNestedSetOperations(setOperationQuery)}")),
            _ => Node(DisplayTreeNodeKind.Section, title, Node("クエリ: 不明"))
        };
    }

    /// <summary>
    /// GROUP BY の詳細ノードを作る。
    /// 項目単位で式と参照列を辿れるようにする。
    /// </summary>
    private static DisplayTreeNode BuildGroupByNode(GroupByAnalysis groupBy)
    {
        if (groupBy.GroupingItems.Count == 0)
        {
            return Node(DisplayTreeNodeKind.Section, "GROUP BY", groupBy.Items.Select(item => Node(item)).ToArray());
        }

        return Node(
            DisplayTreeNodeKind.Section,
            "GROUP BY",
            groupBy.GroupingItems
                .Select(groupingItem =>
                {
                    var children = new List<DisplayTreeNode>
                    {
                        NodeWithSpan($"式: {groupingItem.ExpressionText}", groupingItem.SourceSpan)
                    };

                    if (groupingItem.ColumnReferences.Count > 0)
                    {
                        children.Add(BuildColumnReferencesNode(groupingItem.ColumnReferences));
                    }

                    return NodeWithSpan(DisplayTreeNodeKind.Select, $"項目 #{groupingItem.Sequence}: {groupingItem.DisplayText}", groupingItem.SourceSpan, children.ToArray());
                })
                .ToArray());
    }

    /// <summary>
    /// ORDER BY の詳細ノードを作る。
    /// 項目ごとの式、方向、参照列を分けて表示する。
    /// </summary>
    private static DisplayTreeNode BuildOrderBySectionNode(OrderByAnalysis orderBy)
    {
        if (orderBy.OrderItems.Count == 0)
        {
            return Node(
                DisplayTreeNodeKind.Section,
                "並び順",
                orderBy.Items
                    .Select((item, index) => Node($"項目 #{index + 1}: {item}"))
                    .ToArray());
        }

        return Node(
            DisplayTreeNodeKind.Section,
            "並び順",
            orderBy.OrderItems
                .Select(orderItem =>
                {
                    var children = new List<DisplayTreeNode>
                    {
                        NodeWithSpan($"式: {orderItem.ExpressionText}", orderItem.SourceSpan),
                        Node($"方向: {BuildOrderByDirectionText(orderItem.Direction)}")
                    };

                    if (orderItem.ColumnReferences.Count > 0)
                    {
                        children.Add(BuildColumnReferencesNode(orderItem.ColumnReferences));
                    }

                    return NodeWithSpan(DisplayTreeNodeKind.Select, $"項目 #{orderItem.Sequence}: {orderItem.ExpressionText}", orderItem.SourceSpan, children.ToArray());
                })
                .ToArray());
    }

    /// <summary>
    /// 列参照一覧ノードを作る。
    /// 各式がどの別名・列を参照しているかをまとめて確認できるようにする。
    /// </summary>
    private static DisplayTreeNode BuildColumnReferencesNode(IReadOnlyList<ColumnReferenceAnalysis> columnReferences)
    {
        return Node(
            DisplayTreeNodeKind.ColumnReference,
            "参照列",
            columnReferences
                .Select(columnReference => NodeWithSpan(
                    DisplayTreeNodeKind.ColumnReference,
                    $"列 #{columnReference.Sequence}: {columnReference.DisplayText}",
                    columnReference.SourceSpan,
                    Node($"修飾子: {columnReference.Qualifier ?? "なし"}"),
                    Node($"列名: {columnReference.ColumnName}"),
                    Node($"参照先: {columnReference.ResolvedTargetDisplayText ?? columnReference.ResolvedSourceDisplayText ?? "なし"}"),
                    Node($"参照別名: {columnReference.ResolvedSourceAlias ?? "なし"}")))
                .ToArray());
    }

    /// <summary>
    /// 子孫にある集合演算ノード数を返す。
    /// 現在ノード自身は含めず、左右にどれだけ集合演算がぶら下がっているかだけを数える。
    /// </summary>
    private static int CountNestedSetOperations(SetOperationQueryAnalysis query)
    {
        return CountNestedSetOperations(query.LeftQuery) + CountNestedSetOperations(query.RightQuery);
    }

    /// <summary>
    /// 任意のクエリ式配下にある集合演算ノード数を返す。
    /// </summary>
    private static int CountNestedSetOperations(QueryExpressionAnalysis query)
    {
        if (query is not SetOperationQueryAnalysis setOperationQuery)
        {
            return 0;
        }

        return 1 + CountNestedSetOperations(setOperationQuery.LeftQuery) + CountNestedSetOperations(setOperationQuery.RightQuery);
    }

    /// <summary>
    /// 空入力や未対応時の概要を作る。
    /// </summary>
    private static string BuildFallbackOverview(QueryAnalysisResult result)
    {
        return result.StatementCategory switch
        {
            QueryStatementCategory.Empty => "SQL の入力待ちです。",
            QueryStatementCategory.Unsupported => "未対応の文種別です。",
            QueryStatementCategory.ParseError => "構文エラーのため構造を展開できません。",
            _ => "解析結果はありません。"
        };
    }

    /// <summary>
    /// クエリ配下のサブクエリ一覧を返す。
    /// SELECT 以外では現在の表示対象を持たないため空配列を返す。
    /// </summary>
    private static IReadOnlyList<SubqueryAnalysis> GetSubqueries(QueryExpressionAnalysis query)
    {
        return query switch
        {
            SelectQueryAnalysis selectQuery => selectQuery.Subqueries,
            SetOperationQueryAnalysis setOperationQuery => GetSubqueries(setOperationQuery.LeftQuery)
                .Concat(GetSubqueries(setOperationQuery.RightQuery))
                .ToArray(),
            _ => []
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
    /// 子ノード付きノードを簡潔に作るヘルパー。
    /// </summary>
    private static DisplayTreeNode Node(string text, params DisplayTreeNode[] children)
    {
        return Node(DisplayTreeNodeKind.Detail, text, children);
    }

    /// <summary>
    /// 表示分類を指定して子ノード付きノードを作るヘルパー。
    /// WinForms 側はこの分類を使って色や太字を決める。
    /// </summary>
    private static DisplayTreeNode Node(DisplayTreeNodeKind kind, string text, params DisplayTreeNode[] children)
    {
        return new DisplayTreeNode(text, children, Kind: kind);
    }

    /// <summary>
    /// SQL 上の位置情報を持つ表示ノードを作る。
    /// 選択連動や全文表示の起点として使う。
    /// </summary>
    private static DisplayTreeNode NodeWithSpan(string text, TextSpan? sourceSpan, params DisplayTreeNode[] children)
    {
        return NodeWithSpan(DisplayTreeNodeKind.Detail, text, sourceSpan, children);
    }

    /// <summary>
    /// 表示分類と SQL 上の位置情報を持つ表示ノードを作る。
    /// 構文ハイライト連動と TreeView の視認性向上を両立するための補助メソッド。
    /// </summary>
    private static DisplayTreeNode NodeWithSpan(DisplayTreeNodeKind kind, string text, TextSpan? sourceSpan, params DisplayTreeNode[] children)
    {
        return new DisplayTreeNode(text, children, sourceSpan, kind);
    }
}
