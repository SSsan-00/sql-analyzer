using TSqlAnalyzer.Application.Presentation;
using TSqlAnalyzer.Application.Services;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// メイン画面。
/// フォームは入力取得とイベント処理に責務を絞り、解析そのものはサービスへ委譲する。
/// </summary>
public partial class MainForm : Form
{
    private readonly IQueryAnalysisService _analysisService;
    private readonly QueryAnalysisTreeBuilder _treeBuilder;

    /// <summary>
    /// 必要なサービスを受け取って初期化する。
    /// </summary>
    public MainForm(IQueryAnalysisService analysisService, QueryAnalysisTreeBuilder treeBuilder)
    {
        _analysisService = analysisService;
        _treeBuilder = treeBuilder;

        InitializeComponent();
    }

    /// <summary>
    /// 解析ボタン押下時の処理。
    /// 入力 SQL を解析し、TreeView 用モデルへ変換して画面へ反映する。
    /// </summary>
    private void AnalyzeButton_Click(object? sender, EventArgs e)
    {
        var analysis = _analysisService.Analyze(sqlTextBox.Text);
        var tree = _treeBuilder.Build(analysis);

        AnalysisTreeViewBinder.Bind(resultTreeView, tree);
    }

    /// <summary>
    /// 入力欄と表示結果をまとめて初期化する。
    /// </summary>
    private void ClearButton_Click(object? sender, EventArgs e)
    {
        sqlTextBox.Clear();
        resultTreeView.Nodes.Clear();
        sqlTextBox.Focus();
    }
}
