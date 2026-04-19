using TSqlAnalyzer.Application.Analysis;
using TSqlAnalyzer.Application.Presentation;
using TSqlAnalyzer.Application.Services;

namespace TSqlAnalyzer.Tests.Presentation;

/// <summary>
/// TreeView 用モデルへの変換を固定するテスト。
/// 実際の WinForms TreeNode は使わず、UI 非依存モデルを検証する。
/// </summary>
public sealed class QueryAnalysisTreeBuilderTests
{
    /// <summary>
    /// JOIN を含む解析結果が、利用者に読みやすい木構造へ落ちることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForJoinQuery_ContainsExpectedTexts()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id,
                               o.OrderNo
                           FROM dbo.Users u
                           LEFT JOIN dbo.Orders o
                               ON u.Id = o.UserId
                           WHERE o.Amount > 0;
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("クエリ解析結果", flattenedTexts);
        Assert.Contains("主構造", flattenedTexts);
        Assert.Contains("取得項目", flattenedTexts);
        Assert.Contains("主テーブル", flattenedTexts);
        Assert.Contains("結合", flattenedTexts);
        Assert.Contains("JOIN #1", flattenedTexts);
        Assert.Contains("種別: LEFT JOIN", flattenedTexts);
        Assert.Contains("結合先: dbo.Orders o", flattenedTexts);
        Assert.Contains("ON条件: u.Id = o.UserId", flattenedTexts);
        Assert.Contains("抽出条件", flattenedTexts);
    }

    /// <summary>
    /// 空入力時も TreeView に返せる最低限のノードが生成されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForEmptyInput_ReturnsNoticeTree()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();

        var analysis = service.Analyze(string.Empty);
        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("クエリ解析結果", flattenedTexts);
        Assert.Contains(flattenedTexts, text => text.Contains("入力", StringComparison.Ordinal));
    }

    /// <summary>
    /// CTE と派生テーブル JOIN を含む場合、追いやすい補助ノードが生成されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForComplexQuery_ContainsCteAndNestedSourceNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           WITH recent_orders AS (
                               SELECT
                                   o.UserId,
                                   o.OrderId
                               FROM dbo.Orders o
                           )
                           SELECT
                               ro.UserId,
                               invoice_total.TotalAmount
                           FROM recent_orders ro
                           INNER JOIN (
                               SELECT
                                   i.UserId,
                                   SUM(i.Amount) AS TotalAmount
                               FROM dbo.InvoiceItems i
                               GROUP BY i.UserId
                           ) invoice_total
                               ON ro.UserId = invoice_total.UserId;
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("共通テーブル式", flattenedTexts);
        Assert.Contains("CTE #1: recent_orders", flattenedTexts);
        Assert.Contains("JOIN #1", flattenedTexts);
        Assert.Contains("結合先の内部構造", flattenedTexts);
        Assert.Contains("内部構造", flattenedTexts);
        Assert.Contains(flattenedTexts, text => text.Contains("SUM(i.Amount)", StringComparison.Ordinal));
    }

    /// <summary>
    /// CTE 参照関係が TreeView から読み取れることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForCteReferenceQuery_ContainsReferenceSummary()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
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

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("参照関係", flattenedTexts);
        Assert.Contains("メインクエリ: active_users", flattenedTexts);
        Assert.Contains("CTE active_users: base_users", flattenedTexts);
        Assert.Contains("依存順", flattenedTexts);
        Assert.Contains("手順 1: base_users", flattenedTexts);
        Assert.Contains("手順 2: active_users", flattenedTexts);
        Assert.Contains("種別: CTE参照", flattenedTexts);
    }

    /// <summary>
    /// 再帰 CTE がある場合、依存順ノードで循環が見えることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForRecursiveCte_ContainsCycleNode()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
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

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("依存順", flattenedTexts);
        Assert.Contains("循環あり: recursive_users", flattenedTexts);
    }

    /// <summary>
    /// 条件マーカー配下から EXISTS の内部クエリを辿れることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForExistsCondition_ContainsMarkerNestedQueryNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE EXISTS (
                               SELECT
                                   1
                               FROM dbo.Orders o
                               WHERE o.UserId = u.Id
                           );
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("条件種別", flattenedTexts);
        Assert.Contains("条件 #1", flattenedTexts);
        Assert.Contains("種別: EXISTS", flattenedTexts);
        Assert.Contains("内部クエリ", flattenedTexts);
        Assert.Contains(flattenedTexts, text => text.Contains("dbo.Orders o", StringComparison.Ordinal));
    }

    /// <summary>
    /// 論理条件が入れ子の場合、条件論理ノードから構造を追えることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForNestedLogicalCondition_ContainsConditionTreeNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
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

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("条件論理", flattenedTexts);
        Assert.Contains("AND", flattenedTexts);
        Assert.Contains("OR", flattenedTexts);
        Assert.Contains("NOT EXISTS", flattenedTexts);
        Assert.Contains(flattenedTexts, text => text.Contains("u.IsActive = 1", StringComparison.Ordinal));
        Assert.Contains(flattenedTexts, text => text.Contains("dbo.BlockedUsers bu", StringComparison.Ordinal));
    }

    /// <summary>
    /// 条件論理木の葉ノードに述語種別が表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForVariousPredicates_ContainsPredicateKindTexts()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
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
                             );
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("述語種別: 比較", flattenedTexts);
        Assert.Contains("述語種別: NULL判定", flattenedTexts);
        Assert.Contains("述語種別: LIKE", flattenedTexts);
        Assert.Contains("述語種別: BETWEEN", flattenedTexts);
        Assert.Contains("述語種別: EXISTS", flattenedTexts);
    }

    /// <summary>
    /// 比較述語では比較演算子の種類も表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForComparisonPredicates_ContainsComparisonKindTexts()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.Id = 1
                             AND u.Score >= 80
                             AND u.Status <> 'Deleted';
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("比較種別: 等価 (=)", flattenedTexts);
        Assert.Contains("比較種別: 以上 (>=)", flattenedTexts);
        Assert.Contains("比較種別: 不等価 (<>)", flattenedTexts);
    }

    /// <summary>
    /// NULL 判定と BETWEEN 系の詳細種別が表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForNullAndBetweenPredicates_ContainsDetailKindTexts()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.DeletedAt IS NULL
                             AND u.ClosedAt IS NOT NULL
                             AND u.Score BETWEEN 1 AND 10
                             AND u.Rank NOT BETWEEN 100 AND 200;
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("NULL判定種別: IS NULL", flattenedTexts);
        Assert.Contains("NULL判定種別: IS NOT NULL", flattenedTexts);
        Assert.Contains("範囲種別: BETWEEN", flattenedTexts);
        Assert.Contains("範囲種別: NOT BETWEEN", flattenedTexts);
    }

    /// <summary>
    /// 入れ子の集合演算でも種別を追える TreeView になることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForNestedSetOperation_ContainsRecursiveOperationNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
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

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("集合演算", flattenedTexts);
        Assert.Contains("種別: UNION ALL", flattenedTexts);
        Assert.Contains("右クエリ", flattenedTexts);
        Assert.Contains("種別: INTERSECT", flattenedTexts);
        Assert.Contains(flattenedTexts, text => text.Contains("dbo.PremiumUsers", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Flatten(DisplayTreeNode node)
    {
        yield return node.Text;

        foreach (var child in node.Children)
        {
            foreach (var text in Flatten(child))
            {
                yield return text;
            }
        }
    }
}
