/* ============================================================================
 * site-data.mjs —— 部署时生成 site/assets/js/site-data.js
 * ----------------------------------------------------------------------------
 * 数据来源（能取到哪个用哪个，取不到的沿用文件里的旧值，不凭空编）：
 *   version / sizeBytes / published  ← GitHub Releases 的最新发布（tag 与 .exe 资产）
 *   perms / ifaces                   ← 源码 ModPlugin.vb（DescribePermission 的 Case 数、Public Interface 数）
 *   market                           ← 插件市场的 index.json（名称 / 简介 / 版本 / 体积 / 日期 / 是否官方）
 *
 * 用法：
 *   node .github/scripts/site-data.mjs                      # CI 里跑
 *   node .github/scripts/site-data.mjs --index "D:\index.json"   # 用本地索引试跑
 *   GITHUB_TOKEN=... node .github/scripts/site-data.mjs     # 本地带 token 跑
 *
 * 没有 token 时跳过 Releases 查询，只更新源码与市场能算出来的部分。
 * ==========================================================================*/

import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const ROOT = process.cwd();
const VB = path.join(ROOT, 'Plain Craft Launcher 2', 'Modules', 'ModPlugin.vb');
const DATA = path.join(ROOT, 'site', 'assets', 'js', 'site-data.js');

const argOf = (flag) => {
  const i = process.argv.indexOf(flag);
  return i > -1 ? process.argv[i + 1] : '';
};

const REPO = argOf('--repo') || process.env.GITHUB_REPOSITORY || 'Badqf/PCL-Harness';
const TOKEN = process.env.GITHUB_TOKEN || '';
const INDEX_LOCAL = argOf('--index');
const MARKET_INDEX_URL =
  process.env.MARKET_INDEX_URL ||
  'https://raw.githubusercontent.com/Badqf/PCL-Harness-Market/main/index.json';

const warn = (msg) => console.warn(`[site-data] ${msg}`);

/* ---------- 1. 读上一次的值，作为 API 不可用时的兜底 ---------- */
async function readPrevious() {
  const fallback = {
    version: '',
    sizeBytes: 0,
    sizeLabel: '',
    published: '',
    perms: 0,
    ifaces: 0,
    market: [],
  };
  try {
    const text = await readFile(DATA, 'utf8');
    const m = text.match(/window\.SITE_DATA\s*=\s*(\{[\s\S]*?\});/);
    if (!m) return fallback;
    return { ...fallback, ...JSON.parse(m[1]) };
  } catch {
    warn('读不到旧的 site-data.js，按空值处理（页面会显示 —）');
    return fallback;
  }
}

