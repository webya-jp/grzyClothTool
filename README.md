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
