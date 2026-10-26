# AGENTS.md

このファイルは、このリポジトリ全体で作業するエージェント向けの指針です。

## 作業の進め方

- 変更前に関連する実装・テスト・ドキュメントと `git status` を確認する。既存のユーザー変更を上書きせず、コミットには今回の変更だけを含める。
- 一つの論理的な修正・機能追加ごとに、実装・関連テスト・ドキュメント更新・検証を完了し、ローカルコミットしてから無関係な作業へ進む。複数の変更を最後にまとめてコミットしない。
- ユーザー向けの仕様・操作を変更する場合は、日本語の `README.md` と英語の `README.en.md` を同時に更新する。
- 実装・ビルド・テストの完了やローカルコミットは、push・バージョン確定・リリース作成の許可を意味しない。それぞれユーザーの指示に従う。

## 構成と責務

- `HeartRateMonitor/`: C# / WPF アプリケーション。
  - `BluetoothMonitor`、`GattAccess`、`AutoConnector`: BLE 通信・検出・再接続。
  - `RecordingEngine`: 記録処理の調整。
  - `Store` とその分割ファイル: Windows の `winsqlite3.dll` を使った永続化。
  - `OscSender`: VRChat 向け OSC パラメーターの送信。
  - `MainWindow.*` とチャートコントロール: UI。通信・記録・永続化の責務を持ち込まない。
  - `Theme.xaml`: 共通の色・テーマリソース。
  - `AppIcon.xaml`: アプリアイコンの定義。
- `tests/`: 依存パッケージ不要のテスト、UI 自動化、Bluetooth 診断。
- `tools/`: アイコン生成などの開発用スクリプト。

## 実装と検証

### 開発環境・コード規約

- 対象環境は x64 Windows 10/11、.NET Framework 4.8。ビルドには .NET SDK の Roslyn と Windows SDK メタデータが必要。NuGet 依存はない。
- 既存の C# スタイルに合わせる。インデントは半角スペース 4 個、型・メソッド・プロパティは PascalCase、ローカル変数・private フィールドは camelCase。ファイル名は対応する型・責務に合わせる。
- フォーマッター・リンターは設定されていない。不要な整形変更を混ぜない。
- テストは `tests/Tests.cs` と `tests/Tests*.cs` の既存ランナーを拡張し、`Test...` メソッドと、意図が分かる `Check` / `Throws` アサーションを使う。
- 変更に応じて、解析・変換・永続化・再接続・保存期間などの振る舞いを検証する。カバレッジ率の指定はない。

### 検証コマンド

リポジトリルートから実行する。ビルド時は必ず `-Test` を付ける。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

