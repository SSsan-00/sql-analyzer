using TSqlAnalyzer.Application.Editor;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Tests.Editor;

/// <summary>
/// 構文エラー位置からエディター強調範囲を求める処理を検証する。
/// UI に依存せず、行・列から適切な文字範囲が引けるかだけを確認する。
/// </summary>
public sealed class ParseIssueTextSpanResolverTests
{
    /// <summary>
    /// 複数行 SQL の行・列情報から、対象トークン全体を取り出せることを確認する。
    /// </summary>
    [Fact]
    public void TryResolve_ForMultilineSql_ReturnsTokenSpan()
    {
        const string sql = """
                           SELECT
                               u.Id
                           FROM dbo.Users u
                           WHER u.Name = 'Alice';
                           """;
        var resolver = new ParseIssueTextSpanResolver();
        var issue = CreateIssueAt(sql, "WHER");

        var resolved = resolver.TryResolve(sql, issue, out var span);

        Assert.True(resolved);
        Assert.Equal("WHER", sql.Substring(span.Start, span.Length));
    }

    /// <summary>
    /// 修飾子付き列名の途中が指された場合でも、列参照全体を強調できることを確認する。
    /// </summary>
    [Fact]
    public void TryResolve_ForQualifiedName_ReturnsQualifiedTokenSpan()
    {
        const string sql = "SELECT * FROM dbo.Users u WHERE u.Nam = 1;";
        var resolver = new ParseIssueTextSpanResolver();
        var issue = CreateIssueAt(sql, "u.Nam");

        var resolved = resolver.TryResolve(sql, issue, out var span);

        Assert.True(resolved);
        Assert.Equal("u.Nam", sql.Substring(span.Start, span.Length));
    }

    /// <summary>
    /// 行・列が文字列範囲外なら失敗扱いになることを確認する。
    /// </summary>
    [Fact]
    public void TryResolve_ForOutOfRangeLocation_ReturnsFalse()
    {
        const string sql = "SELECT 1;";
        var resolver = new ParseIssueTextSpanResolver();

        var resolved = resolver.TryResolve(sql, new ParseIssue(4, 1, "dummy"), out _);

        Assert.False(resolved);
    }

    /// <summary>
    /// テスト用に、指定文字列の先頭位置から行・列を作る。
    /// </summary>
    private static ParseIssue CreateIssueAt(string sql, string token)
    {
        var index = sql.IndexOf(token, StringComparison.Ordinal);
        Assert.True(index >= 0);

        var line = 1;
        var column = 1;
        for (var position = 0; position < index; position++)
        {
            if (sql[position] == '\r')
            {
                if (position + 1 < sql.Length && sql[position + 1] == '\n')
                {
                    position++;
                }

                line++;
                column = 1;
                continue;
            }

            if (sql[position] == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return new ParseIssue(line, column, "dummy");
    }
}
