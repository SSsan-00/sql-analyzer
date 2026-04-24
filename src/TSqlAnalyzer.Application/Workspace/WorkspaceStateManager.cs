namespace TSqlAnalyzer.Application.Workspace;

/// <summary>
/// ワークスペース状態の生成と更新を担う。
/// 最低 1 ワークスペース、最低 1 クエリを維持する不変条件をここで守る。
/// </summary>
public sealed class WorkspaceStateManager
{
    /// <summary>
    /// 状態が null や不正でも、必ず使える状態へ補正する。
    /// </summary>
    public WorkspaceSessionState EnsureValidState(WorkspaceSessionState? state)
    {
        if (state is null || state.Workspaces.Count == 0)
        {
            return CreateDefaultState();
        }

        var clonedWorkspaces = state.Workspaces
            .Select(CloneWorkspace)
            .ToList();

        for (var index = 0; index < clonedWorkspaces.Count; index++)
        {
            if (clonedWorkspaces[index].Queries.Count == 0)
            {
                clonedWorkspaces[index] = clonedWorkspaces[index] with
                {
                    Queries = [CreateQueryDocument(DefaultQueryName(1))]
                };
            }
        }

        var selectedWorkspace = clonedWorkspaces
            .FirstOrDefault(workspace => workspace.Id == state.SelectedWorkspaceId)
            ?? clonedWorkspaces[0];
        var selectedQuery = selectedWorkspace.Queries
            .FirstOrDefault(query => query.Id == state.SelectedQueryId)
            ?? selectedWorkspace.Queries[0];

        return new WorkspaceSessionState(
            clonedWorkspaces,
            selectedWorkspace.Id,
            selectedQuery.Id);
    }

    /// <summary>
    /// 既定ワークスペース状態を作る。
    /// </summary>
    public WorkspaceSessionState CreateDefaultState()
    {
        var query = CreateQueryDocument(DefaultQueryName(1));
        var workspace = CreateWorkspace(DefaultWorkspaceName(1), [query]);
        return new WorkspaceSessionState(
            [workspace],
            workspace.Id,
            query.Id);
    }

    /// <summary>
    /// ワークスペースを追加し、新規ワークスペースを選択状態にする。
    /// </summary>
    public WorkspaceSessionState AddWorkspace(WorkspaceSessionState state, string? workspaceName = null)
    {
        var ensuredState = EnsureValidState(state);
        var workspaces = ensuredState.Workspaces
            .Select(CloneWorkspace)
            .ToList();
        var query = CreateQueryDocument(DefaultQueryName(1));
        var workspace = CreateWorkspace(
            NormalizeName(workspaceName, DefaultWorkspaceName(workspaces.Count + 1)),
            [query]);

        workspaces.Add(workspace);
        return new WorkspaceSessionState(workspaces, workspace.Id, query.Id);
    }

    /// <summary>
    /// ワークスペース名を更新する。
    /// </summary>
    public WorkspaceSessionState RenameWorkspace(WorkspaceSessionState state, string workspaceId, string workspaceName)
    {
        var ensuredState = EnsureValidState(state);
        var workspaces = ensuredState.Workspaces
            .Select(workspace => workspace.Id == workspaceId
                ? workspace with { Name = NormalizeName(workspaceName, workspace.Name) }
                : CloneWorkspace(workspace))
            .ToList();

        return EnsureValidState(ensuredState with { Workspaces = workspaces });
    }

    /// <summary>
    /// ワークスペースの並び順を変更する。
    /// 選択状態は維持しつつ、指定位置へ対象ワークスペースを移動する。
    /// </summary>
    public WorkspaceSessionState MoveWorkspace(WorkspaceSessionState state, string workspaceId, int targetIndex)
    {
        var ensuredState = EnsureValidState(state);
        var workspaces = ensuredState.Workspaces
            .Select(CloneWorkspace)
            .ToList();
        var sourceIndex = workspaces.FindIndex(workspace => workspace.Id == workspaceId);
        if (sourceIndex < 0)
        {
            return ensuredState;
        }

        var clampedTargetIndex = Math.Clamp(targetIndex, 0, workspaces.Count - 1);
        if (sourceIndex == clampedTargetIndex)
        {
            return ensuredState;
        }

        var workspace = workspaces[sourceIndex];
        workspaces.RemoveAt(sourceIndex);
        workspaces.Insert(Math.Clamp(clampedTargetIndex, 0, workspaces.Count), workspace);

        return new WorkspaceSessionState(
            workspaces,
            ensuredState.SelectedWorkspaceId,
            ensuredState.SelectedQueryId);
    }

