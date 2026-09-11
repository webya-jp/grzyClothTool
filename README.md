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