UI を変更した場合は、対話可能なデスクトップで UI 自動化も実行する。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
```

- UI の検証コードは `HeartRateMonitor/MainWindow.SmokeTest.cs` などの `*SmokeTest.cs` と `tests/ui-smoke.ps1` を参照する。
- 手動起動は `.\artifacts\HeartRateMonitor.exe`。隔離した描画確認には `--data-dir <isolated-directory> --smoke-test` を指定する。
- テストには一時ディレクトリまたは隔離したデータを使い、通常利用のデータベースを使わない。
- ドキュメントのみの変更ではビルド・UI 自動化は不要。リンク先・コマンド・記載内容と実装の整合性を確認する。
- コミット前に `git diff --check` と差分を確認する。実行した検証と、実機センサー・VRChat などで未検証の範囲を正確に報告する。

## ビルド成果物の配置

- 現在の開発ビルドは必ず `artifacts/HeartRateMonitor.exe` に出力し、前回の開発ビルドを上書きする。
- 開発成果物は `artifacts/` 直下に置く。`artifacts/dev-*` などのビルド別ディレクトリや、別途保存する中間ビルドを作らない。
- ログ・検証ファイルは `artifacts/logs/`、リポジトリ保守用バックアップは `artifacts/backups/` に置く。
- 確定版スナップショットを `artifacts/versions/version-X.Y.Z/` に保存するのは、ユーザーが明示的に指示した場合だけ。実装・ビルド・テスト後に自動作成しない。
- 既存の `artifacts/versions/version-0.0.0/` は、バージョン未設定の旧成果物専用とする。
- 生成物はソースではない。生成物の整理を理由に製品バージョンを変更しない。

## 維持すべき製品仕様

### センサー・記録・OSC

- HW706 を含む標準 BLE Heart Rate Service 対応センサーを、メーカー優先なしでサポートする。
- 起動時は、未検出でも前回接続確認済みのセンサーを先に試し、その後に Heart Rate Service を確認できた候補へ接続する。名前だけで接続対象と判定しない。
- Stop 中は検出・再接続を停止し、再開操作まで再開しない。
- 心拍値をタイムスタンプ付きで永続化する。表示・出力は Live / History チャートと CSV とし、受信値の一覧テーブルは作らない。
- 通常の測定値を捏造しない。デモ・テストデータは明示して通常データから隔離する。単発の送信テストで測定値を作らない。
- OSC の既定値は Int `VRCOSC/Heartrate/Value`、Float `HRPercent`。送信先・パラメーター名・変換設定は変更可能に保つ。
- 接続・保存・OSC のエラーを正確に表示する。UDP 送信成功を受信側への到達確認として扱わない。
- `Beko.BluetoothHeartRateOSC` は振る舞いの参考に限り、ソースをコピーしない。

### UI・設定

- 日本語 / 英語、Light / Dark / System テーマ、共通テーマリソース、設定の保存を維持する。
- 永続的な設定は Settings に集約し、ツールバーのテーマ切替ショートカットは残す。
- ピル型バッジや引き伸ばしたステータスコンテナーを避け、接続ボタンは共通のグリッド列で揃える。
- トレイ常駐と明示的な Exit を維持する。
- History の初期表示範囲は 10 分、スライダーの最小範囲は 1 分を維持する。

### データ保存期間

- 既定は無期限保存とし、既存設定の移行時も維持する。
- 保存期間の選択肢は Settings のみに置き、無期限 / 1 / 7 / 30 / 90 / 365 日とする。
- 有限の保存期間が設定されている場合は、起動時・設定変更時・少なくとも毎分、期限切れデータを削除する。
- 削除が取り消せないことを、日本語・英語の UI と README に明記する。
- Live / History に現在の保存期間を表示する。

## バージョン・コミット・公開

### バージョン変更

- 現在の製品バージョンは `HeartRateMonitor/AppVersion.cs` を参照する。ユーザーの明示的な指示なしに確定・更新しない。
- バージョン変更を指示された場合は SemVer に従う。1.0 未満は互換機能追加を MINOR、修正を PATCH とし、1.0 以降は破壊的変更を MAJOR、互換機能追加を MINOR、修正を PATCH とする。
- `-beta.N` / `-rc.N` のプレリリースを使用できる。
- `AppVersion.cs`、`HeartRateMonitor/app.manifest` の identity、表示タイトル、両 README のバージョン表記を同時に整合させる。
- 製品・informational バージョンはプレリリース接尾辞を含めた `X.Y.Z`、assembly / file / manifest バージョンは数値のみの `X.Y.Z.0` とする。

### コミット・PR

- Conventional Commits の `<type>[optional scope][!]: <description>` に従い、簡潔な命令形の件名にする。例: `fix: handle empty input`、`feat(osc): add destination setting`。
- 破壊的変更には type / scope の後の `!` または `BREAKING CHANGE:` フッターを付ける。
- PR には変更後の振る舞い、関連 issue、検証結果、実機検証の制約を記載する。見た目の変更にはスクリーンショットを添える。

### GitHub リリースノート

- 該当するトップレベル見出し `Added`、`Changed`、`Fixed`、`Removed`（または `Deleted`）と、変更説明の箇条書きだけで構成する。空の分類は省略する。
- 各項目は対応する動詞で始める。例: `- Added ...`。
- テスト結果・件数、検証メモ、SHA / チェックサム、インストール手順、その他の補足セクション・文章は含めない。
