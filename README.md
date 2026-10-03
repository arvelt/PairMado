# PairMado

左右に画像を並べて比較する、Windows用の小さな画像ビューアです。
各ペインへ別フォルダの画像をドラッグ＆ドロップし、同じフォルダ内の画像を前後に切り替えられます。
インターネット接続は不要です。

## 起動

[リリースページ](https://github.com/arvelt/PairMado/releases/latest)から `PairMado-Windows.zip` をダウンロードし、すべて展開して `PairMado.exe` を起動してください。
ソースコードから作る場合は、下記のビルド手順を使用してください。
Windowsの.NET Framework 4.8以降が動作条件です。開発・動作検証はWindows 11の.NET Framework 4.8.1環境で行っています。

## 操作

- 左右の枠へ画像を1枚ずつドロップするか、各枠の「画像を選ぶ」で画像を開きます。
- 各枠の `←` / `→` で、表示中の画像と同じフォルダ内の前後の画像へ移動します。サブフォルダは含みません。
- 左右それぞれで名前、作成日時、更新日時の昇順・降順を選べます。
- 名前順は数字を考慮します（例：`image1` → `image2` → `image10`）。
- 並べ替えを変更しても、表示中の画像は保持します。先頭と末尾では対応する矢印が無効になります。
- 画像は縦横比を維持して枠内に最大表示され、ウィンドウのサイズ変更に追従します。
- 元画像を書き換えず、表示中もファイルをロックしません。

## 対応形式・制限

PNG、JPEG、BMP、GIF、TIFF、ICOに対応します。GIFとTIFFは最初のフレームを表示します。
WebP、AVIFなどは、Windowsに対応する画像コーデックがある場合のみ表示できます。
作成日時はWindowsのファイル作成日時です。画像内の撮影日時ではありません。
画像とフォルダ一覧の読み込みは同期処理なので、大きな画像や遅い保存先では一時的に画面の反応が止まる場合があります。
新しく追加されたファイルや日時の変更は、前後移動や並べ替え時に読み直します。

## ビルド

Windows PowerShellまたはPowerShellで、リポジトリのルートから実行します。

```powershell
powershell -NoProfile -File .\build.ps1
```

Windowsに含まれる.NET FrameworkのC#コンパイラーを利用します。
外部ライブラリ、NuGetパッケージ、インターネット接続は不要です。
実行結果は `dist\PairMado.exe` と `dist\PairMado-Windows.zip` に出力されます。

## 検証

```powershell
powershell -NoProfile -File .\build.ps1 -Verify
```

テスト画像を `test-output` 以下の専用フォルダに生成し、6種類の並べ替え、数値を含む名前の順序、同じ階層だけの列挙、前後移動、端での停止、左右の独立動作、エラー時の表示保持、一覧更新、ファイルロックの解放を確認します。
検証後はテスト機能を含まない配布用バイナリを再ビルドします。

## リリース作成

GitHubの Actions → Build and release → Run workflow で、新しいタグ（例：`v1.0.1`）を指定して実行します。
Windows上でビルド・検証後、指定タグのリリースを作成し、配布ZIPを添付します。既存のリリースを上書きしません。

## ファイル構成

- `src/PairMado.cs`：画面、画像表示、ファイル一覧と並べ替え
- `assets/image-compare.ico`：専用アイコン
- `tests/Verification.cs`：動作検証
- `build.ps1`：ビルド・検証・配布ZIP作成

## ライセンス

MIT License。ソースコードと専用アイコンに適用します。詳細は `LICENSE` を参照してください。
