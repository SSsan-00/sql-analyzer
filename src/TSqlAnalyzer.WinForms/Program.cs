using TSqlAnalyzer.Application.Analysis;
using TSqlAnalyzer.Application.Export;
using TSqlAnalyzer.Application.Presentation;
using TSqlAnalyzer.Application.Services;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// アプリケーションの起動点。
/// 初期版ではここで依存関係を手動で組み立て、MainForm へ渡す。
/// </summary>
internal static class Program
{
    /// <summary>
    /// WinForms アプリケーションを起動する。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var analysisService = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var treeBuilder = new QueryAnalysisTreeBuilder();
        var columnTextExportBuilder = new ColumnTextExportBuilder();

        System.Windows.Forms.Application.Run(new MainForm(analysisService, treeBuilder, columnTextExportBuilder));
    }
}
