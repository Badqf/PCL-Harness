/* ============================================================================
   main.js — 页面交互（无依赖、无构建、可直接双击 index.html 预览）
   01 配置注入 / 02 主题 / 03 导航 / 04 入场动画
   05 演示标签 / 06 联系表单
   ==========================================================================*/
(function () {
  'use strict';

  var SITE = window.SITE || {};
  var $ = function (s, root) { return (root || document).querySelector(s); };
  var $$ = function (s, root) { return Array.prototype.slice.call((root || document).querySelectorAll(s)); };

  /* 按 "a.b.c" 路径取配置值 */
  function get(path) {
    return String(path).split('.').reduce(function (o, k) {
      return (o && o[k] !== undefined) ? o[k] : undefined;
    }, SITE);
  }
  function esc(v) {
    return String(v == null ? '' : v)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  /* ---------- 01 配置注入 ---------- */
  var ICONS = {
    bolt: '<path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z"/>',
    layers: '<path d="m12 2 9 5-9 5-9-5 9-5z"/><path d="m3 12 9 5 9-5M3 17l9 5 9-5"/>',
    puzzle: '<path d="M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z"/>',
    palette: '<circle cx="12" cy="12" r="9"/><circle cx="8" cy="9" r="1"/><circle cx="12" cy="7.5" r="1"/><circle cx="16" cy="9" r="1"/><path d="M12 21a9 9 0 0 1 0-18"/>',
    shield: '<path d="M12 2 4 6v6c0 5 3.4 9.2 8 10 4.6-.8 8-5 8-10V6l-8-4z"/><path d="m9 12 2 2 4-4"/>',
    code: '<path d="m8 8-4 4 4 4M16 8l4 4-4 4M14 4l-4 16"/>',
    globe: '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18"/>',
    spark: '<path d="M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8L12 3z"/>'
  };
  function icon(name) {
    return '<svg viewBox="0 0 24 24" aria-hidden="true">' + (ICONS[name] || ICONS.spark) + '</svg>';
  }

  function applyText() {
    $$('[data-text]').forEach(function (el) {
      var v = get(el.getAttribute('data-text'));
      if (v !== undefined) el.textContent = v;
    });
    var name = get('brand.name');
    if (name) document.title = name + ' — 产品介绍';
    var y = $('#year');
    if (y) y.textContent = get('meta.year') || new Date().getFullYear();
  }

  function applyLinks() {
    $$('[data-link]').forEach(function (a) {
      var url = get('links.' + a.getAttribute('data-link'));
      if (!url) { a.hidden = true; return; }
      a.setAttribute('href', url);
      if (a.getAttribute('target') === '_blank') a.setAttribute('rel', 'noopener noreferrer');
    });
    $$('[data-link-label]').forEach(function (el) {
      var url = get('links.' + el.getAttribute('data-link-label')) || '';
      el.textContent = url.replace(/^https?:\/\//, '');
    });
  }

  function renderLists() {
    /* Hero 数据条 */
    var stats = $('#stats-list');
    if (stats) stats.innerHTML = (get('stats') || []).map(function (s) {
      return '<div class="stat"><b>' + esc(s.value) + '</b><span>' + esc(s.label) + '</span></div>';
    }).join('');

    /* 概览要点 */
    var bullets = $('#overview-bullets');
    if (bullets) bullets.innerHTML = (get('text.overviewBullets') || [])
      .map(function (t) { return '<li>' + esc(t) + '</li>'; }).join('');

    /* 功能卡片 */
    var fg = $('#features-grid');
    if (fg) fg.innerHTML = (get('features') || []).map(function (f, i) {
      return '<article class="feature-card reveal" style="transition-delay:' + (i % 3) * 70 + 'ms">' +
        '<div class="fc-top"><span class="fc-icon">' + icon(f.icon) + '</span>' +
        '<span class="fc-tag">' + esc(f.tag || '') + '</span></div>' +
        '<h3>' + esc(f.title) + '</h3><p>' + esc(f.desc) + '</p></article>';
    }).join('');

    /* 演示标签 */
    var tabs = $('#showcase-tabs');
    if (tabs) tabs.innerHTML = (get('showcase.tabs') || []).map(function (t, i) {
      return '<button class="tab' + (i === 0 ? ' active' : '') + '" type="button" role="tab" ' +
        'aria-selected="' + (i === 0) + '" data-tab="' + esc(t.id) + '">' + esc(t.label) + '</button>';
    }).join('');

    /* 使用步骤 */
    var steps = $('#steps-list');
    if (steps) steps.innerHTML = (get('steps') || []).map(function (s, i) {
      return '<div class="step reveal" style="transition-delay:' + i * 80 + 'ms">' +
        '<span class="step-no">' + esc(s.no) + '</span>' +
        '<h4>' + esc(s.title) + '</h4><p>' + esc(s.desc) + '</p></div>';
    }).join('');

    /* 用户评价 */
    var rg = $('#reviews-grid');
    if (rg) rg.innerHTML = (get('testimonials') || []).map(function (t, i) {
      return '<article class="review-card reveal" style="transition-delay:' + i * 80 + 'ms">' +
        '<div class="review-quote">&ldquo;</div>' +
        '<div class="review-stars" aria-label="五星评价">★★★★★</div>' +
        '<blockquote>' + esc(t.quote) + '</blockquote>' +
        '<div class="review-user"><span class="avatar">' + esc(t.initials || 'U') + '</span>' +
        '<span><b>' + esc(t.name) + '</b><small>' + esc(t.role) + '</small></span></div></article>';
    }).join('');
  }

  /* ---------- 02 主题 ---------- */
  var THEME_KEY = 'product-site-theme';
  function readStore(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
  function writeStore(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* file:// 下可能受限 */ } }

  function setTheme(mode) {
    document.documentElement.setAttribute('data-theme', mode);
    var meta = $('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', mode === 'light' ? '#f5f7ff' : '#080b14');
    writeStore(THEME_KEY, mode);
  }
  function initTheme() {
    var saved = readStore(THEME_KEY);
    var preferLight = window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches;
    setTheme(saved || (preferLight ? 'light' : 'dark'));
    var btn = $('#theme-toggle');
    if (btn) btn.addEventListener('click', function () {
      setTheme(document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark');
    });
  }

  /* ---------- 03 导航 ---------- */
  function initNav() {
    var header = $('#site-header');
    var bar = $('#progress-bar');
    var backTop = $('#back-top');
    var menuBtn = $('#menu-btn');
    var menu = $('#mobile-menu');

    function onScroll() {
      var y = window.pageYOffset || document.documentElement.scrollTop;
      var max = document.documentElement.scrollHeight - window.innerHeight;
      if (bar) bar.style.width = (max > 0 ? Math.min(100, y / max * 100) : 0) + '%';
      if (header) header.classList.toggle('scrolled', y > 12);
      if (backTop) backTop.classList.toggle('show', y > 600);
    }
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll);
    onScroll();

    if (backTop) backTop.addEventListener('click', function () {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    });

    /* 移动端菜单 */
    function closeMenu() {
      if (!menu) return;
      menu.hidden = true;
      if (menuBtn) menuBtn.setAttribute('aria-expanded', 'false');
    }
    if (menuBtn && menu) {
      menuBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        var open = menu.hidden;
        menu.hidden = !open;
        menuBtn.setAttribute('aria-expanded', String(open));
      });
      $$('a', menu).forEach(function (a) { a.addEventListener('click', closeMenu); });
      document.addEventListener('click', function (e) {
        if (!menu.hidden && !menu.contains(e.target) && e.target !== menuBtn) closeMenu();
      });
      document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeMenu(); });
      window.addEventListener('resize', function () { if (window.innerWidth > 900) closeMenu(); });
    }

    /* 当前区块高亮 */
    var sections = $$('main section[id]');
    var navAnchors = $$('.nav-links a, .mobile-menu nav a');
    function setActive(id) {
      navAnchors.forEach(function (a) {
        a.classList.toggle('active', a.getAttribute('href') === '#' + id);
      });
    }
    if ('IntersectionObserver' in window) {
      var navObserver = new IntersectionObserver(function (entries) {
        entries.forEach(function (en) { if (en.isIntersecting) setActive(en.target.id); });
      }, { rootMargin: '-25% 0px -65% 0px', threshold: 0 });
      sections.forEach(function (s) { navObserver.observe(s); });
    } else {
      setActive('home');
    }
  }

  /* ---------- 04 入场动画 ---------- */
  function initReveal() {
    var els = $$('.reveal');
    if (!('IntersectionObserver' in window)) {
      els.forEach(function (el) { el.classList.add('in'); });
      return;
    }
    var ob = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) { en.target.classList.add('in'); ob.unobserve(en.target); }
      });
    }, { threshold: 0.08, rootMargin: '0px 0px -40px 0px' });
    els.forEach(function (el) { ob.observe(el); });
  }

  /* ---------- 05 演示标签 ---------- */
  function initShowcase() {
    var tabs = $$('.tab', $('#showcase-tabs'));
    var panels = $$('.mock-panel');
    var titleEl = $('#panel-title');
    var descEl = $('#panel-desc');
    var bulletsEl = $('#panel-bullets');
    var mockTitle = $('#mock-title');
    var data = get('showcase.tabs') || [];

    function activate(id) {
      var t = data.filter(function (x) { return x.id === id; })[0] || data[0];
      if (!t) return;
      tabs.forEach(function (b) {
        var on = b.getAttribute('data-tab') === t.id;
        b.classList.toggle('active', on);
        b.setAttribute('aria-selected', String(on));
      });
      panels.forEach(function (p) {
        p.classList.toggle('is-active', p.getAttribute('data-panel') === t.id);
      });
      if (titleEl) titleEl.textContent = t.title || '';
      if (descEl) descEl.textContent = t.desc || '';
      if (mockTitle) mockTitle.textContent = t.label || '';
      if (bulletsEl) bulletsEl.innerHTML = (t.bullets || [])
        .map(function (b) { return '<li>' + esc(b) + '</li>'; }).join('');
    }
    tabs.forEach(function (b) {
      b.addEventListener('click', function () { activate(b.getAttribute('data-tab')); });
    });
    activate(data.length ? data[0].id : 'home');
  }

  /* ---------- 06 联系表单（无后端，生成 GitHub Issue 草稿） ---------- */
  function initForm() {
    var form = $('#contact-form');
    var result = $('#form-result');
    if (!form || !result) return;

    var issueLink = $('#issue-link');
    var preview = $('#result-preview');
    var copyBtn = $('#copy-btn');
    var resetBtn = $('#reset-btn');
    var lastText = '';

    function markInvalid(el, bad) { el.classList.toggle('invalid', bad); }
    $$('input, select, textarea', form).forEach(function (el) {
      el.addEventListener('input', function () { markInvalid(el, false); });
      el.addEventListener('change', function () { markInvalid(el, false); });
    });

    form.addEventListener('submit', function (e) {
      e.preventDefault();
      var fields = $$('input, select, textarea', form);
      var firstBad = null;
      fields.forEach(function (el) {
        var ok = el.value.trim() !== '' && (el.type !== 'email' || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(el.value.trim()));
        markInvalid(el, !ok);
        if (!ok && !firstBad) firstBad = el;
      });
      if (firstBad) { firstBad.focus(); return; }

      var v = function (n) { return form.elements[n].value.trim(); };
      var title = '[' + v('topic') + '] ' + v('name') + ' 的反馈';
      var body = [
        '**主题**：' + v('topic'),
        '**称呼**：' + v('name'),
        '**联系邮箱**：' + v('email'),
        '',
        '### 留言内容',
        v('message'),
        '',
        '---',
        '> 该内容由产品介绍页模板的联系表单生成，请在提交前确认未包含敏感信息。'
      ].join('\n');
      lastText = title + '\n\n' + body;

      var base = (get('links.github') || '').replace(/\/+$/, '');
      var url = base + '/issues/new?title=' + encodeURIComponent(title) + '&body=' + encodeURIComponent(body);
      if (issueLink) issueLink.setAttribute('href', url);
      if (preview) preview.textContent = lastText;
      form.hidden = true;
      result.hidden = false;
      result.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    });

    function copyText(text, done) {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done, function () { fallback(); });
      } else { fallback(); }
      function fallback() {
        var ta = document.createElement('textarea');
        ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select();
        try { document.execCommand('copy'); } catch (err) { /* 忽略 */ }
        document.body.removeChild(ta);
        done();
      }
    }
    if (copyBtn) copyBtn.addEventListener('click', function () {
      copyText(lastText, function () {
        var old = copyBtn.textContent;
        copyBtn.textContent = '已复制 ✓';
        setTimeout(function () { copyBtn.textContent = old; }, 1800);
      });
    });
    if (resetBtn) resetBtn.addEventListener('click', function () {
      form.reset();
      $$('.invalid', form).forEach(function (el) { el.classList.remove('invalid'); });
      result.hidden = true;
      form.hidden = false;
      var first = $('#f-name'); if (first) first.focus();
    });
  }

  /* ---------- 启动 ---------- */
  function boot() {
    applyText();
    applyLinks();
    renderLists();
    initTheme();
    initNav();
    initShowcase();
    initForm();
    initReveal();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
