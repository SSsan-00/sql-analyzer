using TSqlAnalyzer.Application.Workspace;

namespace TSqlAnalyzer.Tests.Workspace;

/// <summary>
/// ワークスペース状態管理の不変条件を検証する。
/// UI を介さず、最低 1 ワークスペース・最低 1 クエリが維持されることを確認する。
/// </summary>
public sealed class WorkspaceStateManagerTests
{
    /// <summary>
    /// 状態未指定なら既定ワークスペースと既定クエリを生成することを確認する。
    /// </summary>
    [Fact]
    public void EnsureValidState_ForNull_ReturnsDefaultWorkspaceAndQuery()
    {
        var manager = new WorkspaceStateManager();

        var state = manager.EnsureValidState(null);

        Assert.Single(state.Workspaces);
        Assert.Equal("ワークスペース 1", state.Workspaces[0].Name);
        Assert.Single(state.Workspaces[0].Queries);
        Assert.Equal("クエリ 1", state.Workspaces[0].Queries[0].Name);
        Assert.Equal(state.Workspaces[0].Id, state.SelectedWorkspaceId);
        Assert.Equal(state.Workspaces[0].Queries[0].Id, state.SelectedQueryId);
        Assert.True(state.IsWorkspaceListExpanded);
        Assert.True(state.IsQueryListExpanded);
    }

    /// <summary>
    /// ワークスペース追加時は新規ワークスペースが選択されることを確認する。
    /// </summary>
    [Fact]
    public void AddWorkspace_SelectsAddedWorkspace()
    {
        var manager = new WorkspaceStateManager();
        var state = manager.EnsureValidState(null);

        var updatedState = manager.AddWorkspace(state, "検証用");

        Assert.Equal(2, updatedState.Workspaces.Count);
        Assert.Equal("検証用", updatedState.Workspaces[^1].Name);
        Assert.Equal(updatedState.Workspaces[^1].Id, updatedState.SelectedWorkspaceId);
        Assert.Equal(updatedState.Workspaces[^1].Queries[0].Id, updatedState.SelectedQueryId);
    }

    /// <summary>
    /// ワークスペース移動では順序だけが変わり、選択中ワークスペース自体は維持されることを確認する。
    /// </summary>
    [Fact]
    public void MoveWorkspace_ReordersListAndKeepsSelection()
    {
        var manager = new WorkspaceStateManager();
        var state = manager.AddWorkspace(manager.EnsureValidState(null), "検証用 2");
        state = manager.AddWorkspace(state, "検証用 3");
        var selectedWorkspaceId = state.SelectedWorkspaceId;
        var selectedQueryId = state.SelectedQueryId;

        var updatedState = manager.MoveWorkspace(state, selectedWorkspaceId, 0);

        Assert.Equal(selectedWorkspaceId, updatedState.SelectedWorkspaceId);
        Assert.Equal(selectedQueryId, updatedState.SelectedQueryId);
        Assert.Equal(selectedWorkspaceId, updatedState.Workspaces[0].Id);
        Assert.Equal("ワークスペース 1", updatedState.Workspaces[1].Name);
        Assert.Equal("検証用 2", updatedState.Workspaces[2].Name);
    }

    /// <summary>
    /// 移動先が範囲外でも先頭または末尾へ丸められることを確認する。
    /// </summary>
    [Fact]
    public void MoveWorkspace_ClampsOutOfRangeTargetIndex()
    {
        var manager = new WorkspaceStateManager();
        var state = manager.AddWorkspace(manager.EnsureValidState(null), "検証用 2");
        var firstWorkspaceId = state.Workspaces[0].Id;

        var updatedState = manager.MoveWorkspace(state, firstWorkspaceId, 99);

        Assert.Equal(firstWorkspaceId, updatedState.Workspaces[^1].Id);
    }

    /// <summary>
    /// 一覧展開状態はトグルごとに反転し、他の選択状態は維持されることを確認する。
    /// </summary>
    [Fact]
    public void ToggleListExpanded_FlipsOnlyTargetFlag()
    {
        var manager = new WorkspaceStateManager();
        var state = manager.AddWorkspace(manager.EnsureValidState(null), "検証用 2");
        var selectedWorkspaceId = state.SelectedWorkspaceId;
        var selectedQueryId = state.SelectedQueryId;

        state = manager.ToggleWorkspaceListExpanded(state);
        var updatedState = manager.ToggleQueryListExpanded(state);

        Assert.False(updatedState.IsWorkspaceListExpanded);
        Assert.False(updatedState.IsQueryListExpanded);
        Assert.Equal(selectedWorkspaceId, updatedState.SelectedWorkspaceId);
        Assert.Equal(selectedQueryId, updatedState.SelectedQueryId);
    }

    /// <summary>
    /// 最後のクエリを削除しようとしても、空ワークスペースにならず代替クエリが維持されることを確認する。
    /// </summary>
    [Fact]
    public void DeleteQuery_ForLastQuery_KeepsReplacementQuery()
    {
        var manager = new WorkspaceStateManager();
        var state = manager.EnsureValidState(null);

        var updatedState = manager.DeleteQuery(state, state.SelectedWorkspaceId, state.SelectedQueryId);

        var workspace = Assert.Single(updatedState.Workspaces);
        var query = Assert.Single(workspace.Queries);
        Assert.Equal("クエリ 1", query.Name);
        Assert.Equal(workspace.Id, updatedState.SelectedWorkspaceId);
        Assert.Equal(query.Id, updatedState.SelectedQueryId);
    }

    /// <summary>
    /// 選択中クエリの SQL 更新が状態へ反映されることを確認する。
    /// </summary>
    [Fact]
    public void UpdateSelectedQuerySql_UpdatesCurrentQueryText()
    {
        var manager = new WorkspaceStateManager();
        var state = manager.EnsureValidState(null);

        var updatedState = manager.UpdateSelectedQuerySql(state, "SELECT 1;");

        var workspace = Assert.Single(updatedState.Workspaces);
        var query = Assert.Single(workspace.Queries);
        Assert.Equal("SELECT 1;", query.SqlText);
    }
}
