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
