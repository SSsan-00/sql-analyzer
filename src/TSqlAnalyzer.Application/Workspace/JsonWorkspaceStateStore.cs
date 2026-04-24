using System.Text.Json;

namespace TSqlAnalyzer.Application.Workspace;

/// <summary>
/// ワークスペース状態を JSON ファイルへ保存する。
/// 起動時はファイルを読み込み、存在しない場合は既定状態を返す。
/// </summary>
public sealed class JsonWorkspaceStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly WorkspaceStateManager _workspaceStateManager;

    /// <summary>
    /// 保存先ファイルパスと状態管理を受け取る。
    /// </summary>
    public JsonWorkspaceStateStore(string filePath, WorkspaceStateManager workspaceStateManager)
    {
        _filePath = filePath;
        _workspaceStateManager = workspaceStateManager;
    }

    /// <summary>
    /// JSON を読み込み、使える状態へ補正して返す。
    /// </summary>
    public WorkspaceSessionState Load()
    {
        if (!File.Exists(_filePath))
        {
            return _workspaceStateManager.CreateDefaultState();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<WorkspaceSessionState>(json, SerializerOptions);
            return _workspaceStateManager.EnsureValidState(state);
        }
        catch
        {
            return _workspaceStateManager.CreateDefaultState();
        }
    }

    /// <summary>
    /// 状態を JSON として保存する。
    /// 保存先ディレクトリがなければ先に作成する。
    /// </summary>
    public void Save(WorkspaceSessionState state)
    {
        var normalizedState = _workspaceStateManager.EnsureValidState(state);
        var directoryPath = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(normalizedState, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