/* ---------- 2. 源码里能数出来的：权限条数、接口数 ---------- */
async function readSourceCounts() {
  try {
    const text = await readFile(VB, 'utf8');
    const lines = text.split(/\r?\n/);

    const start = lines.findIndex((l) => /Function DescribePermission\b/.test(l));
    const end = lines.findIndex((l) => /Function PermissionRisk\b/.test(l));
    const perms =
      start > -1 && end > start
        ? lines.slice(start, end).filter((l) => /^\s*Case\s+"/.test(l)).length
        : 0;

    const ifaces = lines.filter((l) => /^\s*Public Interface\s+\w+/.test(l)).length;
    return { perms, ifaces };
  } catch (err) {
    warn(`读不到 ModPlugin.vb：${err.message}`);
    return { perms: 0, ifaces: 0 };
  }
}

/* ---------- 3. 最新发布的 tag / exe 体积 / 发布日期 ---------- */
async function readLatestRelease() {
  if (!TOKEN) {
    warn('没有 GITHUB_TOKEN，跳过 Releases 查询（版本号沿用旧值）');
    return null;
  }
  try {
    const res = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
      signal: AbortSignal.timeout(15000),
      headers: {
        Authorization: `Bearer ${TOKEN}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
      },
    });
    if (!res.ok) {
      warn(`Releases 接口返回 ${res.status}，版本号沿用旧值`);
      return null;
    }
    const rel = await res.json();
    const version = String(rel.tag_name || '').replace(/^v/i, '').trim();
    const exe = (rel.assets || []).find((a) => /\.exe$/i.test(a.name));
    return {
      version,
      sizeBytes: exe ? Number(exe.size) : 0,
      published: (rel.published_at || '').slice(0, 10),
    };
  } catch (err) {
    warn(`Releases 查询失败：${err.message}`);
    return null;
  }
}

/* ---------- 4. 插件市场索引 → 卡片数据 ---------- */
const kb = (bytes) => {
  const b = Number(bytes) || 0;
  if (!b) return '';
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
  return `${(b / 1024 / 1024).toFixed(1)} MB`;
};

// "11/08/2026 01:30:00" (M/D/YYYY) → "2026-11-08"
const day = (raw) => {
  const m = String(raw || '').match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})/);
  if (!m) return String(raw || '').slice(0, 10);
  return `${m[3]}-${m[1].padStart(2, '0')}-${m[2].padStart(2, '0')}`;
};

function mapMarket(index) {
  const list = Array.isArray(index) ? index : index?.plugins || [];
  if (!list.length) return [];
  return list
    .filter((p) => p && p.latest && p.latest.version)
    .sort((a, b) => {
      if (!!a.featured !== !!b.featured) return a.featured ? -1 : 1;
      return new Date(day(b.latest.publishedAt)) - new Date(day(a.latest.publishedAt));
    })
    .slice(0, 6)
    .map((p) => {
      const official = (p.tags || []).includes('官方');
      return {
        id: p.id || '',
        name: p.name || p.id || '',
        summary: p.summary || String(p.description || '').split('\n')[0],
        meta: [
          official ? '官方' : '第三方',
          'v' + p.latest.version,
          day(p.latest.publishedAt),
        ]
          .filter(Boolean)
          .join(' · '),
      };
    });
}

async function readMarket() {
  try {
    let raw;
    if (INDEX_LOCAL) {
      raw = await readFile(INDEX_LOCAL, 'utf8');
    } else {
      const res = await fetch(MARKET_INDEX_URL, { signal: AbortSignal.timeout(15000) });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      raw = await res.text();
    }
    const mapped = mapMarket(JSON.parse(raw));
    if (!mapped.length) throw new Error('索引里没有可用条目');
    return mapped;
  } catch (err) {
    warn(`市场索引没取到（${err.message}），沿用旧条目`);
    return null;
  }
}

/* ---------- 5. 合并写回 ---------- */
const mb = (bytes) => (bytes ? `${(bytes / 1024 / 1024).toFixed(1)}MB` : '');

const prev = await readPrevious();
const counts = await readSourceCounts();
const rel = await readLatestRelease();
const market = await readMarket();

const sizeBytes = (rel && rel.sizeBytes) || prev.sizeBytes;
const next = {
  version: (rel && rel.version) || prev.version || '',
  sizeBytes,
  sizeLabel: mb(sizeBytes),
  published: (rel && rel.published) || prev.published || '',
  perms: counts.perms || prev.perms,
  ifaces: counts.ifaces || prev.ifaces,
  market: market || prev.market || [],
};

const body = `/* ============================================================================
 * site-data.js —— 由 CI 生成的数据（本地这份是最后一次生成的快照）
 * ----------------------------------------------------------------------------
 * 部署时 .github/scripts/site-data.mjs 会读取最新 Release 与市场索引，
 * 重新生成这个文件：版本号、安装包体积、权限数、接口数、市场条目都会自动更新，
 * 不用为了改数字重新推站。不要手改，下次部署会被覆盖。
 * ==========================================================================*/
window.SITE_DATA = ${JSON.stringify(next, null, 2)};
`;

await writeFile(DATA, body, 'utf8');
console.log(
  '[site-data] 写入完成：',
  JSON.stringify({
    version: next.version,
    sizeLabel: next.sizeLabel,
    perms: next.perms,
    ifaces: next.ifaces,
    market: next.market.length,
  }),
);
