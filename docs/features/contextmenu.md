# 右クリックメニュー（QsContextMenu）

Hierarchy（シーン上のオブジェクト一覧）でオブジェクトを右クリックすると、`qs_toolbox` というメニューが表示されます。

---

## アバタールート用コンポーネント追加

アバターのルート（一番上のオブジェクト）に、改変に役立つコンポーネントを一度にまとめて追加できます。

**何が追加されるの？**

| コンポーネント | 何をしてくれるか |
|---|---|
| [MA Mesh Settings](https://modular-avatar.nadena.dev/ja/docs/reference/mesh-settings) | 影やライティングのバグ、メッシュが消える問題を改善します |
| [MA Convert Constraints](https://modular-avatar.nadena.dev/ja/docs/reference/convert-constraints) | Unity の Constraint を VRChat に対応した形式に変換します |
| [AAO Trace and Optimize](https://vpm.anatawa12.com/avatar-optimizer/ja/docs/reference/trace-and-optimize/) | 使っていないメッシュなどを自動で削除してアバターを軽くします |

**使い方**
1. Hierarchy でアバターのルートオブジェクトを右クリック
2. `qs_toolbox` → `アバタールート用コンポーネント追加` をクリック

::: tip
アバターのルートとは、Hierarchy の一番上にある VRC Avatar Descriptor がついているオブジェクトのことです。
:::

---

## 頭に追従 / 腰に追従

アクセサリーや髪型などのオブジェクトに [`MA Bone Proxy`](https://modular-avatar.nadena.dev/ja/docs/reference/bone-proxy) を追加して、アバターの頭や腰ボーンに追従させます。  
追従させることで、アバターが動いたときに一緒に動くようになります。

**使い方**
1. Hierarchy で追従させたいオブジェクトを右クリック
2. `qs_toolbox` → `頭に追従` または `腰に追従` をクリック
