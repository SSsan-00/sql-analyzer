using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Formatting;

/// <summary>
/// SQL 整形の実行結果。
/// 整形後文字列と失敗理由をまとめ、UI が分岐しやすい形で返す。
/// </summary>
public sealed record SqlFormatResult(
    bool IsSuccess,
    string FormattedSql,
    IReadOnlyList<ParseIssue> ParseIssues);
