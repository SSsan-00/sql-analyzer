using TSqlAnalyzer.Application.Analysis;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Services;

/// <summary>
/// WinForms から呼び出されるアプリケーションサービス。
/// 現時点では解析器への委譲が中心だが、将来の前処理やログ記録の集約点になる。
/// </summary>
public sealed class QueryAnalysisService : IQueryAnalysisService
{
    private readonly ISqlQueryAnalyzer _queryAnalyzer;

    /// <summary>
    /// 利用する解析器を受け取る。
    /// </summary>
    public QueryAnalysisService(ISqlQueryAnalyzer queryAnalyzer)
    {
        _queryAnalyzer = queryAnalyzer;
    }

    /// <summary>
    /// SQL を解析する。
    /// </summary>
    public QueryAnalysisResult Analyze(string sql)
    {
        return _queryAnalyzer.Analyze(sql);
    }
}
