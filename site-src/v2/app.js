/* PCL Harness 官网 v2 · 入场、视差、进度
   原生滚动驱动动画优先（CSS），仅在浏览器不支持时才挂 JS 兜底 */
(() => {
  'use strict';

  const root = document.documentElement;
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const nativeSDA = CSS.supports('(animation-timeline: view()) and (animation-range: entry)');
  const nativeScroll = CSS.supports('animation-timeline', 'scroll()');
  root.classList.add('js');

  const paint = () => root.classList.add('is-ready');
  if (reduce) paint();
  else requestAnimationFrame(() => requestAnimationFrame(paint));

  /* 入场：统一打标记，动画交给 CSS 的 .rv */
  const sel = [
    '.hero__text > *', '.hero__plate', '.ticker',
    '.h2', '.body', '.plate', '.spec', '.facts',
    '.quote', '.risks', '.code', '.pair', '.presets'
  ].join(', ');

  const items = Array.from(document.querySelectorAll(sel));
  const seen = new Map();
  items.forEach((el) => {
    const parent = el.parentElement;
    const n = seen.get(parent) || 0;
    seen.set(parent, n + 1);
    el.style.setProperty('--d', Math.min(n, 5) * 70 + 'ms');
    el.classList.add('rv');
    if (el.matches('.plate, .hero__plate')) el.classList.add('rv--plate');
    else if (el.matches('.body, .facts, .quote, .risks, .presets')) el.classList.add('rv--flat');
  });

  if (!reduce && 'IntersectionObserver' in window) {
    /* 段方块：一次性状态，始终走 IO */
    const so = new IntersectionObserver((entries) => {
      entries.forEach((entry) => entry.target.classList.toggle('is-in', entry.isIntersecting));
    }, { threshold: 0.18 });
    document.querySelectorAll('.sec').forEach((sec) => so.observe(sec));

    /* 入场兜底：只在没有 view() 时间线时才需要 */
    if (!nativeSDA) {
      const io = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          entry.target.classList.add('is-in');
          io.unobserve(entry.target);
        });
      }, { rootMargin: '0px 0px -4% 0px', threshold: 0.02 });
      items.forEach((el) => io.observe(el));
    }
  } else if (!nativeSDA) {
    items.forEach((el) => el.classList.add('is-in'));
  }

  /* 进度条兜底 */
  if (!nativeScroll) {
    const bar = document.querySelector('.prog i');
    const progress = () => {
      if (!bar) return;
      const max = root.scrollHeight - window.innerHeight;
      const p = max > 0 ? Math.min(1, Math.max(0, window.scrollY / max)) : 0;
      bar.style.transform = 'scaleX(' + p.toFixed(4) + ')';
    };
    window.addEventListener('scroll', progress, { passive: true });
    window.addEventListener('resize', progress, { passive: true });
    progress();
  }

  /* 视差兜底 */
  if (!reduce && !nativeSDA) {
    const plates = Array.from(document.querySelectorAll('.plate:not(.hero__plate) img'));
    let raf = 0;
    const parallax = () => {
      raf = 0;
      const vh = window.innerHeight;
      plates.forEach((img) => {
        const r = img.getBoundingClientRect();
        if (r.bottom < -120 || r.top > vh + 120) return;
        const t = (r.top + r.height / 2 - vh / 2) / vh;
        img.style.transform = 'translate3d(0,' + (t * -12).toFixed(2) + 'px,0) scale(1.04)';
      });
    };
    const onScroll = () => { if (!raf) raf = requestAnimationFrame(parallax); };
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll, { passive: true });
    parallax();
  }
})();
