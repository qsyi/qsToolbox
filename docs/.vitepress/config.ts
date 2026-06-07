import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'qsToolBox',
  description: 'アバター改変効率化ツール',
  lang: 'ja',
  base: '/qsToolbox/',
  themeConfig: {
    nav: [
      { text: 'ホーム', link: '/' },
      { text: '使い方', link: '/install' },
    ],
    sidebar: [
      { text: 'インストール', link: '/install' },
      {
        text: '機能',
        items: [
          { text: 'マテリアル', link: '/features/material' },
          { text: 'シェイプキー', link: '/features/blendshape' },
          { text: 'スケール', link: '/features/scale' },
          { text: 'メニュー生成', link: '/features/menu' },
        ]
      }
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/qsyi/qsToolbox' }
    ]
  }
})
