# 変更履歴（Changelog）

このファイルには、このプロジェクトの重要な変更点を記録します。

表記は Keep a Changelog に準拠し、バージョニングは Semantic Versioning に従います。

## [Unreleased]
- CI: Linux/Windows/macOS のマトリクスでビルド・テスト。テスト結果（.trx）とカバレッジ（Cobertura）をアーティファクトに保存。ジョブサマリーにカバレッジ要約を出力。
- エディタ: `.editorconfig` を追加（LF, UTF-8, 4スペース等で体裁を統一）。
- ハウスキーピング: `.gitignore` を拡充、Dependabot（NuGet/GitHub Actions）を追加。
- テスト: FsCheck + xUnit v3 によるプロパティベーステストを導入（今後も不変条件を拡充）。

## [0.2.0] - 2025-09-07
### 追加（Added）
- Namespace: `buildDocumentWithNamespaces` を追加。外部の `PrefixMap` を用いて、root の xmlns と `explicitMember` の QName を指定のプレフィックスで生成。
- Validation: `Validation.validateDocument` による軽量検査を追加。
  - `MissingSchemaRef`, `MissingNamespacePrefix`, `InvalidExplicitMember` を検出。
- Builder API: `Builder.init/addContext/addUnit/addFact/addFactWithRefs/build`。Context/Unit の重複排除と ID 自動採番に対応。
- Config/Resolver: `Config.tryLoad` / `tryGetDefaultSchemaRefHref` / `schemaRefUrl`、および `Resolver` ヘルパーを追加。
- C# フレンドリー API: `CSharp` モジュールを追加（簡易コンストラクタと結果ラッパ）。
- 既定の schemaRef: 2020-11-01 の JPPFS エントリポイントを自動付与。EDINETホストの http 指定は https に昇格。

### 変更（Changed）
- 丸め: `roundWith RoundHalfUp` が負の `decimals` をサポートするよう修正（スケーリングで対応、`ArgumentOutOfRangeException` 回避）。

### テスト（Tests）
- FsUnit + xUnit v3 + FsCheck.Xunit.v3 のセットアップ。
- プロパティ:
  - Truncate は符号方向にオーバーシュートしない。
  - Truncate は RoundDown と RoundHalfUp の間に入る（符号に応じた境界条件）。
  - RoundHalfUp の誤差上限が理論範囲に収まる。
  - Builder: コンテキスト/ユニットの重複排除と追加順序の不変。
  - Namespaces: root の xmlns 完全性、`buildDocumentWithNamespaces` は指定プレフィックスのみ宣言、`qnameOfWith` のフォールバック。

[Unreleased]: https://github.com/divergen371/XbrlInstanceBuilder/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/divergen371/XbrlInstanceBuilder/releases/tag/v0.2.0
