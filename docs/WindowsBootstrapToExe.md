# Windows で単一ファイルから exe を作る手順

このドキュメントは、リポジトリを clone できない端末向けの手順書。
`TSqlAnalyzer.Bootstrap.csproj` という 1 ファイルだけを保存し、その後にプロダクションコードの展開、build、`exe` 生成、起動確認まで進める流れをまとめる。

## 最初に押さえるポイント

- bootstrap 展開物には `tests/` を含めない
- そのため、展開先では xUnit を前提にしない
- 展開後に build する対象は `TSqlAnalyzer.Runtime.slnx`
- 開発端末で test を回すときだけ `TSqlAnalyzer.slnx` と `tests/` を使う

言い換えると、clone できない端末は実行確認専用、clone できる端末は開発専用という分担にしている。

## この手順でやること

1. 単一ファイル bootstrap を保存する
2. bootstrap を実行してプロダクションコード一式を展開する
3. 展開されたコードを build する
4. WinForms アプリの単一ファイル `exe` を生成する
5. 生成した `exe` を起動して動作確認する

## 前提

- Windows 11 または Windows 10
- PowerShell
- .NET SDK 10 系
- NuGet へアクセスできるネットワーク

## 最短手順

PowerShell で次を順に実行する。

### 1. 作業用フォルダを作る

```powershell
mkdir C:\work\tsql-analyzer-bootstrap
cd C:\work\tsql-analyzer-bootstrap
```

### 2. bootstrap ファイルを保存する

配布された `TSqlAnalyzer.Bootstrap.csproj` の内容を、現在のフォルダに同名で保存する。

保存直後のイメージ:

```text
C:\work\tsql-analyzer-bootstrap
  TSqlAnalyzer.Bootstrap.csproj
```

### 3. bootstrap を実行する

```powershell
dotnet build .\TSqlAnalyzer.Bootstrap.csproj
```

このコマンドで次が自動実行される。

- `.\extracted\TSqlAnalyzer\` へプロダクションコード一式を展開
- 展開先の `TSqlAnalyzer.Runtime.slnx` を `dotnet build`

ここでは test は実行しない。`tests/` 自体を展開していないため。

### 4. 展開されたソースへ移動する

```powershell
cd .\extracted\TSqlAnalyzer
```

### 5. build を自分でも確認する

bootstrap 実行直後に既定で build は走っているが、明示的に確認したい場合は次を実行する。

```powershell
dotnet build .\TSqlAnalyzer.Runtime.slnx -c Release
```

### 6. 単一ファイル exe を生成する

```powershell
dotnet publish .\src\TSqlAnalyzer.WinForms\TSqlAnalyzer.WinForms.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

### 7. 生成された exe を起動する

```powershell
.\src\TSqlAnalyzer.WinForms\bin\Release\net10.0-windows\win-x64\publish\TSqlAnalyzer.WinForms.exe
```

## フォルダ構成の見え方

### bootstrap 実行前

```text
C:\work\tsql-analyzer-bootstrap
  TSqlAnalyzer.Bootstrap.csproj
```

### bootstrap 実行後

```text
C:\work\tsql-analyzer-bootstrap
  TSqlAnalyzer.Bootstrap.csproj
  extracted
    TSqlAnalyzer
      README.md
      TSqlAnalyzer.Runtime.slnx
      src
      docs
```

重要:

- `tests` フォルダは作られない
- `TSqlAnalyzer.slnx` も作られない
- 展開先は実行用コードだけに絞ってある

### publish 後

```text
C:\work\tsql-analyzer-bootstrap\extracted\TSqlAnalyzer
  src
    TSqlAnalyzer.WinForms
      bin
        Release
          net10.0-windows
            win-x64
              publish
                TSqlAnalyzer.WinForms.exe
```

## 手順の意味を整理すると

### clone できない端末

- `TSqlAnalyzer.Bootstrap.csproj` を 1 ファイル保存する
- `TSqlAnalyzer.Runtime.slnx` を build する
- `WinForms` を publish して `exe` を作る
- test は持ち込まない

### clone できる開発端末

- リポジトリをそのまま使う
- `TSqlAnalyzer.slnx` を対象に `dotnet test` を回す
- xUnit を使って TDD で実装を進める

