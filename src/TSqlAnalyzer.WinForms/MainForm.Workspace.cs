using TSqlAnalyzer.Application.Workspace;
using TSqlAnalyzer.WinForms.UI;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// ワークスペース機能まわりの UI と状態同期をまとめる partial。
/// ワークスペース選択、クエリ一覧、保存復元を画面本体から切り分ける。
/// </summary>
public partial class MainForm
{
    private readonly WorkspaceStateManager _workspaceStateManager;
    private readonly JsonWorkspaceStateStore _workspaceStateStore;

    private TableLayoutPanel _workspacePanel = null!;
    private ComboBox _workspaceComboBox = null!;
    private ListBox _queryListBox = null!;
    private System.Windows.Forms.Timer _workspaceSaveTimer = null!;

    private WorkspaceSessionState _workspaceState = null!;
    private bool _suppressWorkspaceUiEvents;
    private bool _suppressWorkspaceTextSync;

    /// <summary>
    /// ワークスペース UI を初期化する。
    /// 既存レイアウトへ上部パネルとして差し込み、SQL 入力欄の上に一覧を置く。
    /// </summary>
    private void InitializeWorkspaceUi()
    {
        _workspaceComboBox = CreateWorkspaceComboBox();
        _queryListBox = CreateQueryListBox();
        _workspacePanel = CreateWorkspacePanel();
        _workspaceSaveTimer = new System.Windows.Forms.Timer(components!)
        {
            Interval = 800
        };

        _workspaceSaveTimer.Tick += WorkspaceSaveTimer_Tick;
        FormClosing += MainForm_FormClosing;

        mainSplitContainer.Panel1.Controls.Add(_workspacePanel);
        mainSplitContainer.Panel1.Controls.SetChildIndex(inputLabel, 0);
        mainSplitContainer.Panel1.Controls.SetChildIndex(_workspacePanel, 1);
        mainSplitContainer.Panel1.Controls.SetChildIndex(findPanel, 2);
        mainSplitContainer.Panel1.Controls.SetChildIndex(sqlTextBox, 3);
    }

    /// <summary>
    /// 保存済みワークスペース状態を読み込み、選択中クエリを入力欄へ反映する。
    /// </summary>
    private void InitializeWorkspaceState()
    {
        _workspaceState = _workspaceStateStore.Load();
        BindWorkspaceControls();
        ApplySelectedQueryToEditor();
    }

