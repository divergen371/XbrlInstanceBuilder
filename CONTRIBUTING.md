# Contributing

ありがとうございます！このリポジトリでは以下の方針で開発を行っています。

## 前提
- .NET 9 / F# を使用
- OS は Linux / Windows / macOS を想定（CI は 3OS マトリクス）

## セットアップ
```bash
# 依存復元
dotnet restore

# ビルド
dotnet build -c Debug

# テスト（カバレッジ付き）
dotnet test \
  --logger trx \
  --results-directory TestResults \
  --collect:"XPlat Code Coverage"
```

CI は `/.github/workflows/dotnet.yml` を参照ください。テスト結果（.trx）と Cobertura 形式のカバレッジがアーティファクトとして保存され、ジョブサマリに概要が表示されます。

## コーディングスタイル
- `.editorconfig` に従います
  - 4 スペースインデント、LF、UTF-8
- F# の可読性を重視し、パイプ・関数合成を適切に使用します

## テスト
- xUnit v3 + FsUnit + FsCheck を使用
- 不変条件はなるべくプロパティベースで検証
- 新規 API を追加した場合、サンプルとテストをセットで追加してください

## PR
- テンプレート（`.github/pull_request_template.md`）に沿って記載
- `dotnet build` / `dotnet test` がローカルでも成功していること
- 影響範囲に応じて README / CHANGELOG の更新をお願いします

## リリース
- 本段階では NuGet への公開は行いません
- タグ `v*` の push で `dotnet pack` を実行し、パッケージをアーティファクトとして保存します（公開はしません）

## 質問・バグ報告・機能要望
- Issue テンプレート（バグ/機能要望）を用意しています

ご協力ありがとうございます！
