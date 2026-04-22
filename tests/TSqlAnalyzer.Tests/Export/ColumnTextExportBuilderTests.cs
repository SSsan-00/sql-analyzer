using TSqlAnalyzer.Application.Analysis;
using TSqlAnalyzer.Application.Export;
using TSqlAnalyzer.Application.Services;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Tests.Export;

/// <summary>
/// 列テキストエクスポートの仕様を検証する。
/// 画面やファイル保存に依存せず、解析結果から作るテキストだけを対象にする。
/// </summary>
public sealed class ColumnTextExportBuilderTests
{
    private static readonly string Separator = new('\\', 11);

    /// <summary>
    /// SELECT では取得項目に出てくる参照列だけを、同一ブロックに並べることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForSelect_ReturnsSelectItemReferences()
    {
        var analysis = Analyze("""
                               SELECT
                                   u.Id,
                                   u.Name,
                                   u.FirstName + u.LastName AS FullName
                               FROM dbo.Users u;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Lines(
                "u.Id",
                "u.Name",
                "u.FirstName",
                "u.LastName"),
            text);
    }

    /// <summary>
    /// SELECT で参照列がない取得項目は、別名、式の順で出力することを確認する。
    /// </summary>
    [Fact]
    public void Build_ForSelectWithoutReferences_ReturnsAliasOrExpression()
    {
        var analysis = Analyze("""
                               SELECT
                                   1 AS FixedValue,
                                   'ABC' AS Label,
                                   GETDATE(),
                                   NULL
                               FROM dbo.Users u;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Lines(
                "FixedValue",
                "Label",
                "GETDATE()",
                "NULL"),
            text);
    }

    /// <summary>
    /// 派生テーブルなどの内側 SELECT は、外側 SELECT と別ブロックに分けることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForDerivedTable_ReturnsNestedSelectBlock()
    {
        var analysis = Analyze("""
                               SELECT
                                   u.Id,
                                   s.TotalAmount
                               FROM dbo.Users u
                               LEFT JOIN (
                                   SELECT
                                       o.UserId,
                                       SUM(o.Amount) AS TotalAmount
                                   FROM dbo.Orders o
                                   GROUP BY o.UserId
                               ) s
                                   ON u.Id = s.UserId;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["u.Id", "s.TotalAmount"],
                ["o.UserId", "o.Amount"]),
            text);
    }

    /// <summary>
    /// INSERT ... VALUES では挿入先列と VALUES 値を別ブロックにすることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForInsertValues_ReturnsColumnsAndValuesBlocks()
    {
        var analysis = Analyze("""
                               INSERT INTO dbo.Users (
                                   Id,
                                   Name,
                                   CreatedAt
                               )
                               VALUES (
                                   1,
                                   'Alice',
                                   GETDATE()
                               );
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["Id", "Name", "CreatedAt"],
                ["1", "'Alice'", "GETDATE()"]),
            text);
    }

    /// <summary>
    /// INSERT ... VALUES の複数行は、行ごとに値ブロックを分けることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForInsertMultipleValues_ReturnsEachValuesRowAsBlock()
    {
        var analysis = Analyze("""
                               INSERT INTO dbo.Users (
                                   Id,
                                   Name
                               )
                               VALUES
                                   (1, 'Alice'),
                                   (2, 'Bob');
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["Id", "Name"],
                ["1", "'Alice'"],
                ["2", "'Bob'"]),
            text);
    }

    /// <summary>
    /// INSERT ... SELECT では挿入先列と入力元 SELECT の取得項目参照列を別ブロックにすることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForInsertSelect_ReturnsTargetColumnsAndInputSelectBlocks()
    {
        var analysis = Analyze("""
                               INSERT INTO dbo.UserSummary (
                                   UserId,
                                   OrderCount,
                                   TotalAmount
                               )
                               SELECT
                                   u.Id,
                                   COUNT(o.Id),
                                   SUM(o.Amount)
                               FROM dbo.Users u
                               LEFT JOIN dbo.Orders o
                                   ON u.Id = o.UserId
                               GROUP BY u.Id;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["UserId", "OrderCount", "TotalAmount"],
                ["u.Id", "o.Id", "o.Amount"]),
            text);
    }

    /// <summary>
    /// INSERT の列リストが省略されている場合は、列名を推測せず省略として出すことを確認する。
    /// </summary>
    [Fact]
    public void Build_ForInsertWithoutTargetColumns_ReturnsOmittedColumnBlock()
    {
        var analysis = Analyze("""
                               INSERT INTO dbo.UserBackup
                               SELECT
                                   u.Id,
                                   u.Name,
                                   GETDATE()
                               FROM dbo.Users u;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["(挿入列省略)"],
                ["u.Id", "u.Name", "GETDATE()"]),
            text);
    }

    /// <summary>
    /// UPDATE では SET 左辺の更新列と右辺の値式を別ブロックにすることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForUpdate_ReturnsTargetColumnsAndValueExpressions()
    {
        var analysis = Analyze("""
                               UPDATE u
                               SET
                                   u.Name = s.Name,
                                   u.UpdatedAt = GETDATE(),
                                   u.Status = CASE WHEN s.IsActive = 1 THEN 'A' ELSE 'S' END
                               FROM dbo.Users u
                               INNER JOIN dbo.SourceUsers s
                                   ON u.Id = s.UserId;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["u.Name", "u.UpdatedAt", "u.Status"],
                ["s.Name", "GETDATE()", "CASE WHEN s.IsActive = 1 THEN 'A' ELSE 'S' END"]),
            text);
    }

    /// <summary>
    /// UNION では左右の SELECT を別ブロックとして区切ることを確認する。
    /// </summary>
    [Fact]
    public void Build_ForUnion_ReturnsEachSideAsBlock()
    {
        var analysis = Analyze("""
                               SELECT
                                   u.Id,
                                   u.Name
                               FROM dbo.Users u
                               UNION ALL
                               SELECT
                                   a.Id,
                                   a.Name
                               FROM dbo.ArchivedUsers a;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["u.Id", "u.Name"],
                ["a.Id", "a.Name"]),
            text);
    }

    /// <summary>
    /// CTE 定義はメイン SELECT とは別の SELECT ブロックとして出力することを確認する。
    /// </summary>
    [Fact]
    public void Build_ForCommonTableExpression_ReturnsMainAndCteBlocks()
    {
        var analysis = Analyze("""
                               WITH order_summary AS (
                                   SELECT
                                       o.UserId,
                                       SUM(o.Amount) AS TotalAmount
                                   FROM dbo.Orders o
                                   GROUP BY o.UserId
                               )
                               SELECT
                                   s.UserId,
                                   s.TotalAmount
                               FROM order_summary s;
                               """);
        var builder = new ColumnTextExportBuilder();

        var text = builder.Build(analysis);

        Assert.Equal(
            Blocks(
                ["s.UserId", "s.TotalAmount"],
                ["o.UserId", "o.Amount"]),
            text);
    }

    private static QueryAnalysisResult Analyze(string sql)
    {
        var service = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        return service.Analyze(sql);
    }

    private static string Lines(params string[] lines)
    {
        return string.Join("\r\n", lines);
    }

    private static string Blocks(params string[][] blocks)
    {
        return string.Join($"\r\n{Separator}\r\n", blocks.Select(block => Lines(block)));
    }
}
