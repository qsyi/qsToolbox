# qsToolbox
アバター改変作業を効率化するUnityエディタ拡張ツール集です。

<div id="top"></div>

## 目次
1. [概要](#概要)  
2. [インストール方法](#インストール方法)  
3. [機能一覧](#機能一覧)  
4. [動作環境](#動作環境)  

## 概要 <a name="概要"></a>
- **作業効率を向上**
  - `Ctrl + E` で選択オブジェクトを `EditorOnly`+非表示 に切り替え
  - `Ctrl + Q` で各機能をまとめたツールウィンドウを呼び出し可能
  - ホームボタンで Assets 直下にジャンプ、ブックマークでよく使うフォルダへ即移動
  - オブジェクトを右クリックしてアバタールート用コンポーネントの一括追加や BoneProxy の設定が可能

- **マテリアル操作**
  - ドラッグ＆ドロップでマテリアルへの差し替え

- **シェイプキー調整**
  - 限界突破や反転に対応

- **スケール調整**
  - 衣装のスケールを素体に合わせて調整
  - MA Scale Adjuster に対応

- **メニュー生成**
  - `lilycalInventory` 用のメニューをまとめて簡易的に生成

## インストール方法 <a name="インストール方法"></a>
- 推奨: VPMリポジトリの [**Add to VCC**](https://qsyi.github.io/vpm-repos/) を利用  
- 代替: [最新リリース](https://github.com/qsyi/qsToolbox/releases/latest) の `.unitypackage` をインポート  

## 機能一覧 <a name="機能一覧"></a>

- **ToggleEditorOnly**  
  - `Ctrl + E` で選択オブジェクトを `EditorOnly` 化し非アクティブに  

- **BookmarkOverlay**
  - ホーム: Assets直下に戻る  
  - ブックマーク: 登録済みフォルダへ移動  
  - ブックマーク管理: ブックマークの並べ替え・削除が可能
-
  - アセットフォルダをブックマークボタンへドラッグ＆ドロップで登録可能
  ![BookmarkOverlay](https://github.com/user-attachments/assets/60e88bac-3241-4d64-a553-844115eca533)  

- **オブジェクトの右クリック**
  - **アバタールート用コンポーネント追加**: オブジェクトに `MA Mesh Settings`・`MA Convert Constraints`・`AAO Trace and Optimize` を一括追加  
  - **頭に追従**: 選択オブジェクトに `MA Bone Proxy`（Head）を追加  
  - **腰に追従**: 選択オブジェクトに `MA Bone Proxy`（Hips）を追加  

- **qsToolBoxウィンドウ**  
  - `Ctrl + Q` で呼び出し  
  - 配下のマテリアルやシェイプキーを自動探索  

  - **マテリアルモード**  
    - ドラッグ＆ドロップでマテリアル差し替え  

  - **ブレンドシェイプモード**  
    - シェイプキー合成
    - シェイプキーを上書きするか選択可能
    - ベースにするシェイプキーを選択し、それに追加するシェイプキーとその数値(-100～100)を選択する

  - **スケールモード**  
    - アバターと衣装のスケールを同期
    - 衣装を選択して`Ctrl + Q`するとその衣装を検出してスケール同期

  - **メニュー生成モード**
    - 探索対象配下のメッシュを一覧表示
    - チェックしたメッシュだけメニュー化

## 動作環境 <a name="動作環境"></a>
- Unity 2022.3.22f1 以降推奨  
- [Modular Avatar](https://modular-avatar.nadena.dev/ja) v1.10.0以降が必要  
- [lilycalInventory](https://github.com/lilxyzw/lilycalInventory) v1.5.2以降が必要  
- [Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/) v1.9.0以降が必要  
