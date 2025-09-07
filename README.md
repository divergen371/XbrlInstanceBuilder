# Xbrl.InstanceBuilder

F# ライブラリ。EDINET（日本）のタクソノミに基づく XBRL インスタンス文書を簡潔に生成します。

- explicitMember の大文字小文字（M は大文字）に準拠
- dimension 属性は接頭辞つき QName を出力（例: `jppfs_cor:ConsolidatedOrNonConsolidatedAxis`）
- 名前空間宣言（xbrli/xbrldi/iso4217/jppfs_cor/jpcrp_cor/jpdei_cor、link/xlink）を自動付与
- 既定の schemaRef（2020-11-01 の jppfs エントリポイント）を自動付与（上書き可）

## インストール

NuGet 公開後:

```bash
 dotnet add package Xbrl.InstanceBuilder --version 0.2.0
```

## 使い方（F#）

```fsharp
open System
open System.Xml.Linq
open Xbrl.InstanceBuilder

let args : InstanceBuilder.BuildArgs =
  let axisMember : InstanceBuilder.AxisMember =
    { Axis = InstanceBuilder.Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
      Member = InstanceBuilder.Ns.jppfs + "ConsolidatedMember" }
  {
    Lei = "5493001KJTIIGC8Y1R12"
    ContextId = "CurrentYearDuration_Consolidated"
    Period = InstanceBuilder.Period.Duration(DateTime(2024,4,1), DateTime(2025,3,31))
    Dimensions = [ axisMember ]
    UnitId = "UJPY"
    MeasureQName = "iso4217:JPY"
    Facts = [
      { QName = InstanceBuilder.Ns.jppfs + "NetSales"
        ContextRef = "CurrentYearDuration_Consolidated"
        UnitRef = "UJPY"
        Decimals = -3
        Rounding = InstanceBuilder.RoundingPolicy.Truncate
        Value = 1234000000m }
    ]
    // None の場合、設定ファイルやフォールバックの既定により schemaRef を付与
    SchemaRefHref = None
  }

let doc : XDocument = InstanceBuilder.buildDocument args
// 保存は利用側の責務
let path = "sample-edinet-instance.xml"
doc.Save(path)
```

## API 概要

- `InstanceBuilder.Ns` — 代表的な XML 名前空間（xbrli/xbrldi/iso4217/jppfs/jpcrp/jpdei/link/xlink）
- `InstanceBuilder.Period` — 期間表現（瞬時 or 期間）
- `InstanceBuilder.AxisMember` — ディメンション（Axis と Member を XName で）
- `InstanceBuilder.MonetaryFactArg` — 金額ファクトの引数レコード（`Decimals` と `Rounding` を指定）
- `InstanceBuilder.BuildArgs` — 文書生成の引数レコード（`Dimensions` は `AxisMember list`）
- `InstanceBuilder.buildDocument : BuildArgs -> XDocument` — 生成本体
- `InstanceBuilder.buildDocumentWithConfig : string option -> BuildArgs -> XDocument` — 設定ファイル経由で schemaRef を解決してから生成
- `InstanceBuilder.buildDocumentWithResolver : Resolver.SchemaRefResolver -> BuildArgs -> XDocument` — 任意のリゾルバ関数で schemaRef を解決
- `InstanceBuilder.buildDocumentWithNamespaces : PrefixMap -> BuildArgs -> XDocument` — ルートの xmlns と explicitMember の QName を外部指定のプレフィックスで生成
- `InstanceBuilder.tryBuildDocument : BuildArgs -> Result<XDocument, BuildError list>` — 入力検証つき生成
- `InstanceBuilder.Config` — 設定読み込み/URL 生成（`EntryPoint`, `Settings`, `schemaRefUrl`, `tryLoad`, `tryGetDefaultSchemaRefHref`）
- `InstanceBuilder.Preset` — エントリポイント種別のプリセット（`epAsr`, `qsr`, `srs`, `ssr`, `jppfs`, `jpdei` など）
- `InstanceBuilder.Builder` — 重複排除と自動ID付きのビルダーAPI（`init`, `addContext`, `addUnit`, `addFact`, `addFactWithRefs`, `build`）
- `InstanceBuilder.Resolver` — リゾルバ作成（`fromConfigFile`, `fromConst`, `empty`）
- `InstanceBuilder.Validation` — 軽量検証（`validateDocument` と `ValidationError`）

