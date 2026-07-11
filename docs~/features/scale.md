# スケール調整

スケールを変更した素体に衣装を着せたとき、サイズが合わずにはみ出たり縮んだりすることがあります。  
この機能では、衣装のスケールを素体のスケールに一発で合わせられます。複数の衣装を同時に選んでまとめて同期することもできます。

[Modular Avatar](https://modular-avatar.nadena.dev/ja) の [**Scale Adjuster**](https://modular-avatar.nadena.dev/ja/docs/reference/scale-adjuster) にも対応しています。

::: tip
[Merge Armature](https://modular-avatar.nadena.dev/ja/docs/reference/merge-armature) と [Copy Scale Adjuster](https://github.com/Rerigferl/modular-avatar-copy-scale-adjuster) を組み合わせたやり方に慣れている方であれば、無理にこちらへ乗り換える必要はありません。
:::

---

## こんな時に

- 素体のスケールを変えたら、衣装だけサイズが合わなくなった
- 複数の衣装をまとめて素体に合わせたい
- どのボーンでスケールがズレているのか、ピンポイントで確認したい

---

## 使い方

1. **Setup Outfit** 済みの衣装を選択した状態で `Ctrl + Q` を押します
2. **スケール** タブを選択します
3. 左の一覧に、素体・衣装それぞれで見つかったボーンが表示されます。オレンジの点が付いているボーンはスケールに差分があります
4. **同期** ボタンを押すと、素体のスケールに合わせて一括で調整されます

::: tip
衣装が Modular Avatar の **Setup Outfit**（Merge Armature）を適用済みでないと、ボーンが認識されず一覧に何も出ません。
:::

## 画面の見方

- **素体 Armature** — 同期元になる素体のボーンを指定します。通常はアバター選択時に自動で入ります
- **ボーン一覧** — 緑の点は素体側でそのボーンが見つかったこと、オレンジの点は衣装との間にスケール差分があることを表します。差分のあるボーンにマウスを乗せると、どのくらいズレているかがツールチップで見られます
- **スケール編集** — 一覧でボーンを選ぶと、右側にそのボーンの Scale（および Scale Adjuster があればその値）が表示され、直接数値を編集できます
- **Position / Rotation も同期する（実験的）** — オンにすると、同期時にスケールだけでなく位置・回転もまとめて合わせます
