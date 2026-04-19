using TSqlAnalyzer.Application.Analysis;
using TSqlAnalyzer.Application.Services;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Tests.Analysis;

/// <summary>
/// 解析ロジックの主要な期待値を固定するテスト。
/// ここで仕様を先に明文化し、その後に最小実装を合わせ込む。
/// </summary>
public sealed class QueryAnalysisServiceTests
{
    /// <summary>
    /// 単純な SELECT の基本情報を取得できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SimpleSelect_ReturnsBasicStructure()
    {
        var service = CreateService();
        const string sql = """
                           SELECT DISTINCT TOP 10
                               u.Id,
                               u.Name
                           FROM dbo.Users u
                           ORDER BY u.Name;
                           """;

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.Select, result.StatementCategory);

        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        Assert.True(query.IsDistinct);
        Assert.Equal("10", query.TopExpressionText);
        Assert.Equal(2, query.SelectItems.Count);
        Assert.NotNull(query.MainSource);
        Assert.Contains("dbo.Users", query.MainSource!.DisplayText);
        Assert.NotNull(query.OrderBy);
        Assert.Single(query.OrderBy!.Items);
    }

    /// <summary>
    /// SELECT 項目で別名と集計関数を分解できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithAliasesAndAggregates_ReturnsSelectItemDetails()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id AS UserId,
                               SUM(o.Amount) AS TotalAmount,
                               COUNT(*) OrderCount
                           FROM dbo.Users u
                           LEFT JOIN dbo.Orders o
                               ON u.Id = o.UserId
                           GROUP BY u.Id;
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);

        Assert.Equal(3, query.SelectItems.Count);

        Assert.Equal(SelectItemKind.Expression, query.SelectItems[0].Kind);
        Assert.Equal("u.Id", query.SelectItems[0].ExpressionText);
        Assert.Equal("UserId", query.SelectItems[0].Alias);
        Assert.Null(query.SelectItems[0].AggregateFunctionName);

        Assert.Equal("SUM(o.Amount)", query.SelectItems[1].ExpressionText);
        Assert.Equal("TotalAmount", query.SelectItems[1].Alias);
        Assert.Equal("SUM", query.SelectItems[1].AggregateFunctionName);

        Assert.Equal("COUNT(*)", query.SelectItems[2].ExpressionText);
        Assert.Equal("OrderCount", query.SelectItems[2].Alias);
        Assert.Equal("COUNT", query.SelectItems[2].AggregateFunctionName);
    }

    /// <summary>
    /// SELECT * と table.* を区別して保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithWildcardColumns_ReturnsWildcardDetails()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               *,
                               u.*
                           FROM dbo.Users u;
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);

        Assert.Equal(2, query.SelectItems.Count);

        Assert.Equal(SelectItemKind.Wildcard, query.SelectItems[0].Kind);
        Assert.Equal(SelectWildcardKind.AllColumns, query.SelectItems[0].WildcardKind);
        Assert.Null(query.SelectItems[0].WildcardQualifier);

        Assert.Equal(SelectItemKind.Wildcard, query.SelectItems[1].Kind);
        Assert.Equal(SelectWildcardKind.QualifiedAllColumns, query.SelectItems[1].WildcardKind);
        Assert.Equal("u", query.SelectItems[1].WildcardQualifier);
    }

    /// <summary>
    /// JOIN 表示に必要な最小情報が正しく構築されることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithJoin_ReturnsJoinModel()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id,
                               o.OrderNo
                           FROM dbo.Users u
                           LEFT JOIN dbo.Orders o
                               ON u.Id = o.UserId;
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var join = Assert.Single(query.Joins);

        Assert.Equal(1, join.Sequence);
        Assert.Equal(JoinType.Left, join.JoinType);
        Assert.Equal("LEFT JOIN", join.JoinTypeText);
        Assert.Contains("dbo.Orders o", join.TargetSource.DisplayText);
        Assert.Equal("u.Id = o.UserId", join.OnConditionText);
    }

    /// <summary>
    /// 単純な WHERE 条件を保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithWhere_ReturnsWhereConditionText()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.IsActive = 1
                             AND u.DeletedAt IS NULL;
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);

        Assert.NotNull(query.WhereCondition);
        Assert.Contains("u.IsActive = 1", query.WhereCondition!.DisplayText);
        Assert.Contains("u.DeletedAt IS NULL", query.WhereCondition.DisplayText);
    }

    /// <summary>
    /// WHERE 句に EXISTS がある場合、判別用マーカーとサブクエリが残ることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithExists_DetectsExistsPredicate()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE EXISTS (
                               SELECT 1
                               FROM dbo.Orders o
                               WHERE o.UserId = u.Id
                           );
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = query.WhereCondition;
        Assert.NotNull(where);
        var marker = Assert.Single(where!.Markers);

        Assert.Equal(ConditionMarkerType.Exists, marker.MarkerType);
        Assert.NotNull(marker.NestedQuery);
        Assert.Single(query.Subqueries);
        Assert.Equal("WHERE句", query.Subqueries[0].Location);
    }

    /// <summary>
    /// IN と NOT IN を区別できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithInAndNotIn_DetectsBothPredicates()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.Id IN (
                               SELECT o.UserId
                               FROM dbo.Orders o
                           )
                           AND u.Status NOT IN (
                               SELECT b.StatusCode
                               FROM dbo.BlockStatuses b
                           );
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = query.WhereCondition;
        Assert.NotNull(where);

        Assert.Contains(where!.Markers, marker => marker.MarkerType == ConditionMarkerType.In);
        Assert.Contains(where.Markers, marker => marker.MarkerType == ConditionMarkerType.NotIn);
        Assert.Equal(2, query.Subqueries.Count);
    }

    /// <summary>
    /// 複雑な WHERE 条件でも AND / OR / NOT の階層を保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithNestedLogicalCondition_BuildsConditionTree()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE (u.IsActive = 1 OR u.Status = 'Gold')
                             AND NOT EXISTS (
                                 SELECT 1
                                 FROM dbo.BlockedUsers bu
                                 WHERE bu.UserId = u.Id
                             );
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = Assert.IsType<ConditionAnalysis>(query.WhereCondition);

        Assert.Equal(ConditionNodeKind.And, where.RootNode.NodeKind);
        Assert.Equal(2, where.RootNode.Children.Count);
        Assert.Equal(ConditionNodeKind.Or, where.RootNode.Children[0].NodeKind);
        Assert.All(where.RootNode.Children[0].Children, child => Assert.Equal(ConditionNodeKind.Predicate, child.NodeKind));

        var notExistsNode = where.RootNode.Children[1];
        Assert.Equal(ConditionNodeKind.Predicate, notExistsNode.NodeKind);
        Assert.NotNull(notExistsNode.Marker);
        Assert.Equal(ConditionMarkerType.NotExists, notExistsNode.Marker!.MarkerType);
    }

    /// <summary>
    /// 明示的な括弧で囲まれた条件グループを保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithParenthesizedCondition_PreservesParenthesisFlag()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE (u.IsActive = 1 OR u.Status = 'Gold')
                             AND u.DeletedAt IS NULL;
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = Assert.IsType<ConditionAnalysis>(query.WhereCondition);

        Assert.Equal(ConditionNodeKind.And, where.RootNode.NodeKind);
        Assert.False(where.RootNode.IsParenthesized);

        var groupedNode = where.RootNode.Children[0];
        Assert.Equal(ConditionNodeKind.Or, groupedNode.NodeKind);
        Assert.True(groupedNode.IsParenthesized);

        Assert.All(groupedNode.Children, child => Assert.False(child.IsParenthesized));
    }

    /// <summary>
    /// 条件式の葉ノードが主要な述語種別へ分類されることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithVariousPredicates_ClassifiesPredicateKinds()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.Id = 1
                             AND u.DeletedAt IS NULL
                             AND u.Name LIKE 'A%'
                             AND u.Score BETWEEN 1 AND 10
                             AND EXISTS (
                                 SELECT 1
                                 FROM dbo.Orders o
                                 WHERE o.UserId = u.Id
                             )
                             AND u.Id IN (
                                 SELECT p.UserId
                                 FROM dbo.PointUsers p
                             );
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = Assert.IsType<ConditionAnalysis>(query.WhereCondition);
        var predicateKinds = FlattenConditionNodes(where.RootNode)
            .Where(node => node.NodeKind == ConditionNodeKind.Predicate)
            .Select(node => node.PredicateKind)
            .ToArray();

        Assert.Contains(ConditionPredicateKind.Comparison, predicateKinds);
        Assert.Contains(ConditionPredicateKind.NullCheck, predicateKinds);
        Assert.Contains(ConditionPredicateKind.Like, predicateKinds);
        Assert.Contains(ConditionPredicateKind.Between, predicateKinds);
        Assert.Contains(ConditionPredicateKind.Exists, predicateKinds);
        Assert.Contains(ConditionPredicateKind.In, predicateKinds);
    }

    /// <summary>
    /// 比較述語では演算子の種類まで保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithComparisonOperators_ClassifiesComparisonKinds()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.Id = 1
                             AND u.Score >= 80
                             AND u.Status <> 'Deleted'
                             AND u.Level < 5;
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = Assert.IsType<ConditionAnalysis>(query.WhereCondition);
        var comparisonKinds = FlattenConditionNodes(where.RootNode)
            .Where(node => node.NodeKind == ConditionNodeKind.Predicate && node.PredicateKind == ConditionPredicateKind.Comparison)
            .Select(node => node.ComparisonKind)
            .ToArray();

        Assert.Contains(ConditionComparisonKind.Equal, comparisonKinds);
        Assert.Contains(ConditionComparisonKind.GreaterThanOrEqual, comparisonKinds);
        Assert.Contains(ConditionComparisonKind.NotEqual, comparisonKinds);
        Assert.Contains(ConditionComparisonKind.LessThan, comparisonKinds);
    }

    /// <summary>
    /// NULL 判定と BETWEEN 系では詳細種別まで保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithNullAndBetweenPredicates_ClassifiesDetailKinds()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.DeletedAt IS NULL
                             AND u.ClosedAt IS NOT NULL
                             AND u.Score BETWEEN 1 AND 10
                             AND u.Rank NOT BETWEEN 100 AND 200;
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = Assert.IsType<ConditionAnalysis>(query.WhereCondition);
        var predicateNodes = FlattenConditionNodes(where.RootNode)
            .Where(node => node.NodeKind == ConditionNodeKind.Predicate)
            .ToArray();

        Assert.Contains(predicateNodes, node => node.NullCheckKind == ConditionNullCheckKind.IsNull);
        Assert.Contains(predicateNodes, node => node.NullCheckKind == ConditionNullCheckKind.IsNotNull);
        Assert.Contains(predicateNodes, node => node.BetweenKind == ConditionBetweenKind.Between);
        Assert.Contains(predicateNodes, node => node.BetweenKind == ConditionBetweenKind.NotBetween);
    }

    /// <summary>
    /// LIKE 系では LIKE と NOT LIKE を区別できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_SelectWithLikePredicates_ClassifiesLikeKinds()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.Name LIKE 'A%'
                             AND u.Code NOT LIKE 'TMP%';
                           """;

        var result = service.Analyze(sql);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        var where = Assert.IsType<ConditionAnalysis>(query.WhereCondition);
        var predicateNodes = FlattenConditionNodes(where.RootNode)
            .Where(node => node.NodeKind == ConditionNodeKind.Predicate)
            .ToArray();

        Assert.Contains(predicateNodes, node => node.LikeKind == ConditionLikeKind.Like);
        Assert.Contains(predicateNodes, node => node.LikeKind == ConditionLikeKind.NotLike);
    }

    /// <summary>
    /// 集合演算の種類を区別できることを確認する。
    /// </summary>
    [Theory]
    [InlineData("UNION", SetOperationType.Union)]
    [InlineData("UNION ALL", SetOperationType.UnionAll)]
    [InlineData("EXCEPT", SetOperationType.Except)]
    [InlineData("INTERSECT", SetOperationType.Intersect)]
    public void Analyze_SetOperation_ReturnsExpectedOperationType(string operatorText, SetOperationType expectedType)
    {
        var service = CreateService();
        var sql = $"""
                   SELECT u.Id
                   FROM dbo.Users u
                   {operatorText}
                   SELECT a.UserId
                   FROM dbo.ArchiveUsers a;
                   """;

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.SetOperation, result.StatementCategory);

        var query = Assert.IsType<SetOperationQueryAnalysis>(result.Query);
        Assert.Equal(expectedType, query.OperationType);
        Assert.IsType<SelectQueryAnalysis>(query.LeftQuery);
        Assert.IsType<SelectQueryAnalysis>(query.RightQuery);
    }

    /// <summary>
    /// 入れ子になった集合演算でも左右の階層を保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_NestedSetOperation_PreservesHierarchy()
    {
        var service = CreateService();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           UNION ALL
                           (
                               SELECT
                                   a.UserId
                               FROM dbo.ArchiveUsers a
                               INTERSECT
                               SELECT
                                   p.UserId
                               FROM dbo.PremiumUsers p
                           );
                           """;

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.SetOperation, result.StatementCategory);

        var root = Assert.IsType<SetOperationQueryAnalysis>(result.Query);
        Assert.Equal(SetOperationType.UnionAll, root.OperationType);
        Assert.IsType<SelectQueryAnalysis>(root.LeftQuery);

        var nested = Assert.IsType<SetOperationQueryAnalysis>(root.RightQuery);
        Assert.Equal(SetOperationType.Intersect, nested.OperationType);
        Assert.IsType<SelectQueryAnalysis>(nested.LeftQuery);
        Assert.IsType<SelectQueryAnalysis>(nested.RightQuery);
    }

    /// <summary>
    /// CTE と派生テーブル JOIN を含む複雑な SELECT を保持できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_ComplexQueryWithCteAndDerivedJoin_ReturnsDetailedStructure()
    {
        var service = CreateService();
        const string sql = """
                           WITH recent_orders AS (
                               SELECT
                                   o.UserId,
                                   o.OrderId,
                                   o.OrderDate
                               FROM dbo.Orders o
                               WHERE o.OrderDate >= '2026-01-01'
                           ),
                           payment_summary AS (
                               SELECT
                                   p.UserId,
                                   COUNT(*) AS PaymentCount
                               FROM dbo.Payments p
                               GROUP BY p.UserId
                           )
                           SELECT
                               ro.UserId,
                               ps.PaymentCount
                           FROM recent_orders ro
                           INNER JOIN (
                               SELECT
                                   i.UserId,
                                   SUM(i.Amount) AS TotalAmount
                               FROM dbo.InvoiceItems i
                               GROUP BY i.UserId
                           ) invoice_total
                               ON ro.UserId = invoice_total.UserId
                           LEFT JOIN payment_summary ps
                               ON ro.UserId = ps.UserId
                           WHERE NOT EXISTS (
                               SELECT 1
                               FROM dbo.BlockedUsers bu
                               WHERE bu.UserId = ro.UserId
                           )
                           GROUP BY
                               ro.UserId,
                               ps.PaymentCount
                           HAVING COUNT(*) > 0
                           ORDER BY ro.UserId;
                           """;

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.Select, result.StatementCategory);
        Assert.Equal(2, result.CommonTableExpressions.Count);
        Assert.Equal("recent_orders", result.CommonTableExpressions[0].Name);
        Assert.Equal("payment_summary", result.CommonTableExpressions[1].Name);

        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        Assert.Equal(2, query.Joins.Count);
        Assert.Equal(JoinType.Inner, query.Joins[0].JoinType);
        Assert.NotNull(query.Joins[0].TargetSource.NestedQuery);
        Assert.Equal(JoinType.Left, query.Joins[1].JoinType);
        Assert.NotNull(query.GroupBy);
        Assert.Equal(2, query.GroupBy!.Items.Count);
        Assert.NotNull(query.HavingCondition);
        Assert.Contains("COUNT(*) > 0", query.HavingCondition!.DisplayText);
        Assert.Contains(query.WhereCondition!.Markers, marker => marker.MarkerType == ConditionMarkerType.NotExists);
        Assert.Contains(query.Subqueries, subquery => subquery.Location == "JOIN句");
        Assert.Contains(query.Subqueries, subquery => subquery.Location == "WHERE句");
    }

    /// <summary>
    /// CTE 参照が主クエリと別 CTE の両方で判別できることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_CteReferenceSource_DetectsReferenceKind()
    {
        var service = CreateService();
        const string sql = """
                           WITH base_users AS (
                               SELECT
                                   u.Id
                               FROM dbo.Users u
                           ),
                           active_users AS (
                               SELECT
                                   bu.Id
                               FROM base_users bu
                           )
                           SELECT
                               au.Id
                           FROM active_users au;
                           """;

        var result = service.Analyze(sql);

        Assert.Equal(2, result.CommonTableExpressions.Count);

        var secondCte = result.CommonTableExpressions[1];
        var secondCteQuery = Assert.IsType<SelectQueryAnalysis>(secondCte.Query);
        Assert.Equal(SourceKind.CommonTableExpressionReference, secondCteQuery.MainSource!.SourceKind);
        Assert.Equal("base_users", secondCteQuery.MainSource.SourceName);

        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        Assert.Equal(SourceKind.CommonTableExpressionReference, query.MainSource!.SourceKind);
        Assert.Equal("active_users", query.MainSource.SourceName);
    }

    /// <summary>
    /// 再帰 CTE を含む場合、自己参照を検出して補足を返すことを確認する。
    /// </summary>
    [Fact]
    public void Analyze_RecursiveCte_ReturnsSelfReferenceNotice()
    {
        var service = CreateService();
        const string sql = """
                           WITH recursive_users AS (
                               SELECT
                                   u.Id
                               FROM dbo.Users u
                               UNION ALL
                               SELECT
                                   ru.Id
                               FROM recursive_users ru
                           )
                           SELECT
                               ru.Id
                           FROM recursive_users ru;
                           """;

        var result = service.Analyze(sql);

        var cte = Assert.Single(result.CommonTableExpressions);
        var cteQuery = Assert.IsType<SetOperationQueryAnalysis>(cte.Query);
        var recursiveBranch = Assert.IsType<SelectQueryAnalysis>(cteQuery.RightQuery);
        Assert.Equal(SourceKind.CommonTableExpressionReference, recursiveBranch.MainSource!.SourceKind);
        Assert.Equal("recursive_users", recursiveBranch.MainSource.SourceName);
        Assert.Contains(result.Notices, notice => notice.Message.Contains("再帰", StringComparison.Ordinal));
    }

    /// <summary>
    /// 空入力時に例外ではなく、利用者に返せる結果になることを確認する。
    /// </summary>
    [Fact]
    public void Analyze_EmptySql_ReturnsNotice()
    {
        var service = CreateService();

        var result = service.Analyze("   ");

        Assert.Equal(QueryStatementCategory.Empty, result.StatementCategory);
        Assert.Null(result.Query);
        Assert.Contains(result.Notices, notice => notice.Level == AnalysisNoticeLevel.Warning);
    }

    /// <summary>
    /// 今回未対応の文種別は未対応として返すことを確認する。
    /// </summary>
    [Fact]
    public void Analyze_UnsupportedStatement_ReturnsUnsupportedCategory()
    {
        var service = CreateService();
        const string sql = "MERGE dbo.Users AS target USING dbo.NewUsers AS source ON target.Id = source.Id WHEN MATCHED THEN UPDATE SET target.Name = source.Name;";

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.Unsupported, result.StatementCategory);
        Assert.Null(result.Query);
        Assert.Contains(result.Notices, notice => notice.Message.Contains("未対応", StringComparison.Ordinal));
    }

    private static QueryAnalysisService CreateService()
    {
        return new QueryAnalysisService(new ScriptDomQueryAnalyzer());
    }

    private static IEnumerable<ConditionNodeAnalysis> FlattenConditionNodes(ConditionNodeAnalysis node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var nested in FlattenConditionNodes(child))
            {
                yield return nested;
            }
        }
    }
}