    /// <summary>
    /// ワークスペースを削除する。
    /// 最後の 1 件は残し、削除後も必ず選択先が存在するようにする。
    /// </summary>
    public WorkspaceSessionState DeleteWorkspace(WorkspaceSessionState state, string workspaceId)
    {
        var ensuredState = EnsureValidState(state);
        var remainingWorkspaces = ensuredState.Workspaces
            .Where(workspace => workspace.Id != workspaceId)
            .Select(CloneWorkspace)
            .ToList();

        if (remainingWorkspaces.Count == 0)
        {
            return CreateDefaultState();
        }

        var selectedWorkspace = remainingWorkspaces
            .FirstOrDefault(workspace => workspace.Id == ensuredState.SelectedWorkspaceId)
            ?? remainingWorkspaces[0];
        var selectedQuery = selectedWorkspace.Queries[0];

        return new WorkspaceSessionState(
            remainingWorkspaces,
            selectedWorkspace.Id,
            selectedQuery.Id);
    }

    /// <summary>
    /// クエリを追加し、新規クエリを選択状態にする。
    /// </summary>
    public WorkspaceSessionState AddQuery(WorkspaceSessionState state, string workspaceId, string? queryName = null)
    {
        var ensuredState = EnsureValidState(state);
        var workspaces = ensuredState.Workspaces
            .Select(CloneWorkspace)
            .ToList();
        var workspaceIndex = workspaces.FindIndex(workspace => workspace.Id == workspaceId);
        if (workspaceIndex < 0)
        {
            return ensuredState;
        }

        var workspace = workspaces[workspaceIndex];
        var query = CreateQueryDocument(
            NormalizeName(queryName, DefaultQueryName(workspace.Queries.Count + 1)));
        workspace.Queries.Add(query);
        workspaces[workspaceIndex] = workspace;

        return new WorkspaceSessionState(workspaces, workspace.Id, query.Id);
    }

    /// <summary>
    /// クエリ名を更新する。
    /// </summary>
    public WorkspaceSessionState RenameQuery(WorkspaceSessionState state, string workspaceId, string queryId, string queryName)
    {
        var ensuredState = EnsureValidState(state);
        var workspaces = ensuredState.Workspaces
            .Select(CloneWorkspace)
            .ToList();
        var workspaceIndex = workspaces.FindIndex(workspace => workspace.Id == workspaceId);
        if (workspaceIndex < 0)
        {
            return ensuredState;
        }

        var workspace = workspaces[workspaceIndex];
        var queryIndex = workspace.Queries.FindIndex(query => query.Id == queryId);
        if (queryIndex < 0)
        {
            return ensuredState;
        }

        workspace.Queries[queryIndex] = workspace.Queries[queryIndex] with
        {
            Name = NormalizeName(queryName, workspace.Queries[queryIndex].Name)
        };
        workspaces[workspaceIndex] = workspace;

        return EnsureValidState(ensuredState with { Workspaces = workspaces });
    }

    /// <summary>
    /// クエリを削除する。
    /// ワークスペース内の最後の 1 件は残し、削除後も選択先が途切れないようにする。
    /// </summary>
    public WorkspaceSessionState DeleteQuery(WorkspaceSessionState state, string workspaceId, string queryId)
    {
        var ensuredState = EnsureValidState(state);
        var workspaces = ensuredState.Workspaces
            .Select(CloneWorkspace)
            .ToList();
        var workspaceIndex = workspaces.FindIndex(workspace => workspace.Id == workspaceId);
        if (workspaceIndex < 0)
        {
            return ensuredState;
        }

        var workspace = workspaces[workspaceIndex];
        workspace.Queries.RemoveAll(query => query.Id == queryId);
        if (workspace.Queries.Count == 0)
        {
            workspace.Queries.Add(CreateQueryDocument(DefaultQueryName(1)));
        }

        workspaces[workspaceIndex] = workspace;
        var selectedWorkspace = workspaces.First(workspaceItem => workspaceItem.Id == workspace.Id);
        var selectedQuery = selectedWorkspace.Queries[0];

        return new WorkspaceSessionState(
            workspaces,
            selectedWorkspace.Id,
            selectedQuery.Id);
    }