## schemaRef 既定

`BuildArgs.SchemaRefHref = None` のとき、以下を自動付与します。

```
<link:schemaRef xlink:type="simple" xlink:href="https://disclosure.edinet-fsa.go.jp/taxonomy/jppfs/2020-11-01/jppfs_cor_2020-11-01.xsd" />
```

年度やエントリポイントを変更したい場合は `SchemaRefHref = Some "...xsd"` を指定してください。

## 設定ファイルで schemaRef を管理

設定ファイル（JSON）や環境変数で、年度やエントリポイントの既定を外出しできます。

- 探索順:
  1. `buildDocumentWithConfig (Some "path/to/xbrl.instancebuilder.json")`
  2. 環境変数 `XBRL_INSTANCEBUILDER_CONFIG`
  3. カレントディレクトリの `./xbrl.instancebuilder.json`

- JSON例（ASR: 有価証券報告書）

```json
{
  "taxonomyDate": "2020-11-01",
  "entryPoint": { "kind": "ASR" }
}
```

- JSON例（JPPFS: 財務諸表“本表だけ”、一般商工業=cai）

```json
{
  "taxonomyDate": "2020-11-01",
  "entryPoint": { "kind": "JPPFS", "industry": "cai" }
}
```

- 呼び出し例（F#）

```fsharp
let doc = InstanceBuilder.buildDocumentWithConfig None args
// None の場合: 環境変数 -> ./xbrl.instancebuilder.json の順に探索

let doc2 = InstanceBuilder.buildDocumentWithConfig (Some "/path/to/xbrl.instancebuilder.json") args

// 入力検証つき
match InstanceBuilder.tryBuildDocument args with
| Ok xdoc -> xdoc.Save("sample.xml")
| Error errs -> printfn "%A" errs
```

## プリセット（エントリポイント種別のヘルパー）

コードから直接エントリポイント URL を作りたい場合、`InstanceBuilder.Config.Settings` を `Preset` で簡単に組み立てられます。

```fsharp
open Xbrl.InstanceBuilder
open Xbrl.InstanceBuilder.Config

let settingsAsr = Preset.epAsr "2020-11-01"
let settingsQsr = Preset.qsr  "2020-11-01"
let settingsSrs = Preset.srs  "2020-11-01" (Some "srs")   // variant は様式に応じて
let settingsSsr = Preset.ssr  "2020-11-01" (Some "ssr")
let settingsPfs = Preset.jppfs "2020-11-01" "cai"           // 一般商工業
let settingsDei = Preset.jpdei "2020-11-01"

let url = Config.schemaRefUrl settingsAsr
```

## 任意のプレフィックス（xmlns）指定で出力

`buildDocumentWithNamespaces` に `PrefixMap`（`Map<string /*namespaceUri*/, string /*prefix*/>`）を渡すと、
ルートの xmlns 宣言と explicitMember の QName をそのプレフィックスで生成します。

```fsharp
open Xbrl.InstanceBuilder

let prefixes : InstanceBuilder.PrefixMap =
  [ string InstanceBuilder.Ns.jppfs, "pfs"
    string InstanceBuilder.Ns.jpcrp, "crp"
    string InstanceBuilder.Ns.jpdei, "dei" ]
  |> Map.ofList

let xdoc = InstanceBuilder.buildDocumentWithNamespaces prefixes args
```

注:
- `buildDocumentWithNamespaces` は既定で `xbrli/xbrldi/iso4217/link/xlink` だけを宣言します。
- `jppfs/jpcrp/jpdei` などタクソノミ固有のプレフィックスは `PrefixMap` に含めてください。

## Builder API（重複排除と自動ID）

Context/Unit の重複を自動で排除し、ID を自動採番してくれるビルダーです。I/O は行いません。

