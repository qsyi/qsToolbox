# インストール方法

::: tip このツールについて
qsToolBox は「よく使う操作のクリック数を減らしたい」という動機で作り始めた、元は個人用のツールです。

改変を始めたばかりの方よりも、基本的な流れに慣れてきた方に使いやすい設計になっています。
:::

## 推奨: VCCまたはALCOM

1. [Add to VCC](vcc://vpm/addRepo?url=https%3A%2F%2Fqsyi.github.io%2Fvpm-repos%2Findex.json) をクリック
2. リポジトリが追加されたら、プロジェクトに `qsToolBox` を追加

### Add to VCC がうまく動かない場合

以下のURLを手動でリポジトリに追加してください。

::: tip コピーするURL
```
https://qsyi.github.io/vpm-repos/index.json
```
:::

**VCC の場合**
1. `Settings` タブを開く
2. `Packages` の **Add Repository** をクリック
3. 上記URLを貼り付けて **Add**

**ALCOM の場合**
1. 左側メニューの `パッケージ&テンプレート` を開く
2. `VPMリポジトリ` タブを選択
3. 右上の **VPMリポジトリを追加** をクリックし、上記URLを貼り付けて追加

![ALCOMのVPMリポジトリ追加画面](/alcom-add-repo.png)

追加後は通常通り、プロジェクトに `qsToolBox` を追加できます。

## 代替: unitypackage

1. [最新リリース](https://github.com/qsyi/qsToolbox/releases/latest) から `.unitypackage` をダウンロード
2. Unity プロジェクトにインポート

## 動作環境

| 項目 | バージョン |
|---|---|
| Unity | 2022.3.22f1 以降 |
| [Modular Avatar](https://modular-avatar.nadena.dev/ja) | v1.10.0 以降 |
| [lilycalInventory](https://github.com/lilxyzw/lilycalInventory) | v1.5.2 以降 |
| [Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/ja/) | v1.9.0 以降 |
