import { defineConfig } from 'vitepress'

export default defineConfig({
  appearance: 'force-light',
  title: 'qsToolBox',
  head: [
    ['meta', { property: 'og:title', content: 'qsToolBox' }],
    ['meta', { property: 'og:description', content: 'アバター改変効率化ツール' }],
    ['meta', { property: 'og:image', content: 'https://qsyi.github.io/qsToolbox/og-image.png' }],
    ['meta', { property: 'og:url', content: 'https://qsyi.github.io/qsToolbox/' }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:image', content: 'https://qsyi.github.io/qsToolbox/og-image.png' }],
  ],
  description: 'アバター改変効率化ツール',
  lang: 'ja',
  base: '/qsToolbox/',
  themeConfig: {
    nav: [
      { text: 'ホーム', link: '/' },
      { text: 'パッチノート', link: '/changelog' },
    ],
    sidebar: [
      { text: 'インストール', link: '/install' },
      { text: 'パッチノート', link: '/changelog' },
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
              { text: '影同期', link: '/features/shadowsync' },
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
