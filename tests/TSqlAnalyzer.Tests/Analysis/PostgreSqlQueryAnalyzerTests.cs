using TSqlAnalyzer.Application.Analysis;
using TSqlAnalyzer.Application.Services;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Tests.Analysis;

public sealed class PostgreSqlQueryAnalyzerTests
{
    [Fact]
    public void Analyze_PostgreSqlSelectWithCteJoinAndLimit_ReturnsStructure()
    {
        var service = CreatePostgreSqlService();
        const string sql = """
                           WITH recent AS (
                               SELECT
                                   u.id,
                                   u.name,
                                   SUM(o.amount) AS total
                               FROM public.users u
                               LEFT JOIN public.orders o
                                   ON o.user_id = u.id
                               WHERE u.active IS NOT NULL
                                 AND o.amount > 0
                               GROUP BY u.id, u.name
                           )
                           SELECT
                               r.id,
                               r.name,
                               r.total
                           FROM recent r
                           WHERE r.total > 100
                           ORDER BY r.name DESC
                           LIMIT 10;
                           """;

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.Select, result.StatementCategory);
        Assert.Contains(result.Notices, notice => notice.Message.Contains("PostgreSQL", StringComparison.Ordinal));

        var cte = Assert.Single(result.CommonTableExpressions);
        Assert.Equal("recent", cte.Name);
        Assert.Contains("total", cte.ColumnNames);

        var cteQuery = Assert.IsType<SelectQueryAnalysis>(cte.Query);
        Assert.Equal("public.users u", cteQuery.MainSource?.DisplayText);
        var cteJoin = Assert.Single(cteQuery.Joins);
        Assert.Equal(JoinType.Left, cteJoin.JoinType);
        Assert.Equal("public.orders o", cteJoin.TargetSource.DisplayText);

        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        Assert.Equal("LIMIT 10", query.TopExpressionText);
        Assert.Equal(SourceKind.CommonTableExpressionReference, query.MainSource?.SourceKind);
        Assert.Equal("recent r", query.MainSource?.DisplayText);
        Assert.Equal(3, query.SelectItems.Count);
        Assert.Equal("r.total > 100", query.WhereCondition?.DisplayText);

        var orderItem = Assert.Single(query.OrderBy!.OrderItems);
        Assert.Equal("r.name", orderItem.ExpressionText);
        Assert.Equal(OrderByDirection.Descending, orderItem.Direction);
    }

    [Fact]
    public void Analyze_PostgreSqlInsertReturning_ReturnsInsertStructure()
    {
        var service = CreatePostgreSqlService();
        const string sql = """
                           INSERT INTO public.users (id, name)
                           VALUES (1, 'Ada')
                           RETURNING id, name;
                           """;

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.Insert, result.StatementCategory);
        var insert = Assert.IsType<InsertStatementAnalysis>(result.DataModification);
        Assert.Equal("public.users", insert.Target.SourceName);
        Assert.Equal(["id", "name"], insert.TargetColumns);
        Assert.Equal(InsertSourceKind.Values, insert.InsertSource?.SourceKind);
        Assert.Equal("RETURNING id, name", insert.OutputClauseText);

        var valueGroup = Assert.Single(insert.InsertSource!.MappingGroups);
        Assert.Collection(
            valueGroup.Mappings,
            mapping =>
            {
                Assert.Equal("id", mapping.TargetColumn);
                Assert.Equal("1", mapping.ValueText);
            },
            mapping =>
            {
                Assert.Equal("name", mapping.TargetColumn);
                Assert.Equal("'Ada'", mapping.ValueText);
            });
    }

    [Fact]
    public void Analyze_FallbackAnalyzer_UsesPostgreSqlWhenScriptDomCannotParse()
    {
        var service = new QueryAnalysisService(new FallbackSqlQueryAnalyzer(
            new ScriptDomQueryAnalyzer(),
            new PostgreSqlQueryAnalyzer()));
        const string sql = "SELECT id FROM public.users LIMIT 5;";

        var result = service.Analyze(sql);

        Assert.Equal(QueryStatementCategory.Select, result.StatementCategory);
        var query = Assert.IsType<SelectQueryAnalysis>(result.Query);
        Assert.Equal("LIMIT 5", query.TopExpressionText);
        Assert.Contains(result.Notices, notice => notice.Message.Contains("PostgreSQL", StringComparison.Ordinal));
    }

    private static QueryAnalysisService CreatePostgreSqlService()
    {
        return new QueryAnalysisService(new PostgreSqlQueryAnalyzer());
    }
}
