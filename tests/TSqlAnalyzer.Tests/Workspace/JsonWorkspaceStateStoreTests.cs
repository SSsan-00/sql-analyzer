using TSqlAnalyzer.Application.Workspace;

namespace TSqlAnalyzer.Tests.Workspace;

/// <summary>
/// ワークスペース状態の JSON 保存と復元を検証する。
/// アプリ再起動後も前回の選択と SQL 本文が戻ることを確認する。
/// </summary>
public sealed class JsonWorkspaceStateStoreTests
{
    /// <summary>
    /// 保存した状態を再読込すると、選択中ワークスペースと SQL 本文が保たれることを確認する。
    /// </summary>
    [Fact]
    public void SaveAndLoad_RoundTripsWorkspaceState()
    {
        var manager = new WorkspaceStateManager();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var filePath = Path.Combine(tempDirectory.FullName, "workspace-state.json");
            var store = new JsonWorkspaceStateStore(filePath, manager);
            var initialState = manager.AddWorkspace(manager.EnsureValidState(null), "調査用");
            var queryWorkspaceId = initialState.SelectedWorkspaceId;
            var queryId = initialState.SelectedQueryId;
            var stateToSave = manager.UpdateSelectedQuerySql(initialState, "SELECT 42 AS Answer;");
            stateToSave = manager.ToggleWorkspaceListExpanded(stateToSave);

            store.Save(stateToSave);

            var loadedState = store.Load();

            Assert.Equal(queryWorkspaceId, loadedState.SelectedWorkspaceId);
            Assert.Equal(queryId, loadedState.SelectedQueryId);
            Assert.Equal("調査用", loadedState.Workspaces[^1].Name);
            Assert.Equal("SELECT 42 AS Answer;", loadedState.Workspaces[^1].Queries[0].SqlText);
            Assert.False(loadedState.IsWorkspaceListExpanded);
            Assert.False(loadedState.IsQueryListExpanded);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// 旧形式 JSON に開閉フラグがなくても、既定では展開状態で読み込めることを確認する。
    /// </summary>
    [Fact]
    public void Load_LegacyJsonWithoutUiFlags_DefaultsListsToExpanded()
    {
        var manager = new WorkspaceStateManager();
        var tempDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var filePath = Path.Combine(tempDirectory.FullName, "workspace-state.json");
            var store = new JsonWorkspaceStateStore(filePath, manager);
            File.WriteAllText(
                filePath,
                """
                {
                  "Workspaces": [
                    {
                      "Id": "workspace-1",
                      "Name": "旧保存",
                      "Queries": [
                        {
                          "Id": "query-1",
                          "Name": "クエリ 1",
                          "SqlText": "SELECT 1;"
                        }
                      ]
                    }
                  ],
                  "SelectedWorkspaceId": "workspace-1",
                  "SelectedQueryId": "query-1"
                }
                """);

            var loadedState = store.Load();

            Assert.False(loadedState.IsWorkspaceListExpanded);
            Assert.False(loadedState.IsQueryListExpanded);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
