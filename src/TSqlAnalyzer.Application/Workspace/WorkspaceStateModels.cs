namespace TSqlAnalyzer.Application.Workspace;

/// <summary>
/// アプリ全体で共有するワークスペース状態。
/// 複数ワークスペースと、現在選択中のワークスペース・クエリを保持する。
/// </summary>
public sealed record WorkspaceSessionState(
    List<QueryWorkspaceState> Workspaces,
    string SelectedWorkspaceId,
    string SelectedQueryId);

/// <summary>
/// ワークスペース 1 件分の状態。
/// ワークスペース名と、その中に属するクエリ一覧を持つ。
/// </summary>
public sealed record QueryWorkspaceState(
    string Id,
    string Name,
    List<QueryDocumentState> Queries);

/// <summary>
/// クエリ 1 件分の状態。
/// 一覧表示名と SQL 本文を保持する。
/// </summary>
public sealed record QueryDocumentState(
    string Id,
    string Name,
    string SqlText);
