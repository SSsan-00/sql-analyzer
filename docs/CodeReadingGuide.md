# コードリーディングガイド

このドキュメントは、T-SQL解析ツールのコードをどこから読むと全体像をつかみやすいかをまとめた開発者向けガイド。
実装は `Domain` / `Application` / `WinForms` に責務を分けているため、入口と流れを押さえると追いやすい。

## 最初に見るファイル

1. `src/TSqlAnalyzer.WinForms/Program.cs`
   - アプリ起動時の依存関係組み立てを読む
2. `src/TSqlAnalyzer.WinForms/MainForm.cs`
   - ボタンクリック、検索、選択連動など画面イベントの入口を読む
3. `src/TSqlAnalyzer.Application/Services/QueryAnalysisService.cs`
   - UI から呼ばれる解析の公開入口を読む
4. `src/TSqlAnalyzer.Application/Analysis/ScriptDomQueryAnalyzer.cs`
   - ScriptDom の AST を独自モデルへ変換する中心実装を読む
5. `src/TSqlAnalyzer.Application/Presentation/QueryAnalysisTreeBuilder.cs`
   - 解析モデルを TreeView 向け表示ノードへ変換する流れを読む
6. `src/TSqlAnalyzer.WinForms/UI/AnalysisTreeViewBinder.cs`
   - 表示ノードを WinForms `TreeNode` へ落とし込む処理を読む
7. `src/TSqlAnalyzer.Application/Editor/ParseIssueTextSpanResolver.cs`
   - 構文エラーの行・列を SQL 文字範囲へ変換する処理を読む
8. `src/TSqlAnalyzer.Application/Editor/SqlInputAssistService.cs`
   - DB 非依存の入力補助候補を組み立てる処理を読む
9. `src/TSqlAnalyzer.Application/Workspace/WorkspaceStateManager.cs`
   - ワークスペース、クエリ一覧、展開状態の不変条件を読む
10. `src/TSqlAnalyzer.WinForms/MainForm.Workspace.cs`
    - ワークスペース UI、保存復元、並べ替え、ヘッダートグルの結線を読む

## レイヤーごとの役割

### Domain

- `src/TSqlAnalyzer.Domain/Analysis/QueryAnalysisModels.cs`
  - 解析結果の中立モデルを定義する
  - `SelectQueryAnalysis`、`JoinAnalysis`、`ConditionExpressionAnalysis` などを保持する
  - UI 型や ScriptDom 型を持ち込まない

### Application

- `Analysis`
  - ScriptDom から独自モデルへ変換する
- `Presentation`
  - 独自モデルから TreeView 表示用ノードへ変換する
- `Formatting`
  - SQL 整形を担当する
- `Editor`
  - 構文エラー位置の解決と DB 非依存の入力補助を担当する
- `Export`
  - 列情報エクスポートを担当する
- `Workspace`
  - ワークスペース状態、クエリ一覧、JSON 保存復元を担当する
- `Services`
  - UI から使う公開サービスをまとめる

### WinForms

- `MainForm`
  - 入力、ボタン、ショートカット、選択連動を管理する
- `UI/*`
  - TreeView の見た目、画像、色、展開状態の反映を担当する

## 主要フローの追い方

### 1. `解析` ボタン

1. `MainForm.AnalyzeButton_Click`
2. `QueryAnalysisService.Analyze`
3. `ScriptDomQueryAnalyzer.Analyze`
4. `QueryAnalysisTreeBuilder.Build`
5. `AnalysisTreeViewBinder.Bind`

この流れを追うと、入力 SQL がどのように解析モデルへ変換され、最終的に TreeView に出るかがわかる。

### 2. `整形` ボタン

1. `MainForm.FormatButton_Click`
2. `SqlFormattingService.Format`

