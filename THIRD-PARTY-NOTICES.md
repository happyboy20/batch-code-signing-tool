# 第三者ソフトウェアについて

## AzureSignTool

署名処理には [AzureSignTool](https://github.com/vcsjones/AzureSignTool) を使用します。本体EXEやこのリポジトリにはエンジンを同梱せず、設定画面から公式の配布物を別途ダウンロードします。

- 導入対象：v7.0.1 のWindows x64向け単体EXE
- 著作権者：Kevin Jones および Oren Novotny（2017年）
- ライセンス：MITライセンス
- [公式ライセンス本文](https://github.com/vcsjones/AzureSignTool/blob/v7.0.1/LICENSE)
- [公式配布ページ](https://github.com/vcsjones/AzureSignTool/releases/tag/v7.0.1)

本ツールの利用規約は、AzureSignToolやその依存ソフトウェアに対する権利を制限しません。それぞれに適用されるライセンスに従ってください。

## .NET・Windows

.NET 10 Desktop Runtimeは本体に同梱しません。Microsoftの提供元から別途導入し、その利用条件に従ってください。Windows標準の署名検査APIを使用します。

## Microsoft SignTool・USBドライバー

USB方式では、[Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) に含まれるMicrosoft SignToolを外部プロセスとして使用します。SignToolやSDKのファイルは同梱・再配布しません。Microsoft公式から導入し、Microsoftの利用条件に従ってください。

トークンのドライバー・管理ソフトも同梱しません。各メーカーまたは証明書販売元の公式案内に従って導入してください。本ツールは秘密鍵をエクスポートせず、PINを保存しません。