    /// <summary>
    /// 選択ワークスペースを切り替える。
    /// 選択クエリはワークスペース先頭へ合わせる。
    /// </summary>
    public WorkspaceSessionState SelectWorkspace(WorkspaceSessionState state, string workspaceId)
    {
        var ensuredState = EnsureValidState(state);
        var workspace = ensuredState.Workspaces.FirstOrDefault(item => item.Id == workspaceId);
        if (workspace is null)
        {
            return ensuredState;
        }

        return new WorkspaceSessionState(
            ensuredState.Workspaces.Select(CloneWorkspace).ToList(),
            workspace.Id,
            workspace.Queries[0].Id);
    }

    /// <summary>
    /// 選択クエリを切り替える。
    /// </summary>
    public WorkspaceSessionState SelectQuery(WorkspaceSessionState state, string workspaceId, string queryId)
    {
        var ensuredState = EnsureValidState(state);
        var workspace = ensuredState.Workspaces.FirstOrDefault(item => item.Id == workspaceId);
        if (workspace is null)
        {
            return ensuredState;
        }

        var query = workspace.Queries.FirstOrDefault(item => item.Id == queryId);
        if (query is null)
        {
            return ensuredState;
        }

        return new WorkspaceSessionState(
            ensuredState.Workspaces.Select(CloneWorkspace).ToList(),
            workspace.Id,
            query.Id);
    }

    /// <summary>
    /// 現在選択中クエリの SQL 本文を更新する。
    /// </summary>
    public WorkspaceSessionState UpdateSelectedQuerySql(WorkspaceSessionState state, string sqlText)
    {
        var ensuredState = EnsureValidState(state);
        var workspaces = ensuredState.Workspaces
            .Select(CloneWorkspace)
            .ToList();
        var workspaceIndex = workspaces.FindIndex(workspace => workspace.Id == ensuredState.SelectedWorkspaceId);
        if (workspaceIndex < 0)
        {
            return ensuredState;
        }

        var workspace = workspaces[workspaceIndex];
        var queryIndex = workspace.Queries.FindIndex(query => query.Id == ensuredState.SelectedQueryId);
        if (queryIndex < 0)
        {
            return ensuredState;
        }

        workspace.Queries[queryIndex] = workspace.Queries[queryIndex] with
        {
            SqlText = sqlText ?? string.Empty
        };
        workspaces[workspaceIndex] = workspace;

        return ensuredState with { Workspaces = workspaces };
    }

    /// <summary>
    /// 選択中のワークスペースを取り出す。
    /// </summary>
    public QueryWorkspaceState GetSelectedWorkspace(WorkspaceSessionState state)
    {
        var ensuredState = EnsureValidState(state);
        return ensuredState.Workspaces.First(workspace => workspace.Id == ensuredState.SelectedWorkspaceId);
    }

    /// <summary>
    /// 選択中のクエリを取り出す。
    /// </summary>
    public QueryDocumentState GetSelectedQuery(WorkspaceSessionState state)
    {
        var workspace = GetSelectedWorkspace(state);
        var ensuredState = EnsureValidState(state);
        return workspace.Queries.First(query => query.Id == ensuredState.SelectedQueryId);
    }

    /// <summary>
    /// ワークスペースを複製する。
    /// </summary>
    private static QueryWorkspaceState CloneWorkspace(QueryWorkspaceState workspace)
    {
        return workspace with
        {
            Queries = workspace.Queries
                .Select(query => query with { })
                .ToList()
        };
    }

    /// <summary>
    /// 新規ワークスペースを作る。
    /// </summary>
    private static QueryWorkspaceState CreateWorkspace(string name, List<QueryDocumentState> queries)
    {
        return new QueryWorkspaceState(
            CreateId(),
            name,
            queries);
    }

    /// <summary>
    /// 新規クエリを作る。
    /// </summary>
    private static QueryDocumentState CreateQueryDocument(string name)
    {
        return new QueryDocumentState(
            CreateId(),
            name,
            string.Empty);
    }

    /// <summary>
    /// 表示名の空文字を既定名へ置き換える。
    /// </summary>
    private static string NormalizeName(string? value, string fallbackName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallbackName
            : value.Trim();
    }

    /// <summary>
    /// 既定ワークスペース名を返す。
    /// </summary>
    private static string DefaultWorkspaceName(int index)
    {
        return $"ワークスペース {index}";
    }

    /// <summary>
    /// 既定クエリ名を返す。
    /// </summary>
    private static string DefaultQueryName(int index)
    {
        return $"クエリ {index}";
    }

    /// <summary>
    /// 状態識別子を生成する。
    /// </summary>
    private static string CreateId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
