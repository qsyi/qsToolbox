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
      { icon: 'github', link: 'https://github.com/qsyi/qsToolbox' },
      {
        icon: {
          svg: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"><path d="M6 8V6a6 6 0 0 1 12 0v2h2a1 1 0 0 1 1 1l-1.2 11a2 2 0 0 1-2 1.8H6.2a2 2 0 0 1-2-1.8L3 9a1 1 0 0 1 1-1h2Zm2 0h8V6a4 4 0 0 0-8 0v2Z"/></svg>'
        },
        link: 'https://qsyi.booth.pm/',
        ariaLabel: 'BOOTH'
      }
    ]
  }
})
