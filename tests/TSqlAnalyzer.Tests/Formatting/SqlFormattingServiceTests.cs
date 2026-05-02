using System.Text.RegularExpressions;
using TSqlAnalyzer.Application.Formatting;

namespace TSqlAnalyzer.Tests.Formatting;

/// <summary>
/// SQL 整形の基本仕様を固定するテスト。
/// 空白の細部まで縛り過ぎず、読みやすさに効く改行位置と失敗時の扱いを確認する。
/// </summary>
public sealed class SqlFormattingServiceTests
{
    /// <summary>
    /// SELECT 系の主要句が改行され、キーワードが大文字で揃うことを確認する。
    /// </summary>
    [Fact]
    public void Format_SelectQuery_ReturnsReadableMultilineSql()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            "select u.id,u.name from dbo.Users u left join dbo.Orders o on u.Id=o.UserId where u.IsActive=1 order by u.Name");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ParseIssues);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("^SELECT\\s+", formatted);
        Assert.Matches("\\n\\s*FROM\\s+", formatted);
        Assert.Matches("\\n\\s*LEFT(?:\\s+OUTER)?\\s+JOIN\\s+", formatted);
        Assert.Matches("\\n\\s*WHERE\\s+", formatted);
        Assert.Matches("\\n\\s*ORDER\\s+BY\\s+", formatted);
        Assert.Contains("u.id", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(";", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// UPDATE の SET 句は項目ごとに改行され、WHERE 句も見やすく分離されることを確認する。
    /// </summary>
    [Fact]
    public void Format_UpdateQuery_BreaksSetItemsAcrossLines()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            "update dbo.Users set Name='Alice',UpdatedAt=getdate(),UpdatedBy='system' where Id=1");

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\n\\s*SET\\s+", formatted);
        Assert.Contains(",\n", formatted, StringComparison.Ordinal);
        Assert.Matches("\\n\\s*WHERE\\s+", formatted);
        Assert.EndsWith(";", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// INSERT の列一覧と VALUES 一覧も複数行に開かれることを確認する。
    /// </summary>
    [Fact]
    public void Format_InsertQuery_BreaksTargetsAndValuesAcrossLines()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            "insert into dbo.Users(Id,Name,CreatedAt) values(1,'Alice',getdate())");

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("^INSERT\\s+INTO\\s+", formatted);
        Assert.Matches("\\n\\s*VALUES\\s*", formatted);
        Assert.Contains("GETDATE", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(";", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// 空入力はそのまま成功扱いにし、不要なエラーを出さないことを確認する。
    /// </summary>
    [Fact]
    public void Format_EmptyInput_ReturnsEmptyText()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format("  ");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ParseIssues);
        Assert.Equal(string.Empty, result.FormattedSql);
    }

    /// <summary>
    /// 構文エラーがある SQL は整形せず、元の文字列とエラー情報を返すことを確認する。
    /// </summary>
    [Fact]
    public void Format_InvalidSql_ReturnsFailureAndOriginalSql()
    {
        var formatter = new SqlFormattingService();
        const string sql = "SELECT FROM WHERE";

        var result = formatter.Format(sql);

        Assert.False(result.IsSuccess);
        Assert.Equal(sql, result.FormattedSql);
        Assert.NotEmpty(result.ParseIssues);
    }

    /// <summary>
    /// T-SQL として構文解析できない PostgreSQL SELECT は、PostgreSQL として整形できることを確認する。
    /// </summary>
    [Fact]
    public void Format_PostgreSqlSelectWithPublicSchemaAndLimit_ReturnsReadableMultilineSql()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            "select id,name from public.users where active = true order by name limit 10 offset 20");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ParseIssues);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("^SELECT\\s+", formatted);
        Assert.Matches("\\nFROM\\s+public\\.users", formatted);
        Assert.Matches("\\nWHERE\\n\\s+active\\s*=\\s*true", formatted);
        Assert.Matches("\\nORDER\\s+BY\\n\\s+name", formatted);
        Assert.Matches("\\nLIMIT\\s+10", formatted);
        Assert.Matches("\\nOFFSET\\s+20", formatted);
        Assert.EndsWith(";", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// PostgreSQL の RETURNING 付き INSERT も整形できることを確認する。
    /// </summary>
    [Fact]
    public void Format_PostgreSqlInsertReturning_ReturnsReadableMultilineSql()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            "insert into public.users (id, name) values (1, 'Ada') returning id, name");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ParseIssues);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("^INSERT\\s+INTO\\s+public\\.users\\s*\\(", formatted);
        Assert.Matches("\\n\\s+id,", formatted);
        Assert.Matches("\\nVALUES\\n\\s+\\(1, 'Ada'\\)", formatted);
        Assert.Matches("\\nRETURNING\\n\\s+id,", formatted);
        Assert.Contains("name", formatted, StringComparison.Ordinal);
        Assert.EndsWith(";", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// ILIKE、PostgreSQL キャスト、interval などの固有構文も整形できることを確認する。
    /// </summary>
    [Fact]
    public void Format_PostgreSqlSpecificOperators_ReturnsReadableMultilineSql()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            "select id from users where name ilike '%ada%' and created_at::date >= current_date - interval '7 days'");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ParseIssues);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\nWHERE\\n", formatted);
        Assert.Matches("\\n\\s+name\\s+ILIKE\\s+'%ada%'", formatted);
        Assert.Matches("\\n\\s+AND\\s+created_at::date\\s+>=", formatted);
        Assert.Contains("'7 days'::interval", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(";", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// 複雑な SELECT では CTE、JOIN、ON 条件、WHERE 条件、ORDER BY の階層が見えることを確認する。
    /// </summary>
    [Fact]
    public void Format_ComplexSelectQuery_EmphasizesHierarchy()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            with sales as (select o.UserId,sum(o.Amount) as TotalAmount from dbo.Orders o where o.Status='Paid' and o.Amount>0 group by o.UserId)
            select u.Id,u.Name,case when s.TotalAmount>=1000 then 'VIP' when s.TotalAmount>0 then 'NORMAL' else 'NONE' end as UserRank
            from dbo.Users u left join sales s on u.Id=s.UserId and s.TotalAmount>0
            where exists(select 1 from dbo.Payments p where p.UserId=u.Id and p.Result='OK') and u.IsActive=1
            order by u.Name
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("^WITH\\s+", formatted);
        Assert.Matches("\\n\\s{4}SELECT\\s+", formatted);
        Assert.Matches("\\nFROM\\s+", formatted);
        Assert.Matches("\\n\\s{4}LEFT(?:\\s+OUTER)?\\s+JOIN\\s+", formatted);
        Assert.Matches("\\n\\s{8}ON\\s+", formatted);
        Assert.Matches("\\n\\s{12}AND\\s+", formatted);
        Assert.Matches("\\nWHERE\\s+", formatted);
        Assert.Matches("\\n\\s{4}EXISTS\\s*\\(", formatted);
        Assert.Matches("\\n\\s{8}SELECT\\s+1", formatted);
        Assert.Matches("\\n\\s{4}AND\\s+u\\.IsActive\\s*=\\s*1", formatted);
        Assert.Matches("\\nORDER\\s+BY\\s+", formatted);
    }

    /// <summary>
    /// CASE 式と集合演算は階層ごとに改行され、読み流しやすいことを確認する。
    /// </summary>
    [Fact]
    public void Format_CaseAndSetOperationQuery_BreaksByLogicalBlocks()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select case when u.Status='A' then 'Active' when u.Status='S' then 'Stopped' else 'Unknown' end as StatusName from dbo.Users u
            union all
            select case when a.IsDeleted=1 then 'Deleted' else 'Archived' end as StatusName from dbo.ArchiveUsers a
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\n\\s{4}CASE\\b", formatted);
        Assert.Matches("\\n\\s{8}WHEN\\s+u\\.Status\\s*=\\s*'A'\\n", formatted);
        Assert.Matches("\\n\\s{12}THEN\\s+'Active'", formatted);
        Assert.Matches("\\n\\s{8}ELSE\\n", formatted);
        Assert.Matches("\\n\\s{12}'Unknown'", formatted);
        Assert.Matches("\\n\\s{4}END\\s+AS\\s+StatusName", formatted);
        Assert.Matches("\\nUNION\\s+ALL\\n", formatted);
        Assert.Equal(2, Regex.Matches(formatted, "(^|\\n)SELECT\\b", RegexOptions.Multiline).Count);
    }

    /// <summary>
    /// WHERE と JOIN ON の括弧付き論理グループが、段付きブロックとして読めることを確認する。
    /// </summary>
    [Fact]
    public void Format_LogicalGroups_ExpandsParenthesizedConditions()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select u.Id,u.Name
            from dbo.Users u
            left join dbo.Orders o on u.Id=o.UserId and (o.Status='Paid' or o.Status='Shipped')
            where u.IsActive=1 and (u.Type='Retail' or u.Type='Wholesale')
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\n\\s{8}ON\\n", formatted);
        Assert.Matches("\\n\\s{12}u\\.Id\\s*=\\s*o\\.UserId", formatted);
        Assert.Matches("\\n\\s{12}AND\\s*\\(", formatted);
        Assert.Matches("\\n\\s{16}o\\.Status\\s*=\\s*'Paid'", formatted);
        Assert.Matches("\\n\\s{16}OR\\s+o\\.Status\\s*=\\s*'Shipped'", formatted);
        Assert.Matches("\\n\\s{12}\\)", formatted);
        Assert.Matches("\\nWHERE\\n", formatted);
        Assert.Matches("\\n\\s{4}u\\.IsActive\\s*=\\s*1", formatted);
        Assert.Matches("\\n\\s{4}AND\\s*\\(", formatted);
        Assert.Matches("\\n\\s{8}u\\.Type\\s*=\\s*'Retail'", formatted);
        Assert.Matches("\\n\\s{8}OR\\s+u\\.Type\\s*=\\s*'Wholesale'", formatted);
        Assert.Matches("\\n\\s{4}\\)", formatted);
    }

    /// <summary>
    /// 派生テーブルと SELECT 項目のスカラーサブクエリも、入れ子構造が追える形で整形されることを確認する。
    /// </summary>
    [Fact]
    public void Format_DerivedTableAndScalarSubquery_ExpandsNestedQueries()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select u.Id,(select max(p.CreatedAt) from dbo.Payments p where p.UserId=u.Id and p.Result='OK') as LastPaymentAt
            from dbo.Users u
            left join (select o.UserId,sum(o.Amount) as TotalAmount from dbo.Orders o where o.Amount>0 group by o.UserId) totals on u.Id=totals.UserId
            where u.IsActive=1
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\n\\s{4}\\(", formatted);
        Assert.Matches("\\n\\s{8}SELECT\\n", formatted);
        Assert.Matches("(?i)\\n\\s+max\\s*\\(p\\.CreatedAt\\)", formatted);
        Assert.Matches("\\n\\s{4}\\)\\s+AS\\s+LastPaymentAt", formatted);
        Assert.Matches("\\n\\s{4}LEFT(?:\\s+OUTER)?\\s+JOIN\\s+\\(", formatted);
        Assert.Matches("\\n\\s{8}SELECT\\n", formatted);
        Assert.Matches("\\n\\s{8}GROUP\\s+BY\\n", formatted);
        Assert.Matches("\\n\\s{4}\\)\\s+totals", formatted);
    }

    /// <summary>
    /// CASE を含む比較条件は、CASE 本体を複数行で開いたまま比較できることを確認する。
    /// </summary>
    [Fact]
    public void Format_CaseInJoinAndWhereCondition_ExpandsCaseBlocks()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select u.Id
            from dbo.Users u
            left join dbo.Orders o on case when o.Amount>0 then o.UserId else null end=u.Id
            where case when u.Status='A' then 1 else 0 end=1
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\n\\s{8}ON\\n", formatted);
        Assert.Matches("\\n\\s{12}CASE\\n", formatted);
        Assert.Matches("\\n\\s{16}WHEN\\s+o\\.Amount\\s*>\\s*0\\n", formatted);
        Assert.Matches("\\n\\s{20}THEN\\s+o\\.UserId", formatted);
        Assert.Matches("\\n\\s{16}ELSE\\n", formatted);
        Assert.Matches("\\n\\s{20}NULL", formatted);
        Assert.Matches("\\n\\s{12}END\\s*=\\s*u\\.Id", formatted);
        Assert.Matches("\\nWHERE\\n", formatted);
        Assert.Matches("\\n\\s{4}CASE\\n", formatted);
        Assert.Matches("\\n\\s{8}WHEN\\s+u\\.Status\\s*=\\s*'A'\\n", formatted);
        Assert.Matches("\\n\\s{12}THEN\\s+1", formatted);
        Assert.Matches("\\n\\s{8}ELSE\\n", formatted);
        Assert.Matches("\\n\\s{12}0", formatted);
        Assert.Matches("\\n\\s{4}END\\s*=\\s*1", formatted);
    }

    /// <summary>
    /// CASE の WHEN 条件に AND / OR が含まれる場合は、条件部分だけを段落化して追いやすくする。
    /// </summary>
    [Fact]
    public void Format_CaseWhenConditionWithLogicalOperators_BreaksConditionIntoParagraphs()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select case when u.IsActive=1 and (u.Type='Retail' or u.Type='Wholesale') and exists(select 1 from dbo.Orders o where o.UserId=u.Id and o.Status='Open') then 'Target' else 'Other' end as Segment
            from dbo.Users u
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\n\\s{8}WHEN\\n", formatted);
        Assert.Matches("\\n\\s{12}u\\.IsActive\\s*=\\s*1", formatted);
        Assert.Matches("\\n\\s{12}AND\\s*\\(", formatted);
        Assert.Matches("\\n\\s{16}u\\.Type\\s*=\\s*'Retail'", formatted);
        Assert.Matches("\\n\\s{16}OR\\s+u\\.Type\\s*=\\s*'Wholesale'", formatted);
        Assert.Matches("\\n\\s{12}\\)", formatted);
        Assert.Matches("\\n\\s{12}AND\\s+EXISTS\\s*\\(", formatted);
        Assert.Matches("\\n\\s{16}SELECT\\s+1\\n", formatted);
        Assert.Matches("\\n\\s{12}\\)", formatted);
        Assert.Matches("\\n\\s{12}THEN\\s+'Target'", formatted);
    }

    /// <summary>
    /// 関数の引数にサブクエリがある場合も、関数ブロックとして読みやすく展開されることを確認する。
    /// </summary>
    [Fact]
    public void Format_FunctionCallWithScalarSubquery_ExpandsFunctionArguments()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select isnull((select max(p.CreatedAt) from dbo.Payments p where p.UserId=u.Id),u.CreatedAt) as EffectiveCreatedAt
            from dbo.Users u
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("(?i)\\n\\s{4}isnull\\s*\\(", formatted);
        Assert.Matches("\\n\\s{8}\\(", formatted);
        Assert.Matches("\\n\\s{12}SELECT\\n", formatted);
        Assert.Matches("(?i)\\n\\s+max\\s*\\(p\\.CreatedAt\\)", formatted);
        Assert.Matches("\\n\\s{8}\\),", formatted);
        Assert.Matches("\\n\\s{8}u\\.CreatedAt", formatted);
        Assert.Matches("\\n\\s{4}\\)\\s+AS\\s+EffectiveCreatedAt", formatted);
    }

    /// <summary>
    /// LIKE 条件で CASE と複雑関数が組み合わさる場合も、左右の階層を崩さず整形できることを確認する。
    /// </summary>
    [Fact]
    public void Format_LikePredicateWithCaseAndFunction_ExpandsBothSides()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select u.Id
            from dbo.Users u
            where case when u.Status='A' then u.Name else u.Code end like isnull((select top 1 p.Pattern from dbo.Patterns p where p.UserId=u.Id order by p.Priority),u.Name)
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\nWHERE\\n", formatted);
        Assert.Matches("\\n\\s{4}CASE\\n", formatted);
        Assert.Matches("\\n\\s{8}WHEN\\s+u\\.Status\\s*=\\s*'A'\\n", formatted);
        Assert.Matches("\\n\\s{12}THEN\\s+u\\.Name", formatted);
        Assert.Matches("\\n\\s{4}END\\s+LIKE\\n", formatted);
        Assert.Matches("(?i)\\n\\s{8}isnull\\s*\\(", formatted);
        Assert.Matches("\\n\\s{12}\\(", formatted);
        Assert.Matches("\\n\\s{16}SELECT\\s+TOP\\s+1\\n", formatted);
        Assert.Matches("\\n\\s{8}\\)\\s*$|\\n\\s{8}\\)\\s*;", formatted);
    }

    /// <summary>
    /// BETWEEN 条件で境界値が複雑な式でも、BETWEEN と AND の区切りが多段で見えることを確認する。
    /// </summary>
    [Fact]
    public void Format_BetweenPredicateWithComplexBounds_ExpandsRangeBlocks()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select u.Id
            from dbo.Users u
            where case when u.IsVip=1 then u.Points else u.TempPoints end between coalesce((select min(l.MinPoint) from dbo.PointLimits l where l.UserId=u.Id),0) and isnull((select max(l.MaxPoint) from dbo.PointLimits l where l.UserId=u.Id),9999)
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\nWHERE\\n", formatted);
        Assert.Matches("\\n\\s{4}CASE\\n", formatted);
        Assert.Matches("\\n\\s{8}WHEN\\s+u\\.IsVip\\s*=\\s*1\\n", formatted);
        Assert.Matches("\\n\\s{12}THEN\\s+u\\.Points", formatted);
        Assert.Matches("\\n\\s{4}END\\s+BETWEEN\\n", formatted);
        Assert.Matches("(?i)\\n\\s{8}coalesce\\s*\\(", formatted);
        Assert.Matches("\\n\\s{16}SELECT\\n", formatted);
        Assert.Matches("(?i)\\n\\s{8}AND\\s+isnull\\s*\\(", formatted);
        Assert.Matches("\\n\\s{16}SELECT\\n", formatted);
    }

    /// <summary>
    /// IS NULL 条件で対象式が複雑関数の場合も、関数本体を展開した上で NULL 判定を末尾へ付けられることを確認する。
    /// </summary>
    [Fact]
    public void Format_IsNullPredicateWithComplexExpression_AppendsNullCheckToExpandedBlock()
    {
        var formatter = new SqlFormattingService();

        var result = formatter.Format(
            """
            select u.Id
            from dbo.Users u
            where coalesce((select top 1 a.Name from dbo.ArchiveUsers a where a.Id=u.Id order by a.CreatedAt desc),case when u.IsDeleted=1 then null else u.Name end) is null
            """);

        Assert.True(result.IsSuccess);

        var formatted = Normalize(result.FormattedSql);
        Assert.Matches("\\nWHERE\\n", formatted);
        Assert.Matches("(?i)\\n\\s{4}coalesce\\s*\\(", formatted);
        Assert.Matches("\\n\\s{8}\\(", formatted);
        Assert.Matches("\\n\\s{12}SELECT\\s+TOP\\s+1\\n", formatted);
        Assert.Matches("\\n\\s{8}CASE\\n", formatted);
        Assert.Matches("\\n\\s{12}WHEN\\s+u\\.IsDeleted\\s*=\\s*1\\n", formatted);
        Assert.Matches("\\n\\s{16}THEN\\s+NULL", formatted);
        Assert.Matches("\\n\\s{8}END\\n", formatted);
        Assert.Matches("\\n\\s{4}\\)\\s+IS\\s+NULL", formatted);
    }

    private static string Normalize(string text)
    {
        return text.ReplaceLineEndings("\n").Trim();
    }
}
