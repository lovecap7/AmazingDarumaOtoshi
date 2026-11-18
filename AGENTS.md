# AGENTS.md — アメイジングだるま落とし

Antigravity（および AGENTS.md を読む AI）向けの入口。
**このリポジトリで作業する前に、次の2つを読むこと。**

1. `CLAUDE.md` … チーム共通のルール（最優先）
2. `Docs/Antigravity_Handoff.md` … プロジェクトの全体像・今の状態・触ってはいけない所

> 担当は**フォルダ名**で決まる。由留部（Yurube）の担当分は `AmaizingDarumaOtoshi/Assets/Yurube/`。
> 他の担当の作業では、`Docs/Antigravity_Handoff.md` の由留部向けの記述を自分のルールとして扱わない。

## 絶対に守ること

- **返答・報告は日本語。**
- **コミット・プッシュをしない。** ユーザーが GitHub Desktop で行う。ブランチも切らない（main のみ）。
- **作業前に `git status`、作業後に変更したファイル名を報告する。**
- 新規ファイルは自分の担当フォルダの中（由留部なら `Assets/Yurube/`）。
- 他の人のフォルダは **「ローカルの動きを変えない追加」だけ**。目印コメント（例 `// ストーリー用(Yurube)`）を付け、最後に報告する。
- `.cs`・`.prefab`・`.asset` を追加したら **`.meta` も必ずセット**（GUID 重複に注意）。
- Unity が勝手に変えるファイル（`ProjectSettings/*`、`Assets/Settings/*`、`DefaultNetworkPrefabs.asset`、
  TextMesh Pro のフォールバック、`PerformanceTestRun*.json`）は、意図した変更以外コミットしない。
- `Temp/`（リポジトリ直下）・過去のビルド成果物・`Assets/Nakagawa/Backup~/`・`Assets/_Recovery/` は触らない。
- **迷ったら実装せず質問する。** 仕様は「決めてから作る」方針。

## 環境

- Unity **6000.6.3f1** / URP / Input System / Netcode for GameObjects。asmdef は無く、すべて `Assembly-CSharp`。
- Unity プロジェクトは **`AmaizingDarumaOtoshi/`**（`Amazing` ではなく `Amaizing`。綴りミスではない）。
- **由留部の PC では `git` に PATH が通っていない。** GitHub Desktop 同梱の git を使う（読むだけ）:

```powershell
$git = Get-ChildItem "$env:LOCALAPPDATA\GitHubDesktop\app-*\resources\app\git\cmd\git.exe" |
       Select-Object -Last 1
& $git.FullName -C "C:\AmazingDarumaOtoshi" status --porcelain
& $git.FullName -C "C:\AmazingDarumaOtoshi" log --oneline -10
```

- PowerShell の既定は cp932。日本語のファイルは AI のファイル読み込み（UTF-8）で読む。
- Unity を開かずに構文チェックする方法は `Docs/Antigravity_Handoff.md` §10 と `Docs/DeepSeek_Handoff.md` §8。

## 最初にやること

変更はせず、次を確認して日本語で報告する。

1. `git status` と直近のコミット
2. 未コミットの内容
3. 次にやるとよさそうなこと（`Docs/Antigravity_Handoff.md` §12 を参考に）
