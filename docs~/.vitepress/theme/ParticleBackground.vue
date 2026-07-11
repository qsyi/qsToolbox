<template>
  <canvas ref="canvasRef" class="qs-particle-bg" aria-hidden="true"></canvas>
</template>

<script setup>
import { onMounted, onUnmounted, ref } from 'vue'

const canvasRef = ref(null)
const colors = ['#4a58c4', '#7b86e8', '#d4809a', '#c0607e']

let ctx = null
let particles = []
let rafId = 0
let dpr = 1
let lastSpawnAt = 0
let lastSpawnX = -9999
let lastSpawnY = -9999

function resize() {
  const canvas = canvasRef.value
  if (!canvas || !ctx) return
  dpr = Math.min(window.devicePixelRatio || 1, 2)
  canvas.width = window.innerWidth * dpr
  canvas.height = window.innerHeight * dpr
  canvas.style.width = window.innerWidth + 'px'
  canvas.style.height = window.innerHeight + 'px'
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
}

function onMouseMove(e) {
  // 動いた距離と経過時間で間引き、発生頻度を抑えてはかない印象にする
  const now = performance.now()
  const dist = Math.hypot(e.clientX - lastSpawnX, e.clientY - lastSpawnY)
  if (now - lastSpawnAt < 22 || dist < 7) return
  lastSpawnAt = now
  lastSpawnX = e.clientX
  lastSpawnY = e.clientY

  particles.push({
    x: e.clientX,
    y: e.clientY,
    vx: (Math.random() - 0.5) * 0.5,
    vy: (Math.random() - 0.5) * 0.5 - 0.15,
    r: Math.random() * 2.6 + 1.8,
    life: 1,
    decay: 0.014 + Math.random() * 0.012,
    color: colors[Math.floor(Math.random() * colors.length)]
  })
  // パーティクル数の上限（増え続けて重くならないように）
  if (particles.length > 60) particles.splice(0, particles.length - 60)
}

function tick() {
  if (ctx) {
    ctx.clearRect(0, 0, window.innerWidth, window.innerHeight)
    for (let i = particles.length - 1; i >= 0; i--) {
      const p = particles[i]
      p.x += p.vx
      p.y += p.vy
      p.life -= p.decay
      if (p.life <= 0) {
        particles.splice(i, 1)
        continue
      }
      const alpha = Math.max(p.life, 0) * 0.7
      ctx.globalAlpha = alpha
      ctx.fillStyle = p.color
      ctx.shadowColor = p.color
      ctx.shadowBlur = p.r * 3
      ctx.beginPath()
      ctx.arc(p.x, p.y, p.r * p.life, 0, Math.PI * 2)
      ctx.fill()
    }
    ctx.shadowBlur = 0
    ctx.globalAlpha = 1
  }
  rafId = requestAnimationFrame(tick)
}

onMounted(() => {
  const canvas = canvasRef.value
  if (!canvas) return

  ctx = canvas.getContext('2d')
  resize()
  window.addEventListener('resize', resize)
  window.addEventListener('mousemove', onMouseMove)
  rafId = requestAnimationFrame(tick)
})

onUnmounted(() => {
  cancelAnimationFrame(rafId)
  window.removeEventListener('resize', resize)
  window.removeEventListener('mousemove', onMouseMove)
})
</script>

<style scoped>
/*
  position:fixed + z-index:-1 で、body自身の背景色より前面・通常コンテンツより背面に固定する。
  カードやナビ等は各自が不透明な背景色を持つため、その部分だけ自然にパーティクルが隠れる。
*/
.qs-particle-bg {
  position: fixed;
  inset: 0;
  z-index: -1;
  pointer-events: none;
}
</style>