    /// <summary>
    /// ワークスペース UI 全体を構築する。
    /// </summary>
    private TableLayoutPanel CreateWorkspacePanel()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Name = "workspacePanel",
            Padding = new Padding(0, 0, 0, 8),
            RowCount = 3
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(CreateWorkspaceSelectorPanel(), 0, 0);
        panel.Controls.Add(CreateQueryHeaderPanel(), 0, 1);
        panel.Controls.Add(_queryListBox, 0, 2);
        return panel;
    }

    /// <summary>
    /// ワークスペース選択行を作る。
    /// </summary>
    private FlowLayoutPanel CreateWorkspaceSelectorPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 6),
            WrapContents = false
        };

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0),
            Text = "ワークスペース"
        });
        panel.Controls.Add(_workspaceComboBox);
        panel.Controls.Add(CreateWorkspaceActionButton("追加", WorkspaceAddButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("名前変更", WorkspaceRenameButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("削除", WorkspaceDeleteButton_Click));
        return panel;
    }

    /// <summary>
    /// クエリ一覧ヘッダーを作る。
    /// </summary>
    private FlowLayoutPanel CreateQueryHeaderPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 4),
            WrapContents = false
        };

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0),
            Text = "クエリ一覧"
        });
        panel.Controls.Add(CreateWorkspaceActionButton("追加", QueryAddButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("名前変更", QueryRenameButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("削除", QueryDeleteButton_Click));
        return panel;
    }

    /// <summary>
    /// ワークスペース選択コンボボックスを作る。
    /// </summary>
    private ComboBox CreateWorkspaceComboBox()
    {
        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220
        };

        comboBox.SelectedIndexChanged += WorkspaceComboBox_SelectedIndexChanged;
        return comboBox;
    }

    /// <summary>
    /// クエリ一覧を作る。
    /// </summary>
    private ListBox CreateQueryListBox()
    {
        var listBox = new ListBox
        {
            Dock = DockStyle.Top,
            Font = new Font("Yu Gothic UI", 9F),
            Height = 110,
            IntegralHeight = false
        };

        listBox.SelectedIndexChanged += QueryListBox_SelectedIndexChanged;
        return listBox;
    }

    /// <summary>
    /// ワークスペース操作ボタンを作る。
    /// </summary>
    private static Button CreateWorkspaceActionButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 0),
            Text = text,
            UseVisualStyleBackColor = true
        };

        button.Click += onClick;
        return button;
    }

    /// <summary>
    /// 現在の状態をコンボボックスと一覧へ反映する。
    /// </summary>
    private void BindWorkspaceControls()
    {
        _workspaceState = _workspaceStateManager.EnsureValidState(_workspaceState);
        _suppressWorkspaceUiEvents = true;
        try
        {
            _workspaceComboBox.BeginUpdate();
            try
            {
                _workspaceComboBox.Items.Clear();
                foreach (var workspace in _workspaceState.Workspaces)
                {
                    _workspaceComboBox.Items.Add(new WorkspaceListItem(workspace));
                }
            }
            finally
            {
                _workspaceComboBox.EndUpdate();
            }

            var selectedWorkspaceIndex = _workspaceState.Workspaces.FindIndex(
                workspace => workspace.Id == _workspaceState.SelectedWorkspaceId);
            _workspaceComboBox.SelectedIndex = selectedWorkspaceIndex >= 0 ? selectedWorkspaceIndex : 0;

            BindQueryListBox();
        }
        finally
        {
            _suppressWorkspaceUiEvents = false;
        }
    }

    /// <summary>
    /// 選択中ワークスペースのクエリ一覧を反映する。
    /// </summary>
    private void BindQueryListBox()
    {
        var selectedWorkspace = _workspaceStateManager.GetSelectedWorkspace(_workspaceState);

        _queryListBox.BeginUpdate();
        try
        {
            _queryListBox.Items.Clear();
            foreach (var query in selectedWorkspace.Queries)
            {
                _queryListBox.Items.Add(new QueryListItem(query));
            }
        }
        finally
        {
            _queryListBox.EndUpdate();
        }

        var selectedQueryIndex = selectedWorkspace.Queries.FindIndex(
            query => query.Id == _workspaceState.SelectedQueryId);
        _queryListBox.SelectedIndex = selectedQueryIndex >= 0 ? selectedQueryIndex : 0;
    }

    /// <summary>
    /// 選択中クエリを入力欄へ反映する。
    /// 空クエリなら解析表示をクリアし、内容があれば再解析する。
    /// </summary>
    private void ApplySelectedQueryToEditor()
    {
        var selectedQuery = _workspaceStateManager.GetSelectedQuery(_workspaceState);

        HideCompletionPopup();
        _suppressWorkspaceTextSync = true;
        _suppressSqlSelectionSync = true;
        try
        {
            ClearSqlLinkedHighlightCore();
            ClearSqlParseIssueHighlightCore();
            sqlTextBox.Text = selectedQuery.SqlText.ReplaceLineEndings(Environment.NewLine);
            sqlTextBox.Select(0, 0);
        }
        finally
        {
            _suppressSqlSelectionSync = false;
            _suppressWorkspaceTextSync = false;
        }

        if (string.IsNullOrWhiteSpace(selectedQuery.SqlText))
        {
            ClearAnalysisView();
            return;
        }

        AnalyzeCurrentSql();
    }

    /// <summary>
    /// 解析結果表示だけを初期化する。
    /// クエリ本文やワークスペース選択は保持する。
    /// </summary>
    private void ClearAnalysisView()
    {
        _currentAnalysis = null;
        _currentTree = null;
        _displayedTree = null;
        _isTreeFilterActive = false;
        _treeSearchText = string.Empty;
        _highlightedSqlSpan = null;
        _parseIssueHighlightedSqlSpan = null;

        _suppressTreeSearchTextChanged = true;
        try
        {
            resultSearchTextBox.Clear();
        }
        finally
        {
            _suppressTreeSearchTextChanged = false;
        }

        resultTreeView.Nodes.Clear();
        detailTextBox.Clear();
        HideCompletionPopup();
        ClearParseIssues();
        ClearSqlLinkedHighlight();
    }

    /// <summary>
    /// 保存タイマーを再始動する。
    /// 入力連打のたびに即保存せず、少しまとめて書き出す。
    /// </summary>
    private void ScheduleWorkspaceSave()
    {
        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    /// <summary>
    /// 現在状態を保存する。
    /// </summary>
    private void SaveWorkspaceState()
    {
        _workspaceSaveTimer.Stop();
        _workspaceStateStore.Save(_workspaceState);
    }

    /// <summary>
    /// タイマー発火時に状態を保存する。
    /// </summary>
    private void WorkspaceSaveTimer_Tick(object? sender, EventArgs e)
    {
        SaveWorkspaceState();
    }

    /// <summary>
    /// 画面終了時に最後の状態を保存する。
    /// </summary>
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveWorkspaceState();
    }

    /// <summary>
    /// ワークスペース選択変更を状態へ反映する。
    /// </summary>
    private void WorkspaceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressWorkspaceUiEvents || _workspaceComboBox.SelectedItem is not WorkspaceListItem item)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.SelectWorkspace(_workspaceState, item.Workspace.Id);
        BindWorkspaceControls();
        ApplySelectedQueryToEditor();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// クエリ選択変更を状態へ反映する。
    /// </summary>
    private void QueryListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressWorkspaceUiEvents || _queryListBox.SelectedItem is not QueryListItem item)
        {
            return;
        }

        var selectedWorkspace = _workspaceStateManager.GetSelectedWorkspace(_workspaceState);
        _workspaceState = _workspaceStateManager.SelectQuery(_workspaceState, selectedWorkspace.Id, item.Query.Id);
        BindWorkspaceControls();
        ApplySelectedQueryToEditor();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// 新しいワークスペースを追加する。
    /// </summary>
    private void WorkspaceAddButton_Click(object? sender, EventArgs e)
    {
        var workspaceName = TextPromptDialog.Show(
            this,
            "ワークスペース追加",
            "ワークスペース名",
            $"ワークスペース {_workspaceState.Workspaces.Count + 1}");
        if (workspaceName is null)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.AddWorkspace(_workspaceState, workspaceName);
        BindWorkspaceControls();
        ApplySelectedQueryToEditor();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// 選択中ワークスペース名を変更する。
    /// </summary>
    private void WorkspaceRenameButton_Click(object? sender, EventArgs e)
    {
        if (_workspaceComboBox.SelectedItem is not WorkspaceListItem item)
        {
            return;
        }

        var workspaceName = TextPromptDialog.Show(
            this,
            "ワークスペース名変更",
            "ワークスペース名",
            item.Workspace.Name);
        if (workspaceName is null)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.RenameWorkspace(_workspaceState, item.Workspace.Id, workspaceName);
        BindWorkspaceControls();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// 選択中ワークスペースを削除する。
    /// </summary>
    private void WorkspaceDeleteButton_Click(object? sender, EventArgs e)
    {
        if (_workspaceComboBox.SelectedItem is not WorkspaceListItem item)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"ワークスペース「{item.Workspace.Name}」を削除しますか。",
                "ワークスペース削除",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.DeleteWorkspace(_workspaceState, item.Workspace.Id);
        BindWorkspaceControls();
        ApplySelectedQueryToEditor();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// 選択中ワークスペースへクエリを追加する。
    /// </summary>
    private void QueryAddButton_Click(object? sender, EventArgs e)
    {
        var selectedWorkspace = _workspaceStateManager.GetSelectedWorkspace(_workspaceState);
        var queryName = TextPromptDialog.Show(
            this,
            "クエリ追加",
            "クエリ名",
            $"クエリ {selectedWorkspace.Queries.Count + 1}");
        if (queryName is null)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.AddQuery(_workspaceState, selectedWorkspace.Id, queryName);
        BindWorkspaceControls();
        ApplySelectedQueryToEditor();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// 選択中クエリ名を変更する。
    /// </summary>
    private void QueryRenameButton_Click(object? sender, EventArgs e)
    {
        if (_queryListBox.SelectedItem is not QueryListItem item)
        {
            return;
        }

        var selectedWorkspace = _workspaceStateManager.GetSelectedWorkspace(_workspaceState);
        var queryName = TextPromptDialog.Show(
            this,
            "クエリ名変更",
            "クエリ名",
            item.Query.Name);
        if (queryName is null)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.RenameQuery(_workspaceState, selectedWorkspace.Id, item.Query.Id, queryName);
        BindWorkspaceControls();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// 選択中クエリを削除する。
    /// </summary>
    private void QueryDeleteButton_Click(object? sender, EventArgs e)
    {
        if (_queryListBox.SelectedItem is not QueryListItem item)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"クエリ「{item.Query.Name}」を削除しますか。",
                "クエリ削除",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        var selectedWorkspace = _workspaceStateManager.GetSelectedWorkspace(_workspaceState);
        _workspaceState = _workspaceStateManager.DeleteQuery(_workspaceState, selectedWorkspace.Id, item.Query.Id);
        BindWorkspaceControls();
        ApplySelectedQueryToEditor();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// ワークスペース一覧コンボの表示用アイテム。
    /// </summary>
    private sealed record WorkspaceListItem(QueryWorkspaceState Workspace)
    {
        public override string ToString()
        {
            return Workspace.Name;
        }
    }

    /// <summary>
    /// クエリ一覧の表示用アイテム。
    /// </summary>
    private sealed record QueryListItem(QueryDocumentState Query)
    {
        public override string ToString()
        {
            return Query.Name;
        }
    }
}
