using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Presentation;

/// <summary>
/// TreeView 表示用の UI 非依存ノード。
/// WinForms の TreeNode を直接返さず、中立的な木構造へ一度落とし込むために使う。
/// </summary>
public sealed record DisplayTreeNode(
    string Text,
    IReadOnlyList<DisplayTreeNode> Children,
    TextSpan? SourceSpan = null);
