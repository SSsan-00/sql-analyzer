using TSqlAnalyzer.Application.Editor;

namespace TSqlAnalyzer.Tests.Editor;

/// <summary>
/// DB 接続なし入力補助の候補生成を検証する。
/// キーワードだけでなく、現在の SQL から拾った CTE 名や別名も返せることを確認する。
/// </summary>
public sealed class SqlInputAssistServiceTests
{
    /// <summary>
    /// 空接頭辞では代表的なキーワードが候補に含まれることを確認する。
    /// </summary>
    [Fact]
    public void GetSuggestions_ForEmptyPrefix_IncludesCommonKeywords()
    {
        var service = new SqlInputAssistService();

        var result = service.GetSuggestions(string.Empty, 0);

        Assert.Contains(result.Items, item => item.DisplayText == "SELECT");
        Assert.Contains(result.Items, item => item.DisplayText == "FROM");
        Assert.Contains(result.Items, item => item.DisplayText == "WHERE");
    }

    /// <summary>
    /// 現在の SQL に出てくる CTE 名、ソース別名、SELECT 別名が候補へ含まれることを確認する。
    /// </summary>
    [Fact]
    public void GetSuggestions_ForLocalSymbols_IncludesCteAliasAndSelectAlias()
    {
        const string sql = """
                           WITH recent_orders AS (
                               SELECT
                                   u.Id AS UserId
                               FROM dbo.Users u
                           )
                           SELECT
                               *
                           FROM recent_orders ro
                           ORDER BY U
                           """;
        var service = new SqlInputAssistService();
        var caretIndex = sql.LastIndexOf('U') + 1;

        var result = service.GetSuggestions(sql, caretIndex);

        Assert.Equal(caretIndex - 1, result.ReplaceStart);
        Assert.Equal(1, result.ReplaceLength);
        Assert.Contains(result.Items, item => item.DisplayText == "UserId");
        Assert.Contains(result.Items, item => item.DisplayText == "u");
    }

    /// <summary>
    /// `alias.` 補完では、その別名で既に使った列だけを候補へ出すことを確認する。
    /// </summary>
    [Fact]
    public void GetSuggestions_ForQualifiedPrefix_ReturnsColumnsForQualifierOnly()
    {
        const string sql = """
                           SELECT
                               u.Id,
                               u.Name,
                               o.OrderId
                           FROM dbo.Users u
                           INNER JOIN dbo.Orders o
                               ON u.Id = o.UserId
                           WHERE u.Na
                           """;
        var service = new SqlInputAssistService();
        var caretIndex = sql.Length;

        var result = service.GetSuggestions(sql, caretIndex);

        Assert.Equal(caretIndex - 2, result.ReplaceStart);
        Assert.Equal(2, result.ReplaceLength);
        Assert.Contains(result.Items, item => item.DisplayText == "Name");
        Assert.DoesNotContain(result.Items, item => item.DisplayText == "OrderId");
        Assert.All(result.Items, item => Assert.Equal(SqlInputAssistItemKind.Column, item.Kind));
    }
}
