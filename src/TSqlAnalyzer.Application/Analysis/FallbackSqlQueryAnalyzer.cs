using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Analysis;

/// <summary>
/// 優先解析器で構文解析できない場合だけ、別解析器へ委譲する合成解析器。
/// T-SQL の既存挙動を優先しつつ、方言固有構文を追加解析器で拾う。
/// </summary>
public sealed class FallbackSqlQueryAnalyzer : ISqlQueryAnalyzer
{
    private readonly ISqlQueryAnalyzer _primaryAnalyzer;
    private readonly ISqlQueryAnalyzer _fallbackAnalyzer;

    public FallbackSqlQueryAnalyzer(ISqlQueryAnalyzer primaryAnalyzer, ISqlQueryAnalyzer fallbackAnalyzer)
    {
        _primaryAnalyzer = primaryAnalyzer;
        _fallbackAnalyzer = fallbackAnalyzer;
    }

    public QueryAnalysisResult Analyze(string sql)
    {
        var primaryResult = _primaryAnalyzer.Analyze(sql);
        if (primaryResult.StatementCategory != QueryStatementCategory.ParseError)
        {
            return primaryResult;
        }

        var fallbackResult = _fallbackAnalyzer.Analyze(sql);
        return fallbackResult.StatementCategory == QueryStatementCategory.ParseError
            ? primaryResult
            : fallbackResult;
    }
}
