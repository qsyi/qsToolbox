# qsToolbox
アバター改変作業を効率化するUnityエディタ拡張ツール集です。

<div id="top"></div>

## 目次
1. [概要](#概要)  
2. [インストール方法](#インストール方法)  
3. [主な機能一覧](#主な機能一覧)  
4. [動作環境](#動作環境)  

## 概要 <a name="概要"></a>
- **ワンクリックで作業効率を向上**
  - `Ctrl + E`で選択オブジェクトを`EditorOnly`に切り替え
  - ホームボタンでAssets直下にジャンプ、ブックマークでよく使うフォルダへ即移動
  - `Ctrl + Q`で各機能をまとめたツールウィンドウを呼び出し可能  

- **マテリアル操作**
  - 色違いのマテリアルへの差し替えを簡単に

- **シェイプキー管理**
  - まとめて変更や合成、まばたき修正、シェイプキーの限界突破にも対応

- **スケール調整**
  - 衣装のスケールを素体に合わせて自動調整
  - MA Scale Adjusterにも対応

- **メニュー生成**
  - `lilycalInventory` 用のメニューを探索対象から生成
  - 個別生成・まとめて生成の両方に対応


## インストール方法 <a name="インストール方法"></a>
- 推奨: VPMリポジトリの [**Add to VCC**](https://qsyi.github.io/vpm-repos/) を利用  
- 代替: [最新リリース](https://github.com/qsyi/qsToolbox/releases/latest) の`.unitypackage`をインポート  

## 主な機能一覧 <a name="主な機能一覧"></a>

- **ToggleEditorOnly**  
  - `Ctrl + E`で選択オブジェクトを`EditorOnly`化し非アクティブに  

- **BookmarkOverlay**
  - ホーム: Assets直下に戻る  
  - ブックマーク: 登録済みフォルダへ移動  
  - 管理ウィンドウ: ブックマークの並べ替え・削除が可能  
  ![BookmarkOverlay](https://github.com/user-attachments/assets/60e88bac-3241-4d64-a553-844115eca533)  

- **qsToolBoxウィンドウ**  
  - `Ctrl + Q`で呼び出し  
  - 配下のマテリアルやシェイプキーを自動探索  

  - **マテリアルモード**  
    - マテリアル差し替え
    ![MaterialChange](https://github.com/user-attachments/assets/c1cd173a-fc52-43b8-82e7-0cc0ae01e1ae)

  - **ブレンドシェイプモード**  
    - シェイプキー合成

  - **スケールモード**  
    - アバターと衣装のスケールを同期
    - `Position/Rotation` 同期にも対応（実験的機能）
    ![Scale](https://github.com/user-attachments/assets/fe991452-fe7d-460e-b191-7af80578e79a) 

  - **メニュー生成モード**
    - `lilycalInventory` 用メニューを生成
    - フォルダ名を指定して `Menu_` 接頭辞付きの親メニューを新規作成
    - 選択レンダラーを個別メニュー、またはまとめメニューとして生成

## 動作環境 <a name="動作環境"></a>
- Unity 2022.3.22f1 以降推奨  
- [Modular Avatar](https://modular-avatar.nadena.dev/ja) v1.10.0以降が必要  
- [lilycalInventory](https://github.com/lilxyzw/lilycalInventory) v1.5.2以降が必要  