`SqlFormattingService` は `SELECT` 系を自前整形し、それ以外は ScriptDom の生成結果へフォールバックする。
`CASE`、サブクエリ、派生テーブル、`JOIN ... ON`、`LIKE / BETWEEN / IS NULL` の複雑条件に加えて、`CASE` の `WHEN` 条件が `AND / OR` で結合される場面を重点的に読めば、整形ルールの設計意図を追いやすい。

### 3. `列情報エクスポート`

1. `MainForm.ExportTextButton_Click`
2. `ColumnTextExportBuilder.Build`

SELECT / INSERT / UPDATE のどの情報をテキストへ落としているかを確認する入口になる。

### 4. 構文エラー表示と入力補助

1. `MainForm.ShowParseIssues`
2. `ParseIssueTextSpanResolver.TryResolve`
3. `MainForm.RefreshCompletionPopup`
4. `SqlInputAssistService.GetSuggestions`

構文エラーの赤ハイライトと、`Ctrl+Space` から開く補完候補の組み立てを追える。

### 5. ワークスペースと前回状態の復元

1. `Program.cs`
2. `JsonWorkspaceStateStore.Load`
3. `MainForm.InitializeWorkspaceState`
4. `MainForm.Workspace.cs` の各操作ハンドラー

ワークスペース切替、クエリ一覧、ヘッダートグル、再起動後復元の流れを追える。

### 6. SQL と TreeView の相互選択

1. `MainForm` 内の SQL 選択イベント
2. `DisplayTreeNodeNavigator`
3. `AnalysisTreeViewBinder`

位置情報は `QueryAnalysisModels.cs` の `SourceSpan` 系レコードで保持している。

## 目的別の読む順番

### TreeView に何が出るかを知りたい

1. `QueryAnalysisTreeBuilder.cs`
2. `DisplayTreeNode.cs`
3. `DisplayTreeNodeKindCatalog.cs`
4. `AnalysisTreeViewBinder.cs`

### 新しい SQL 構文を解析対象へ追加したい

1. `QueryAnalysisModels.cs`
2. `ScriptDomQueryAnalyzer.cs`
3. `QueryAnalysisTreeBuilder.cs`
4. 対応する xUnit テスト

### SQL 整形ルールを変えたい

1. `SqlFormattingService.cs`
2. `tests/TSqlAnalyzer.Tests/Formatting/SqlFormattingServiceTests.cs`

### CTE や依存関係の扱いを追いたい

1. `CommonTableExpressionDependencyAnalyzer.cs`
2. `ScriptDomQueryAnalyzer.cs`
3. `QueryAnalysisTreeBuilder.cs`

## テストの見方

- `tests/TSqlAnalyzer.Tests/Analysis/QueryAnalysisServiceTests.cs`
  - 解析結果モデルの期待値を確認する
- `tests/TSqlAnalyzer.Tests/Presentation/QueryAnalysisTreeBuilderTests.cs`
  - TreeView 向け表示ノードの構成を確認する
- `tests/TSqlAnalyzer.Tests/Formatting/SqlFormattingServiceTests.cs`
  - SQL 整形のルールを確認する
- `tests/TSqlAnalyzer.Tests/Presentation/DisplayTreeExpansionPolicyTests.cs`
  - TreeView の初期展開ポリシーを確認する

実装を読むときは、先にテスト名を眺めてから本体へ入ると仕様がつかみやすい。

## 変更時の基本方針

- ScriptDom の型を WinForms へ直接渡さない
- 解析ロジックを `MainForm` に書かない
- 新しい解析結果はまず Domain モデルへ置く
- 表示要件は `QueryAnalysisTreeBuilder` で吸収する
- TDD を崩さず、先にテストを追加してから最小実装を入れる

## 迷ったときの基準

- これは構文上の事実か
  - 事実なら Domain / Application へ入れる
- これは見せ方か
  - 見せ方なら Presentation / WinForms 側で扱う
- これは操作イベントか
  - 操作イベントなら `MainForm` を入口にする

この境界を守ると、解析ロジックの追加と UI 改修を分離しやすい。
