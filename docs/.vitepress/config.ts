import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'qsToolBox',
  description: 'アバター改変効率化ツール',
  lang: 'ja',
  base: '/qsToolbox/',
  themeConfig: {
    nav: [
      { text: 'ホーム', link: '/' },
    ],
    sidebar: [
      { text: 'インストール', link: '/install' },
      {
        text: '機能',
        items: [
          { text: '右クリックメニュー', link: '/features/contextmenu' },
          { text: 'ブックマーク', link: '/features/bookmark' },
          { text: 'ワンボタンでEditorOnly', link: '/features/editoronly' },
          {
            text: 'qsToolBox ウィンドウ',
            items: [
              { text: 'マテリアル', link: '/features/material' },
              { text: 'ブレンドシェイプ', link: '/features/blendshape' },
              { text: 'スケール', link: '/features/scale' },
              { text: 'メニュー生成', link: '/features/menu' },
            ]
          },
        ]
      }
    ],
    outline: false,
    socialLinks: [
      { icon: 'github', link: 'https://github.com/qsyi/qsToolbox' }
    ]
  }
})