## bootstrap を展開専用として使いたい場合

展開だけ行い、展開先の build は後で手動実行したい場合は次を使う。

```powershell
dotnet build .\TSqlAnalyzer.Bootstrap.csproj `
  -p:RunExtractedBuild=false
```

## 展開先を変えたい場合

```powershell
dotnet build .\TSqlAnalyzer.Bootstrap.csproj `
  -p:ExtractRoot=D:\temp\TSqlAnalyzer
```

この場合、ソースは `D:\temp\TSqlAnalyzer` に展開される。

## build 成果物と主な出力先

### build

```powershell
dotnet build .\TSqlAnalyzer.Runtime.slnx -c Release
```

主な出力先:

- `.\src\TSqlAnalyzer.Domain\bin\Release\net10.0\`
- `.\src\TSqlAnalyzer.Application\bin\Release\net10.0\`
- `.\src\TSqlAnalyzer.WinForms\bin\Release\net10.0-windows\`

### publish

```powershell
dotnet publish .\src\TSqlAnalyzer.WinForms\TSqlAnalyzer.WinForms.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

出力先:

- `.\src\TSqlAnalyzer.WinForms\bin\Release\net10.0-windows\win-x64\publish\`

主な生成物:

- `TSqlAnalyzer.WinForms.exe`
- `TSqlAnalyzer.WinForms.pdb`
- 必要に応じた runtime 関連ファイル

## build / publish に必要なソース一覧

bootstrap 展開物に含まれるのは次の系統。

- `TSqlAnalyzer.Runtime.slnx`
- `README.md`
- `docs/WindowsBootstrapToExe.md`
- `src/TSqlAnalyzer.Domain/**`
- `src/TSqlAnalyzer.Application/**`
- `src/TSqlAnalyzer.WinForms/**`

含まれないもの:

- `tests/**`
- `TSqlAnalyzer.slnx`

## 動作確認のしかた

アプリ起動後、次の SQL を貼り付けて `解析` を押す。

```sql
WITH recent_orders AS (
    SELECT
        o.UserId,
        o.OrderId
    FROM dbo.Orders o
)
SELECT
    ro.UserId,
    invoice_total.TotalAmount
FROM recent_orders ro
INNER JOIN (
    SELECT
        i.UserId,
        SUM(i.Amount) AS TotalAmount
    FROM dbo.InvoiceItems i
    GROUP BY i.UserId
) invoice_total
    ON ro.UserId = invoice_total.UserId
WHERE EXISTS (
    SELECT 1
    FROM dbo.Payments p
    WHERE p.UserId = ro.UserId
);
```

確認ポイント:

- ウィンドウが起動する
- 入力欄へ SQL を貼り付けられる
- `解析` ボタンで TreeView が更新される
- `共通テーブル式` ノードが表示される
- `共通テーブル式` 配下に `参照関係` が表示される
- `共通テーブル式` 配下に `依存順` が表示される
- `結合` 配下に `JOIN #1` が表示される
- `結合先の内部構造` が表示される
- `抽出条件` 配下に `条件論理` が表示される
- `条件論理` 配下に `述語種別: EXISTS` や `述語種別: 比較` などが表示される
- `条件論理` 配下に `比較種別: 等価 (=)` や `比較種別: 以上 (>=)` などが表示される
- `抽出条件` 配下に `条件種別` と `内部クエリ` が表示される

## つまずきやすい点

### xUnit が入っていない端末でも大丈夫か

大丈夫。bootstrap 展開物には test プロジェクトを含めないため、xUnit を前提にしない。

### `dotnet` が見つからない

.NET SDK 10 系が入っていないか、PATH が通っていない。`dotnet --info` で確認する。

### NuGet 復元で失敗する

ネットワーク制限やプロキシ設定の可能性がある。`dotnet restore` が通る環境で再実行する。

### exe が見つからない

`publish` が成功していれば、既定では次に生成される。

```text
.\src\TSqlAnalyzer.WinForms\bin\Release\net10.0-windows\win-x64\publish\TSqlAnalyzer.WinForms.exe
```

### bootstrap は通ったが WinForms が起動しない

Windows 環境で実行しているか確認する。WinForms は Windows 前提。
