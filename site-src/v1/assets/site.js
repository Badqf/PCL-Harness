/* PCL Harness 官网 · 交互
   1 装配台：开关 ↔ 顶栏页签
   2 市场：条目 ↔ 右边预览
   3 权限：风险筛选
   4 入场编排（IntersectionObserver，逐组错开） */

(() => {
  'use strict';

  document.documentElement.classList.add('js');
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ---------- 1 装配台 ---------- */

  const demo = document.querySelector('[data-demo]') || { querySelector: () => null, querySelectorAll: () => [] };
  if (demo) {
    const switches = Array.from(demo.querySelectorAll('.switch'));
    const count = demo.querySelector('[data-count]');

    const sync = () => {
      let on = 0;
      switches.forEach((sw) => {
        const state = sw.getAttribute('aria-checked') === 'true';
        if (state) on += 1;
        const tab = demo.querySelector('.tab[data-tab="' + sw.dataset.plugin + '"]');
        if (!tab) return;
        const was = tab.dataset.on !== 'false';
        tab.dataset.on = state ? 'true' : 'false';
        if (state && !was) {
          tab.classList.remove('is-new');
          void tab.offsetWidth;
          tab.classList.add('is-new');
        }
      });
      if (count) count.textContent = '已启用 ' + on + ' / ' + switches.length;
    };

    switches.forEach((sw) => {
      sw.addEventListener('click', () => {
        const next = sw.getAttribute('aria-checked') !== 'true';
        sw.setAttribute('aria-checked', next ? 'true' : 'false');
        sync();
      });
    });

    sync();
  }

  /* ---------- 2 市场 ---------- */

  const rows = Array.from(document.querySelectorAll('.mrow'));
  if (rows.length) {
    const shots = Array.from(document.querySelectorAll('.shot'));
    const cap = document.querySelector('[data-cap]');

    const pick = (row) => {
      rows.forEach((r) => {
        const on = r === row;
        r.classList.toggle('is-on', on);
        r.setAttribute('aria-selected', on ? 'true' : 'false');
      });
      shots.forEach((s) => s.classList.toggle('is-on', s.dataset.shot === row.dataset.prev));
      if (cap && row.dataset.cap) cap.textContent = row.dataset.cap;
    };

    rows.forEach((row) => {
      row.addEventListener('click', () => pick(row));
      row.addEventListener('mouseenter', () => pick(row));
      row.addEventListener('focus', () => pick(row));
    });
  }

  /* ---------- 3 权限筛选 ---------- */

  const ptabs = Array.from(document.querySelectorAll('.ptab'));
  if (ptabs.length) {
    const perms = Array.from(document.querySelectorAll('.perm'));
    ptabs.forEach((tab) => {
      tab.addEventListener('click', () => {
        ptabs.forEach((t) => {
          const on = t === tab;
          t.classList.toggle('is-on', on);
          t.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        const risk = tab.dataset.risk;
        perms.forEach((p) => {
          p.hidden = risk !== 'all' && p.dataset.risk !== risk;
        });
      });
    });
  }

  /* ---------- 4 入场编排 ---------- */

  const items = Array.from(document.querySelectorAll([
    '.stack > .slab',
    '.split--head > *',
    '.demo',
    '.market__list > *',
    '.market__right > *',
    '.figure',
    '.split__text > *',
    '.theme > *',
    '.theme__panel > *',
    '.steps > li',
    '.code',
    '.api > div',
    '.qa',
    '.end__line',
    '.end__sub',
    '.rel'
  ].join(', ')));

  if (items.length && !reduce) {
    const seen = new Map();
    items.forEach((el) => {
      const key = el.parentElement;
      const n = seen.get(key) || 0;
      el.style.setProperty('--d', Math.min(n, 5) * 70 + 'ms');
      seen.set(key, n + 1);
      el.classList.add('reveal');
    });

    if ('IntersectionObserver' in window) {
      const io = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          entry.target.classList.add('is-in');
          io.unobserve(entry.target);
        });
      }, { rootMargin: '0px 0px -6% 0px', threshold: 0.04 });
      items.forEach((el) => io.observe(el));
    } else {
      items.forEach((el) => el.classList.add('is-in'));
    }
  }
})();