```fsharp
open System
open System.Xml.Linq
open Xbrl.InstanceBuilder

// スキーマ参照（schemaRef）は None だと既定（jppfs 2020-11-01）
let b0 = Builder.init "5493001KJTIIGC8Y1R12" None

let period = InstanceBuilder.Period.Duration(DateTime(2024,4,1), DateTime(2025,3,31))
let dims = [ { Axis = InstanceBuilder.Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis"
               Member = InstanceBuilder.Ns.jppfs + "ConsolidatedMember" } ]

// ファクトを追加（Context/Unit は自動確保・再利用）。戻り値に contextId/unitId も得られる
let b1, ctxId, unitId =
  Builder.addFact (InstanceBuilder.Ns.jppfs + "NetSales") period dims "iso4217:JPY" -3 InstanceBuilder.RoundingPolicy.Truncate 1234000000m b0

// ドキュメントを構築
let xdoc : XDocument = Builder.build b1
```

## C# からの利用例

```csharp
using System;
using System.Xml.Linq;
using Xbrl.InstanceBuilder;

var axis = InstanceBuilder.Ns.jppfs + "ConsolidatedOrNonConsolidatedAxis";
var member = InstanceBuilder.Ns.jppfs + "ConsolidatedMember";
var am = InstanceBuilder.CSharp.newAxisMember(axis, member);

var args = new InstanceBuilder.BuildArgs {
  Lei = "5493001KJTIIGC8Y1R12",
  ContextId = "CurrentYearDuration_Consolidated",
  Period = InstanceBuilder.Period.Duration(new DateTime(2024,4,1), new DateTime(2025,3,31)),
  Dimensions = new System.Collections.Generic.List<InstanceBuilder.AxisMember> { am },
  UnitId = "UJPY",
  MeasureQName = "iso4217:JPY",
  Facts = new System.Collections.Generic.List<InstanceBuilder.MonetaryFactArg> {
    InstanceBuilder.CSharp.newMonetaryFactArg(
      InstanceBuilder.Ns.jppfs + "NetSales",
      "CurrentYearDuration_Consolidated",
      "UJPY",
      -3,
      InstanceBuilder.RoundingPolicy.Truncate,
      1234000000m)
  },
  SchemaRefHref = null
};

// 検証付き
var outcome = InstanceBuilder.CSharp.tryBuild(args);
if (!outcome.Success) Console.WriteLine(string.Join(", ", outcome.Errors));
else outcome.Document.Save("sample.xml");

// 設定パスを使ったビルド
XDocument docCfg = InstanceBuilder.CSharp.buildWithConfigPath("./xbrl.instancebuilder.json", args);

// 任意のプレフィックス
var prefixes = new System.Collections.Generic.Dictionary<string,string> {
  [InstanceBuilder.Ns.jppfs.ToString()] = "pfs",
  [InstanceBuilder.Ns.jpcrp.ToString()] = "crp",
  [InstanceBuilder.Ns.jpdei.ToString()] = "dei",
};
XDocument docNs = InstanceBuilder.CSharp.buildWithNamespaces(prefixes, args);
```

## Validation（軽量検証）

`Validation.validateDocument` は、次のような軽量な検査を行い、`ValidationError list` を返します。

- MissingSchemaRef — ルートに `link:schemaRef` が無い
- MissingNamespacePrefix prefix — `explicitMember` の `dimension` または内容の QName で使用されるプレフィックスが未宣言
- InvalidExplicitMember detail — `explicitMember` の `dimension` または内容が QName 形式でない

使用例（F#）:

```fsharp
open Xbrl.InstanceBuilder
let errors = Validation.validateDocument doc
match errors with
| [] -> printfn "OK"
| es -> printfn "%A" es
```

## ライセンス

MIT

## 開発

```bash
# ビルド
 dotnet build

# パッケージ作成
 dotnet pack -c Release
```

`Xbrl.InstanceBuilder.fsproj` の `RepositoryUrl`/`PackageProjectUrl` は、実リポジトリ URL に置き換えてください。

## セキュリティ注意（http/https について）

- schemaRef など、実際にフェッチされる可能性のある URL は https を使用します（本ライブラリは https を既定化し、http 指定でも https へ強制変換します）。
- 一方、XML 名前空間 URI（例: `http://www.xbrl.org/2003/instance` や EDINET タクソノミの namespace URI）は「識別子（ID）」であり、仕様で定義された固定文字列です。これらは https に変更してはいけません（検証や互換性の観点から不適合になる可能性があります）。
