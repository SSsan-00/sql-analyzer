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
        Assert.Contains("ON条件", flattenedTexts);
        Assert.Contains("条件 #1: u.Id = o.UserId", flattenedTexts);
        Assert.Contains("抽出条件", flattenedTexts);
    }

    /// <summary>
    /// JOIN の ON 条件が複数ある場合、条件ごとに分割表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForJoinWithMultipleConditions_SplitsOnConditions()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id,
                               o.OrderNo
                           FROM dbo.Users u
                           INNER JOIN dbo.Orders o
                               ON u.Id = o.UserId
                              AND o.IsDeleted = 0
                              AND (o.Status = 'Open' OR o.Status = 'Pending');
                           """;

        var analysis = service.Analyze(sql);
        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("ON条件", flattenedTexts);
        Assert.Contains("条件 #1: u.Id = o.UserId", flattenedTexts);
        Assert.Contains("条件 #2: o.IsDeleted = 0", flattenedTexts);
        Assert.Contains("条件 #3: (o.Status = 'Open' OR o.Status = 'Pending')", flattenedTexts);
    }

    /// <summary>
    /// SELECT 項目で別名を表示しつつ、不要な集計関数ラベルを出さないことを確認する。
    /// </summary>
    [Fact]
    public void Build_ForSelectItems_HidesAggregateLabel()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
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

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("取得項目", flattenedTexts);
        Assert.Contains("別名: UserId", flattenedTexts);
        Assert.Contains("別名: TotalAmount", flattenedTexts);
        Assert.Contains("種別: 式", flattenedTexts);
        Assert.DoesNotContain(flattenedTexts, text => text.StartsWith("集計関数:", StringComparison.Ordinal));
    }

    /// <summary>
    /// SELECT 項目や条件式で参照列ノードが表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForColumnReferenceQuery_ContainsReferenceNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id,
                               SUM(o.Amount) AS TotalAmount
                           FROM dbo.Users u
                           INNER JOIN dbo.Orders o
                               ON u.Id = o.UserId
                           WHERE o.Amount > 0
                           GROUP BY u.Id
                           HAVING SUM(o.Amount) > 100;
                           """;

        var analysis = service.Analyze(sql);
        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("参照列", flattenedTexts);
        Assert.Contains("列 #1: u.Id", flattenedTexts);
        Assert.Contains("列 #1: o.Amount", flattenedTexts);
        Assert.Contains("列 #2: o.UserId", flattenedTexts);
        Assert.Contains("解決状態: 解決済み", flattenedTexts);
        Assert.Contains("参照先: dbo.Users u", flattenedTexts);
        Assert.Contains("参照先: dbo.Orders o", flattenedTexts);
        Assert.Contains("参照別名: u", flattenedTexts);
        Assert.Contains("参照別名: o", flattenedTexts);
    }

    /// <summary>
    /// 無修飾列が単一ソースへ解決された場合、参照先が TreeView に表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForUnqualifiedColumnWithSingleSource_ContainsResolvedSource()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               Id
                           FROM dbo.Users u
                           WHERE IsActive = 1;
                           """;

        var analysis = service.Analyze(sql);
        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("列 #1: Id", flattenedTexts);
        Assert.Contains("解決状態: 解決済み", flattenedTexts);
        Assert.Contains("参照先: dbo.Users u", flattenedTexts);
        Assert.Contains("参照別名: u", flattenedTexts);
    }

    /// <summary>
    /// ORDER BY が SELECT 項目別名を参照している場合、参照先に別名情報が表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForOrderBySelectAlias_ContainsSelectAliasTarget()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               SUM(o.Amount) AS TotalAmount
                           FROM dbo.Orders o
                           ORDER BY TotalAmount;
                           """;

        var analysis = service.Analyze(sql);
        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("列 #1: TotalAmount", flattenedTexts);
        Assert.Contains("参照先: SELECT別名 TotalAmount: SUM(o.Amount)", flattenedTexts);
    }

    /// <summary>
    /// SELECT INTO の場合、INTO 先が TreeView に表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForSelectInto_ContainsIntoTargetTexts()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id,
                               u.Name
                           INTO dbo.UserSnapshot
                           FROM dbo.Users u;
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("出力先", flattenedTexts);
        Assert.Contains("dbo.UserSnapshot", flattenedTexts);
    }

    /// <summary>
    /// ワイルドカード項目で全列種別と修飾子が表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForWildcardSelectItems_ContainsWildcardTexts()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               *,
                               u.*
                           FROM dbo.Users u;
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("種別: ワイルドカード", flattenedTexts);
        Assert.Contains("全列種別: 全列", flattenedTexts);
        Assert.Contains("全列種別: 修飾付き全列", flattenedTexts);
        Assert.Contains("修飾子: u", flattenedTexts);
    }

    /// <summary>
    /// GROUP BY と ORDER BY が項目単位で表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForGroupByAndOrderBy_ContainsDetailedItems()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id,
                               u.Name
                           FROM dbo.Users u
                           GROUP BY
                               u.Id,
                               u.Name
                           ORDER BY
                               u.Name DESC,
                               u.Id;
                           """;

        var analysis = service.Analyze(sql);
        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("GROUP BY", flattenedTexts);
        Assert.Contains("項目 #1: u.Id", flattenedTexts);
        Assert.Contains("項目 #2: u.Name", flattenedTexts);
        Assert.Contains("並び順", flattenedTexts);
        Assert.Contains("項目 #1: u.Name", flattenedTexts);
        Assert.Contains("方向: DESC", flattenedTexts);
        Assert.Contains("項目 #2: u.Id", flattenedTexts);
        Assert.Contains("方向: ASC", flattenedTexts);
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

        Assert.Contains("EXISTS", flattenedTexts);
        Assert.Contains("内部クエリ", flattenedTexts);
        Assert.Contains(flattenedTexts, text => text.Contains("dbo.Orders o", StringComparison.Ordinal));
        Assert.DoesNotContain("条件種別", flattenedTexts);
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
    /// 括弧ラベルを表示せず、条件構造だけで追えることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForParenthesizedCondition_HidesParenthesisLabel()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE (u.IsActive = 1 OR u.Status = 'Gold')
                             AND u.DeletedAt IS NULL;
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("OR", flattenedTexts);
        Assert.DoesNotContain("括弧: あり", flattenedTexts);
    }

    /// <summary>
    /// 条件論理木では不要な種別ラベルを表示しないことを確認する。
    /// </summary>
    [Fact]
    public void Build_ForVariousPredicates_HidesRemovedConditionLabels()
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

        Assert.Contains("LIKE種別: LIKE", flattenedTexts);
        Assert.Contains("範囲種別: BETWEEN", flattenedTexts);
        Assert.Contains("EXISTS", flattenedTexts);
        Assert.DoesNotContain(flattenedTexts, text => text.StartsWith("述語種別:", StringComparison.Ordinal));
        Assert.DoesNotContain("条件種別", flattenedTexts);
    }

    /// <summary>
    /// 比較述語でも比較種別ラベルを表示しないことを確認する。
    /// </summary>
    [Fact]
    public void Build_ForComparisonPredicates_HidesComparisonKindTexts()
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

        Assert.DoesNotContain(flattenedTexts, text => text.StartsWith("比較種別:", StringComparison.Ordinal));
        Assert.Contains(flattenedTexts, text => text.Contains("u.Score >= 80", StringComparison.Ordinal));
    }

    /// <summary>
    /// NULL 判定種別ラベルを出さず、必要な範囲種別だけを残すことを確認する。
    /// </summary>
    [Fact]
    public void Build_ForNullAndBetweenPredicates_HidesNullKindTexts()
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

        Assert.Contains("範囲種別: BETWEEN", flattenedTexts);
        Assert.Contains("範囲種別: NOT BETWEEN", flattenedTexts);
        Assert.DoesNotContain(flattenedTexts, text => text.StartsWith("NULL判定種別:", StringComparison.Ordinal));
    }

    /// <summary>
    /// LIKE 系の詳細種別が表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForLikePredicates_ContainsLikeKindTexts()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHERE u.Name LIKE 'A%'
                             AND u.Code NOT LIKE 'TMP%';
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("LIKE種別: LIKE", flattenedTexts);
        Assert.Contains("LIKE種別: NOT LIKE", flattenedTexts);
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

    /// <summary>
    /// 集合演算ノードに左右クエリの概要が表示されることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForNestedSetOperation_ContainsSetOperationSummaryNodes()
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

        Assert.Contains("左概要", flattenedTexts);
        Assert.Contains("右概要", flattenedTexts);
        Assert.Contains("クエリ種別: SELECT", flattenedTexts);
        Assert.Contains("クエリ種別: 集合演算", flattenedTexts);
        Assert.Contains("主ソース: dbo.Users u", flattenedTexts);
        Assert.Contains("子集合演算数: 1", flattenedTexts);
        Assert.Contains("集合演算種別: INTERSECT", flattenedTexts);
    }

    /// <summary>
    /// UPDATE 文でも TreeView から対象・更新内容・JOIN・WHERE を追えることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForUpdateStatement_ContainsUpdateNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();
        const string sql = """
                           UPDATE u
                           SET
                               u.Status = o.Status,
                               u.UpdatedAt = GETDATE()
                           FROM dbo.Users u
                           INNER JOIN dbo.Orders o
                               ON u.Id = o.UserId
                           WHERE o.Status = 'Active';
                           """;

        var analysis = service.Analyze(sql);

        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.Contains("文種別: UPDATE", flattenedTexts);
        Assert.Contains("更新対象", flattenedTexts);
        Assert.Contains("更新内容", flattenedTexts);
        Assert.Contains("SET #1", flattenedTexts);
        Assert.Contains("列: u.Status", flattenedTexts);
        Assert.Contains("値: o.Status", flattenedTexts);
        Assert.Contains("結合", flattenedTexts);
        Assert.Contains("種別: INNER JOIN", flattenedTexts);
        Assert.Contains("抽出条件", flattenedTexts);
    }

    /// <summary>
    /// INSERT 文と DELETE 文でも TreeView から主要構造を追えることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForInsertAndDeleteStatements_ContainsDmlNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();

        const string insertSql = """
                                 INSERT INTO dbo.UserSummary (UserId, OrderCount)
                                 SELECT
                                     u.Id,
                                     COUNT(*) AS OrderCount
                                 FROM dbo.Users u
                                 GROUP BY u.Id;
                                 """;

        var insertAnalysis = service.Analyze(insertSql);
        var insertTree = builder.Build(insertAnalysis);
        var insertTexts = Flatten(insertTree).ToArray();

        Assert.Contains("文種別: INSERT", insertTexts);
        Assert.Contains("挿入対象", insertTexts);
        Assert.Contains("挿入列", insertTexts);
        Assert.Contains("入力元", insertTexts);
        Assert.Contains("列と値の対応", insertTexts);
        Assert.Contains("対応 #1: UserId <= u.Id", insertTexts);
        Assert.Contains("対応 #2: OrderCount <= COUNT(*)", insertTexts);
        Assert.Contains("種別: SELECTクエリ", insertTexts);
        Assert.Contains("内部クエリ", insertTexts);

        const string deleteSql = """
                                 DELETE u
                                 FROM dbo.Users u
                                 LEFT JOIN dbo.Orders o
                                     ON u.Id = o.UserId
                                 WHERE o.UserId IS NULL;
                                 """;

        var deleteAnalysis = service.Analyze(deleteSql);
        var deleteTree = builder.Build(deleteAnalysis);
        var deleteTexts = Flatten(deleteTree).ToArray();

        Assert.Contains("文種別: DELETE", deleteTexts);
        Assert.Contains("削除対象", deleteTexts);
        Assert.Contains("結合", deleteTexts);
        Assert.Contains("種別: LEFT JOIN", deleteTexts);
        Assert.Contains("抽出条件", deleteTexts);
    }

    /// <summary>
    /// 不要な注意点ノードを表示せず、未対応文言も短く保つことを確認する。
    /// </summary>
    [Fact]
    public void Build_ForUnsupportedStatement_HidesNoticeNodeAndShortensMessage()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();

        var analysis = service.Analyze("MERGE dbo.Users AS target USING dbo.NewUsers AS source ON target.Id = source.Id WHEN MATCHED THEN UPDATE SET target.Name = source.Name;");
        var tree = builder.Build(analysis);
        var flattenedTexts = Flatten(tree).ToArray();

        Assert.DoesNotContain("注意点", flattenedTexts);
        Assert.Contains("未対応の文種別です。", flattenedTexts);
        Assert.DoesNotContain(flattenedTexts, text => text.Contains("初期版では", StringComparison.Ordinal));
    }

    /// <summary>
    /// CREATE VIEW と CREATE TABLE でも TreeView から主要構造を追えることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForCreateStatements_ContainsCreateNodes()
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var builder = new QueryAnalysisTreeBuilder();

        const string createViewSql = """
                                     CREATE VIEW dbo.ActiveUsers
                                     AS
                                     SELECT
                                         u.Id,
                                         u.Name
                                     FROM dbo.Users u
                                     WHERE u.IsActive = 1;
                                     """;

        var createViewAnalysis = service.Analyze(createViewSql);
        var createViewTree = builder.Build(createViewAnalysis);
        var createViewTexts = Flatten(createViewTree).ToArray();

        Assert.Contains("文種別: CREATE", createViewTexts);
        Assert.Contains("作成対象", createViewTexts);
        Assert.Contains("種別: VIEW", createViewTexts);
        Assert.Contains("名前: dbo.ActiveUsers", createViewTexts);
        Assert.Contains("内部クエリ", createViewTexts);

        const string createTableSql = """
                                      CREATE TABLE dbo.Users (
                                          Id INT NOT NULL,
                                          Name NVARCHAR(100) NULL
                                      );
                                      """;

        var createTableAnalysis = service.Analyze(createTableSql);
        var createTableTree = builder.Build(createTableAnalysis);
        var createTableTexts = Flatten(createTableTree).ToArray();

        Assert.Contains("種別: TABLE", createTableTexts);
        Assert.Contains("列定義", createTableTexts);
        Assert.Contains("列 #1: Id", createTableTexts);
        Assert.Contains("データ型: INT", createTableTexts);
        Assert.Contains("NULL許可: なし", createTableTexts);
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
