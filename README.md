# grzyClothTool (WIP)

> **This is a fork.** It adds a fully localized Japanese UI on top of the original tool.
> See [日本語 UI について](#日本語-ui-について) below. Upstream: [grzybeek/grzyClothTool](https://github.com/grzybeek/grzyClothTool).

**_Please be aware that this tool is still in a "WORK IN PROGRESS" state. It is likely that you will encounter bugs, missing features or functionality issues._**

##

# 日本語 UI について

このリポジトリは [grzybeek/grzyClothTool](https://github.com/grzybeek/grzyClothTool) のフォークで、
**UI の日本語化**を追加したものです。本家の機能はそのまま利用できます。

## 言語の切り替え方

1. アプリを起動します。
2. 右上の歯車アイコンから **設定** 画面を開きます。
3. **言語 / Language** の項目で、次のいずれかを選びます。
   - **Auto / 自動 (OS)** — Windows の表示言語に従います (既定)。日本語版 Windows なら日本語になります。
   - **English**
   - **日本語**
4. 選んだ内容はすぐに画面へ反映され、次回以降も保持されます。
   一部のテキストは再起動後に反映されます。

設定は `%LocalAppData%\grzyClothTool\settings.json` に保存されます。

起動時に言語を強制したい場合は、コマンドライン引数 `--lang en` / `--lang ja`、または環境変数
`GRZYCLOTHTOOL_LANG=en|ja` を使えます (設定より優先され、設定ファイルは書き換えません)。
自動 UI テストのように表示言語を固定したいときに使います。

## テクスチャ一括最適化

FiveM のサーバーログに `Asset xxx.ytd uses 64.0 MiB of physical memory. Oversized assets can and WILL
lead to streaming issues` が大量に出る原因は、たいてい **無圧縮のディフューズテクスチャ**と
**ミップマップ無し**のテクスチャです。このフォークでは、プロジェクト全体のテクスチャを
まとめて条件で処理する **テクスチャ一括最適化** を追加しています。

プロジェクト画面の右下、「3D プレビュー」の左にある **テクスチャ一括最適化** ボタンから開きます。

対象範囲は **プロジェクト全体 / 選択中のアドオン / 選択中のドロウアブル** から選べます。
適用できる条件は次のとおりです。

| 条件 | 既定 | 内容 |
| --- | --- | --- |
| ミップマップを自動生成する | ON | ミップが足りないテクスチャに正しい段数を設定します。 |
| 無圧縮テクスチャを圧縮する (DXT5) | ON | A8R8G8B8 などの無圧縮形式を DXT5 にします。メモリが約 1/4 になり、アルファも失われません。 |
| 2 の冪に揃える | ON | 2 の冪でない解像度を直近の 2 の冪に揃えます (既定は切り下げ、切り上げも選べます)。 |
| 解像度の上限を適用する | ON | diffuse 2048 / normal 512 / specular 256 が既定です。ダイアログ内で変更できます。 |
| 1 テクスチャあたりの上限 | ON (16 MB) | ミップ込みの推定メモリが上限を超える間、幅と高さを半分にします。 |
| 圧縮形式を最適化する | OFF | 実画像のアルファを調べて DXT1 / DXT5 を選び分けます。全画像を読むので時間がかかります。 |

適用順序は **圧縮 → 2 の冪 → 解像度上限 → メモリ上限 → ミップ段数の再計算** です。
先に圧縮するため、たとえば 4096x2048 の無圧縮 (42.7 MB) は DXT5 にするだけで 10.7 MB になり、
解像度を落とさずに済みます。

「解析」を押すとプランが一覧表示されます (変更が不要なテクスチャは出ません)。
16 MB を超える行は強調表示され、「上限超過のみ選択」で絞り込めます。
「適用」を押しても **その時点ではファイルを書き換えません**。元ファイルはそのままで、
最適化されたテクスチャは **次回のリソースビルド時**に生成されます。「元に戻す」で解除できます。

実測 (女性用アドオン、ydd 128 件 / ytd 421 件):
合計 2052 MB → 853 MB (-58%)、16 MB 超が 18 件 → 0 件、最大テクスチャ 42.7 MB → 5.3 MB。

### 今すぐ書き出す (ビルド前にミップマップと圧縮を適用する)

「適用」がビルド時まで待つのに対し、**今すぐ書き出す** はその場でエンコードします。
対象のテクスチャを実際に .ytd に変換してプロジェクトの assets に書き出し、
`GTexture` の参照をそれに差し替えたうえで、テクスチャ情報を再読込します。
`ビルド時に最適化` フラグは外れるので、ビルドではその .ytd がそのままコピーされます。

これにより **3D プレビューとテクスチャプレビューで、ビルド前に実際の画質を確認できます**。

- 既定では **元ファイルを残す** が ON です。直後なら **直前の書き出しを元に戻す** で戻せます。
  OFF にすると、どこからも参照されなくなった古い素材を削除します (元に戻せません)。
- **外部プロジェクトではあなたの元ファイルを絶対に上書きしません**。
  最適化結果はプロジェクト管理下に書き出し、参照だけを差し替えます。
- 進捗表示と中止に対応しています。完了後に実ファイルサイズと推定メモリの削減量を表示します。
- 元が .ytd の場合、**内部テクスチャ名は変更されません** (ドロウアブルのシェーダーが
  その名前で参照しているため)。

実測 (ytd 2050 件のパックの一部、2048px の diffuse 18 件): 
実ファイル 3.2 MB 削減、推定メモリ 189 MB 削減、ミップ 10 段生成、内部テクスチャ名 18/18 件維持。

## テクスチャの重複統合

同じ画像が何度も入っているプロジェクトは珍しくありません。プロジェクト画面右下の
**テクスチャの重複を探す** ボタンで、内容が同じテクスチャを検出できます。

検出はまずファイル内容の MD5 で行い (高速)、**画像の中身も比較する** を ON にすると
残りを実際にデコードして幅・高さ・ピクセルで比較します (別形式で保存された同じ画像も
見つかりますが時間がかかります)。**ドロウアブルをまたいだ重複も対象**で、非同期・進捗表示付きです。

グループごとに「同一テクスチャ n 件 / 使用しているドロウアブル / バリエーション /
1 件あたりのサイズ / まとめた場合の削減量」を表示します。操作は 2 つあります。

**ファイルを共有する** (既定・安全)
同一内容の複数のテクスチャが 1 つの素材ファイルを参照するようにし、余分なコピーを
プロジェクトの assets から削除します。**ビルド出力は一切変わりません** —
GTA はバリエーションごとに別名の .ytd を必要とするため、書き出されるファイル数は従来どおりです。
減るのはプロジェクトフォルダーの容量と、最適化にかかる手間 (同じ画像を 1 回処理すれば済む) です。
外部プロジェクトはユーザーのファイルを参照しているため、この操作は無効化されます。

**重複バリエーションを削除する** (任意・既定オフ・破壊的)
**同一ドロウアブル内**で内容が同じバリエーション (例 `diff_000_a` と `diff_000_b` が同一) を削除します。
ゲーム内のテクスチャ番号とショップメタのエントリ数が変わるため、実行前に消えるバリエーションを
一覧表示して確認を求めます。ドロウアブルをまたいだ削除は行いません。

実測 (ytd 2058 件 / 680 MB のパックを self-contained で取り込み):
重複 **151 グループ / 963 件**、共有でプロジェクトの assets が **679 MB → 522 MB (-157 MB、-23%)**、
ファイル数 2050 → 1238。同一ドロウアブル内の重複バリエーションは 3 件でした。

## 翻訳について

- ドロウアブル、テクスチャ、アドオン、プロップ、コンポーネントなど、GTA V 衣装 MOD で
  一般的に使われている用語に合わせています。
- コンポーネント/プロップのスロットは、コード名を残したまま日本語を併記します
  (例: `トップス (jbib)`、`下半身 (ズボン) (lowr)`)。スロットコード自体はファイル名や
  内部処理で使われるため変更していません。
- 開発者向けのログ (ログウィンドウに出力される内容) は英語のままです。

## 翻訳に問題を見つけたら

このフォークの [Issues](https://github.com/webya-jp/grzyClothTool/issues) へご報告ください。
本家の不具合・機能要望は [本家リポジトリ](https://github.com/grzybeek/grzyClothTool/issues) へお願いします。

## ライセンス

本家と同じく **GPLv3** です。詳細は [LICENSE](LICENSE) を参照してください。
このフォークの変更点も GPLv3 で配布されます。

<p align="center">
  <img src="https://github.com/grzybeek/grzyClothTool/assets/40837847/30c72912-8828-4fa8-a84f-6f27a1f8eb5f">
</p>

**grzyClothTool** is a free tool to easily create and manage your GTA5 addon clothing packs.
Now you can do _almost_ everything you could do before with _other available tools_, but now without spending any money!

##

# Bulk texture optimization (quick start)

This fork adds a **bulk texture optimizer** for the whole project, aimed at the FiveM warning
`Asset xxx.ytd uses 64.0 MiB of physical memory. Oversized assets can and WILL lead to streaming issues`.

1. Open a project and click **Bulk optimize textures** (bottom right, next to *Preview 3D*).
2. Pick the scope: whole project, selected addon, or selected drawable(s).
3. Rules, all applied in this order — compression, power of two, resolution limit, memory budget,
   mip count:
   - **Generate mip maps** (on) — fills in a missing mip chain.
   - **Compress uncompressed (DXT5)** (on) — re-encodes A8R8G8B8 and friends, which cuts their
     memory to about a quarter without losing the alpha channel.
   - **Snap to power of two** (on, rounds down by default).
   - **Apply resolution limit** (on) — diffuse 2048 / normal 512 / specular 256 by default,
     editable in the dialog.
   - **Memory limit per texture** (on, 16 MB) — halves the texture until the estimated memory,
     mip levels included, fits.
   - **Optimize compression format** (off) — reads every image to pick DXT1 or DXT5 by alpha usage.
4. Press **Analyze**, review the plan (textures that need no change are not listed), then **Apply**.
   Nothing is written at that point: the source files stay untouched and the optimized textures are
   generated during the next resource build. **Revert optimization** undoes it.

Measured on a real female addon (128 ydd / 421 ytd): 2052 MB -> 853 MB of texture memory (-58%),
18 textures above 16 MB -> 0, largest texture 42.7 MB -> 5.3 MB.

5. **Write now** encodes the selected textures immediately instead of waiting for the build: each
   result is written into the project assets, the texture is repointed at it and its details are
   reloaded, so the **3D preview and the texture preview show the real quality before any build**.
   *Keep the original files* is on by default, which makes **Undo the last write** possible.
   An external project's own files are never overwritten — the result is written into the project
   and only the reference changes. When the source is a .ytd its internal texture name is preserved,
   because the drawable's shader references the texture by that name.

Measured on 18 2048 px diffuse textures of a real pack: 3.2 MB smaller on disk, 189 MB less
estimated video memory, a 10 level mip chain generated, 18/18 internal texture names unchanged.

##

# Duplicate texture merge (quick start)

Packs often ship the same image many times over. Click **Find duplicate textures** (bottom right,
next to *Bulk optimize textures*) to group textures that hold the same image, across drawables.

Detection hashes the file contents with MD5, which is fast. **Also compare decoded images** decodes
whatever is left and compares it by size and pixels, so the same image stored in two different
formats is found too — at the cost of a much longer scan. The scan is asynchronous and reports
progress, so a few thousand textures do not freeze the UI.

Each group shows how many copies exist, which drawables and variations use them, the size of one
copy and how much merging would free. Two actions are offered:

- **Share one file** (default, safe) — every duplicate of a self-contained project points at a
  single asset file and the extra copies are deleted from the project assets. **The build output
  does not change**: GTA needs one .ytd per variation, so the same files are written as before.
  What it saves is project folder space and optimization work. Disabled for external projects,
  whose files belong to you.
- **Delete duplicate variations** (optional, off by default, destructive) — removes variations of
  **the same drawable** that hold the same image (e.g. `diff_000_a` and `diff_000_b`). This changes
  the in-game texture numbers and the number of shop meta entries, so the exact list of variations
  that would go away is shown for confirmation first. Duplicates across drawables are never deleted.

Measured on a real pack imported as a self-contained project (2058 ytd, 680 MB): **151 duplicate
groups covering 963 textures**, sharing shrank the project assets from **679 MB to 522 MB (-157 MB,
-23%)** and 2050 files down to 1238.

##

# Why choose grzyClothTool?

- Exclusive features
  - Easily preview your textures
  - Easily preview ALL ped props
  - You can preview multiple drawables at once
  - See how much your hair will shrink under hair in 3D Previewer
  - Check how much your heels require height in 3D Previewer
  - You don't have to worry about 128 items limit in one addon, it automatically splits to multiple addons for you!
- Open Source
  - You don't need to be scared about running some weird obfuscated .exe files on your computer, that no one knows what they are doing in the background 😆
  - It is free to use and will always be

# Mentions

- **[dexyfex](https://github.com/dexyfex/CodeWalker)** - 3D Previewer wouldn't be possible without him and CodeWalker! [Support dexyfex on patreon](https://www.patreon.com/dexyfex)
- [JagodaMods](https://discord.gg/jagoda) - A lot of motivation and ideas 💖
- [ook](https://github.com/ook3d) - Fixes and contribution

# Want to say thanks?

- Click the **⭐ Star ⭐** button
- Spread information about this tool everywhere!

# Donate

- If you find this tool useful in your daily modding, please consider donating to support the development through [Sponsor](https://github.com/grzybeek/grzyClothTool?sponsor) button at the top of this page, so that I can continue to keep working on it.

# Need support?

- Join [Discord](https://discord.gg/HCQutNhxWt), but because tool is still WIP, currently support is only for people that are supporting me on [ko-fi](https://ko-fi.com/grzybeek)

# Screenshots

![image](https://github.com/grzybeek/grzyClothTool/assets/40837847/5f568406-84cf-4050-aa4f-9dbd94066fca)
![image](https://github.com/grzybeek/grzyClothTool/assets/40837847/773b83ae-3609-4dbb-8cd2-c82ff67a00eb)
![image](https://github.com/grzybeek/grzyClothTool/assets/40837847/77bb0ff0-bd55-4895-846b-216cc9ce2349)
![image](https://github.com/grzybeek/grzyClothTool/assets/40837847/48ca84a6-1767-4a1c-b0a7-da8b51f8d2cd)
