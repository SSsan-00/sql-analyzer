using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Analysis;

/// <summary>
/// 実際の SQL 解析器を表す内部契約。
/// ScriptDom 実装の交換や、将来の別実装追加を見据えて分離している。
/// </summary>
public interface ISqlQueryAnalyzer
{
    /// <summary>
    /// SQL 文字列を解析し、ドメインモデルへ変換する。
    /// </summary>
    QueryAnalysisResult Analyze(string sql);
}
