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
