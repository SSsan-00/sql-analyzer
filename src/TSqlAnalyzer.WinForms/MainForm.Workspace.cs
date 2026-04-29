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
    private Button _workspaceListToggleButton = null!;
    private Button _queryListToggleButton = null!;
    private ListBox _workspaceListBox = null!;
    private ListBox _queryListBox = null!;
    private System.Windows.Forms.Timer _workspaceSaveTimer = null!;
    private Point _workspaceDragStartPoint;
    private int _workspaceDragSourceIndex = -1;

    private WorkspaceSessionState _workspaceState = null!;
    private bool _suppressWorkspaceUiEvents;
    private bool _suppressWorkspaceTextSync;

    /// <summary>
    /// ワークスペース UI を初期化する。
    /// 既存レイアウトへ上部パネルとして差し込み、SQL 入力欄の上に一覧を置く。
    /// </summary>
    private void InitializeWorkspaceUi()
    {
        _workspaceListToggleButton = CreateWorkspaceHeaderToggleButton(WorkspaceListToggleButton_Click);
        _queryListToggleButton = CreateWorkspaceHeaderToggleButton(QueryListToggleButton_Click);
        _workspaceListBox = CreateWorkspaceListBox();
        _queryListBox = CreateQueryListBox();
        _workspacePanel = CreateWorkspacePanel();
        _workspaceSaveTimer = new System.Windows.Forms.Timer(components!)
        {
            Interval = 800
        };

        _workspaceSaveTimer.Tick += WorkspaceSaveTimer_Tick;
        FormClosing += MainForm_FormClosing;

        buttonPanel.Controls.Add(_workspaceListToggleButton);
        buttonPanel.Controls.Add(_queryListToggleButton);
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
            RowCount = 4
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(CreateWorkspaceHeaderPanel(), 0, 0);
        panel.Controls.Add(_workspaceListBox, 0, 1);
        panel.Controls.Add(CreateQueryHeaderPanel(), 0, 2);
        panel.Controls.Add(_queryListBox, 0, 3);
        return panel;
    }

    /// <summary>
    /// ワークスペース一覧ヘッダーを作る。
    /// </summary>
    private FlowLayoutPanel CreateWorkspaceHeaderPanel()
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
            Text = "ワークスペース一覧"
        });
        panel.Controls.Add(CreateWorkspaceActionButton("追加", WorkspaceAddButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("名前変更", WorkspaceRenameButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("削除", WorkspaceDeleteButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("↑", WorkspaceMoveUpButton_Click));
        panel.Controls.Add(CreateWorkspaceActionButton("↓", WorkspaceMoveDownButton_Click));
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
    /// ワークスペース一覧を作る。
    /// 選択と並べ替えを同じ一覧で扱い、ドラッグ移動も有効にする。
    /// </summary>
    private ListBox CreateWorkspaceListBox()
    {
        var listBox = new ListBox
        {
            AllowDrop = true,
            Dock = DockStyle.Top,
            Font = new Font("Yu Gothic UI", 9F),
            Height = 96,
            IntegralHeight = false
        };

        listBox.SelectedIndexChanged += WorkspaceListBox_SelectedIndexChanged;
        listBox.MouseDown += WorkspaceListBox_MouseDown;
        listBox.MouseMove += WorkspaceListBox_MouseMove;
        listBox.DragEnter += WorkspaceListBox_DragEnter;
        listBox.DragOver += WorkspaceListBox_DragOver;
        listBox.DragDrop += WorkspaceListBox_DragDrop;
        return listBox;
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
    /// 画面ヘッダーへ置く一覧開閉ボタンを作る。
    /// ワークスペースパネルの表示量を素早く切り替えるため、上部ツールバーへ出す。
    /// </summary>
    private static Button CreateWorkspaceHeaderToggleButton(EventHandler onClick)
    {
        var button = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(12, 6, 12, 6),
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
            _workspaceListBox.BeginUpdate();
            try
            {
                _workspaceListBox.Items.Clear();
                foreach (var workspace in _workspaceState.Workspaces)
                {
                    _workspaceListBox.Items.Add(new WorkspaceListItem(workspace));
                }
            }
            finally
            {
                _workspaceListBox.EndUpdate();
            }

            var selectedWorkspaceIndex = _workspaceState.Workspaces.FindIndex(
                workspace => workspace.Id == _workspaceState.SelectedWorkspaceId);
            _workspaceListBox.SelectedIndex = selectedWorkspaceIndex >= 0 ? selectedWorkspaceIndex : 0;

            BindQueryListBox();
            ApplyWorkspaceListVisibility();
            UpdateWorkspaceHeaderToggleButtons();
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
    /// 一覧の開閉状態を UI へ反映する。
    /// 非表示時は ListBox 自体を隠し、上部ヘッダーだけ残して縦幅を節約する。
    /// </summary>
    private void ApplyWorkspaceListVisibility()
    {
        _workspaceListBox.Visible = _workspaceState.IsWorkspaceListExpanded;
        _queryListBox.Visible = _workspaceState.IsQueryListExpanded;
    }

    /// <summary>
    /// 画面ヘッダー上のトグルボタン文言を現在状態へ合わせる。
    /// </summary>
    private void UpdateWorkspaceHeaderToggleButtons()
    {
        _workspaceListToggleButton.Text = _workspaceState.IsWorkspaceListExpanded
            ? "ワークスペース一覧 ▼"
            : "ワークスペース一覧 ▶";
        _queryListToggleButton.Text = _workspaceState.IsQueryListExpanded
            ? "クエリ一覧 ▼"
            : "クエリ一覧 ▶";
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
    private void WorkspaceListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressWorkspaceUiEvents || _workspaceListBox.SelectedItem is not WorkspaceListItem item)
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
        if (_workspaceListBox.SelectedItem is not WorkspaceListItem item)
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
        if (_workspaceListBox.SelectedItem is not WorkspaceListItem item)
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
    /// 選択中ワークスペースを 1 つ上へ移動する。
    /// </summary>
    private void WorkspaceMoveUpButton_Click(object? sender, EventArgs e)
    {
        if (_workspaceListBox.SelectedItem is not WorkspaceListItem item)
        {
            return;
        }

        var currentIndex = _workspaceState.Workspaces.FindIndex(workspace => workspace.Id == item.Workspace.Id);
        if (currentIndex <= 0)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.MoveWorkspace(_workspaceState, item.Workspace.Id, currentIndex - 1);
        BindWorkspaceControls();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// 選択中ワークスペースを 1 つ下へ移動する。
    /// </summary>
    private void WorkspaceMoveDownButton_Click(object? sender, EventArgs e)
    {
        if (_workspaceListBox.SelectedItem is not WorkspaceListItem item)
        {
            return;
        }

        var currentIndex = _workspaceState.Workspaces.FindIndex(workspace => workspace.Id == item.Workspace.Id);
        if (currentIndex < 0 || currentIndex >= _workspaceState.Workspaces.Count - 1)
        {
            return;
        }

        _workspaceState = _workspaceStateManager.MoveWorkspace(_workspaceState, item.Workspace.Id, currentIndex + 1);
        BindWorkspaceControls();
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
    /// ワークスペース一覧の表示・非表示を切り替える。
    /// </summary>
    private void WorkspaceListToggleButton_Click(object? sender, EventArgs e)
    {
        _workspaceState = _workspaceStateManager.ToggleWorkspaceListExpanded(_workspaceState);
        ApplyWorkspaceListVisibility();
        UpdateWorkspaceHeaderToggleButtons();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// クエリ一覧の表示・非表示を切り替える。
    /// </summary>
    private void QueryListToggleButton_Click(object? sender, EventArgs e)
    {
        _workspaceState = _workspaceStateManager.ToggleQueryListExpanded(_workspaceState);
        ApplyWorkspaceListVisibility();
        UpdateWorkspaceHeaderToggleButtons();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// ドラッグ開始候補のワークスペース位置を記録する。
    /// </summary>
    private void WorkspaceListBox_MouseDown(object? sender, MouseEventArgs e)
    {
        _workspaceDragSourceIndex = _workspaceListBox.IndexFromPoint(e.Location);
        _workspaceDragStartPoint = e.Location;
    }

    /// <summary>
    /// 一定距離動いたらドラッグ移動を開始する。
    /// </summary>
    private void WorkspaceListBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left
            || _workspaceDragSourceIndex < 0
            || _workspaceDragSourceIndex >= _workspaceListBox.Items.Count
            || _workspaceListBox.Items[_workspaceDragSourceIndex] is not WorkspaceListItem item)
        {
            return;
        }

        var dragBounds = new Rectangle(
            _workspaceDragStartPoint.X - SystemInformation.DragSize.Width / 2,
            _workspaceDragStartPoint.Y - SystemInformation.DragSize.Height / 2,
            SystemInformation.DragSize.Width,
            SystemInformation.DragSize.Height);
        if (dragBounds.Contains(e.Location))
        {
            return;
        }

        _workspaceListBox.DoDragDrop(item.Workspace.Id, DragDropEffects.Move);
        _workspaceDragSourceIndex = -1;
    }

    /// <summary>
    /// ワークスペース ID ドラッグだけを受け付ける。
    /// </summary>
    private void WorkspaceListBox_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(string)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    /// <summary>
    /// ドラッグ中のカーソルに移動可能を示す。
    /// </summary>
    private void WorkspaceListBox_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(string)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    /// <summary>
    /// ドロップ位置に合わせてワークスペース順を入れ替える。
    /// </summary>
    private void WorkspaceListBox_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(string)) is not string workspaceId)
        {
            return;
        }

        var sourceIndex = _workspaceState.Workspaces.FindIndex(workspace => workspace.Id == workspaceId);
        if (sourceIndex < 0)
        {
            return;
        }

        var clientPoint = _workspaceListBox.PointToClient(new Point(e.X, e.Y));
        var insertionIndex = GetWorkspaceInsertionIndex(clientPoint);
        var targetIndex = sourceIndex < insertionIndex
            ? insertionIndex - 1
            : insertionIndex;
        targetIndex = Math.Clamp(targetIndex, 0, _workspaceState.Workspaces.Count - 1);

        _workspaceState = _workspaceStateManager.MoveWorkspace(_workspaceState, workspaceId, targetIndex);
        BindWorkspaceControls();
        ScheduleWorkspaceSave();
    }

    /// <summary>
    /// ドロップ位置の挿入インデックスを求める。
    /// アイテムの上半分なら手前、下半分なら次位置へ入れる。
    /// </summary>
    private int GetWorkspaceInsertionIndex(Point clientPoint)
    {
        if (_workspaceListBox.Items.Count == 0)
        {
            return 0;
        }

        for (var index = 0; index < _workspaceListBox.Items.Count; index++)
        {
            var bounds = _workspaceListBox.GetItemRectangle(index);
            var middleY = bounds.Top + bounds.Height / 2;
            if (clientPoint.Y < middleY)
            {
                return index;
            }

            if (clientPoint.Y <= bounds.Bottom)
            {
                return index + 1;
            }
        }

        return _workspaceListBox.Items.Count;
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
