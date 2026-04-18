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
}
