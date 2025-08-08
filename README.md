# qsToolBox

UnityEditor拡張ツール
ショートカットを使用するため競合に注意してください

## インストール手順

1. [Add to VCC](https://qsyi.github.io/vpm-repos/) から追加

## 動作環境

- ModularAvatarが必要

## 使い方

- `Ctrl + E`  
  選択中のゲームオブジェクトをEditorOnlyかつ非アクティブにする

- `Ctrl + Q`  
  選択オブジェクトの配下を自動探索し、ブレンドシェイプ、マテリアル、スケールを操作可能  
  主に衣装用を想定

### ブレンドシェイプ

- 配下の全レンダラーのシェイプキーを検索・操作が可能
- ![BlendShape画像](https://github.com/user-attachments/assets/7a85d67f-b181-4f96-934f-ecd715bb4a08)

### マテリアル

- ドラッグ＆ドロップでまとめて入れ替え可能
- 衣装の色変更などに利用
- ![Material画像](https://github.com/user-attachments/assets/33bc0cdd-6ee9-433e-bcdf-6e8919b76a66)

### スケール

- 素体のMAScaleAdjusterに合わせて衣装のコンポーネントを追加し自動調整  
- 素体のスケールをまとめて変更可能  
- ![Scale画像](https://github.com/user-attachments/assets/70be103c-ebd8-4a8d-8ba1-8a3976291b23)
