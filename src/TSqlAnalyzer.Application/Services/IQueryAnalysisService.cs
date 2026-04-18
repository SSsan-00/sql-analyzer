using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Services;

/// <summary>
/// UI から利用する解析サービスの公開契約。
/// フォームはこの契約だけを知ればよく、内部のパーサー差し替えを隠蔽できる。
/// </summary>
public interface IQueryAnalysisService
{
    /// <summary>
    /// T-SQL 文字列を解析し、UI 向けの中立モデルへ変換する。
    /// </summary>
    QueryAnalysisResult Analyze(string sql);
}
