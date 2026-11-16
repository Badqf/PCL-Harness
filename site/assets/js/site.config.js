/* ============================================================================
 * site.config.js —— PCL Harness 站点内容
 * ----------------------------------------------------------------------------
 * 结构遵循模板约定：改这一个文件就能换文案，不需要动 index.html 和 style.css。
 * 所有数字来自 3.9.5 的源码与发布包：18 项权限（8 低 / 9 中 / 1 高）、9 个接口、
 * 6,156,288 字节安装包、市场三条条目、TUN 网段 10.114.114.0/24。
 * ==========================================================================*/

window.SITE = {
  /* ---------- 1. 品牌 ---------- */
  brand: {
    name: 'PCL Harness',
    short: 'P',
    tagline: '能装插件的 Minecraft 启动器'
  },

  meta: {
    version: 'v3.9.5',
    badge: '基于 PCL2 开源版',
    license: '非官方 · 保留署名',
    year: null
  },

  /* ---------- 2. 链接 ---------- */
  links: {
    github: 'https://github.com/Badqf/PCL-Harness',
    issues: 'https://github.com/Badqf/PCL-Harness/issues',
    releases: 'https://github.com/Badqf/PCL-Harness/releases',
    docs: 'https://github.com/Badqf/PCL-Harness#readme',
    market: 'https://github.com/Badqf/PCL-Harness-Market',
    email: ''
  },

  /* ---------- 3. 页面文案 ---------- */
  text: {
    navHome: '首页',
    navOverview: '概览',
    navFeatures: '功能',
    navShowcase: '界面',
    navReviews: '市场',
    navContact: '联系',

    heroTitleLead: '能装插件的',
    heroTitleAccent: 'Minecraft 启动器',
    heroDesc: '基于 PCL2 开源版。装一个插件，启动器就多一块界面；API v8 和 SDK 都在，你自己也能写。PCL2 原有的版本、下载、Mod、账号，一个不少。',
    heroPrimary: '下载 3.9.5',
    heroSecondary: '看它怎么装',

    overviewEyebrow: '产品概览',
    overviewTitle: '启动器该做的事没变，能加的东西变多了',
    overviewDesc: 'PCL2 的布局、字体和操作都保留。变化在市场：插件从索引里装上，权限先摊开给你看，写一个插件只要一份清单加一个 DLL。',
    overviewBullets: [
      '独立配置目录 PCLHarness\\，与原版 PCL 并存',
      '权限按低、中、高分级，启用时弹一次',
      '插件可回滚，卸载进 Windows 回收站',
      '联机走 EasyTier P2P，一串房间码就够'
    ],

    featuresEyebrow: '核心功能',
    featuresTitle: '六件事，决定它和原版 PCL 的区别',
    featuresDesc: '插件、权限、联机、主题、目录、开发接口。',

    showcaseEyebrow: '界面',
    showcaseTitle: '四个页面，都是 3.9.5 的实机截图',
    showcaseDesc: '点标签切换，截图直接用启动器窗口，没有重画。',

    stepsEyebrow: '怎么开始',
    stepsTitle: '三步，从下载到第一个插件',

    reviewsEyebrow: '市场',
    reviewsTitle: '插件市场',
    reviewsDesc: '条目来自社区维护的索引；版本、体积、权限都写在卡片上，装包会校验 SHA-256。',

    contactEyebrow: '联系我们',
    contactTitle: '有问题，或想一起把它做得更好？',
    contactDesc: '这是开源项目，最有效的沟通方式是 GitHub Issues。填写右侧表单会生成一份预填好的 Issue 草稿，不会向任何服务器发送数据。',
    contactSideTitle: '其他入口',
    formNote: '提交不会发送数据，只会在本地生成 GitHub Issue 草稿链接。',
    formSubmit: '生成 Issue 草稿',
    formSending: '正在生成…'
  },

  /* ---------- 4. Hero 数据条 ---------- */
  stats: [
    { value: '3.9.5', label: '版本' },
    { value: '18', label: '项权限' },
    { value: '9', label: '个接口' },
    { value: '5.9MB', label: '安装包' }
  ],

  /* ---------- 5. 功能卡片（icon: bolt / layers / puzzle / palette / shield / code / globe / spark） ---------- */
  features: [
    {
      icon: 'puzzle',
      tag: '市场',
      title: '市场就在启动器里',
      desc: '发现、收藏、已安装、已禁用四个视图。索引来自社区，安装包校验 SHA-256，装完直接出现在顶栏。'
    },
    {
      icon: 'shield',
      tag: '权限',
      title: '权限先摊开',
      desc: '18 项权限分三级，插件卡片上直接写清要什么。process 等同于在你的电脑上执行代码。'
    },
    {
      icon: 'globe',
      tag: '联机',
      title: '一串房间码',
      desc: 'EasyTier P2P 打洞，房主 10.114.114.1，加入者 .2~.254。TUN 可选，任意游戏任意端口。'
    },
    {
      icon: 'palette',
      tag: '主题',
      title: '深浅两套主题',
      desc: '色相限定 200–230°，四档预设：标准蓝、冰蓝、海蓝、靛青。换主题自动重启。'
    },
    {
      icon: 'layers',
      tag: '目录',
      title: '配置各过各的',
      desc: 'PCLHarness\\ 与 %AppData%\\Roaming\\PCLHarness\\。首启可把旧 PCL 的设置迁过来，旧文件不动。'
    },
    {
      icon: 'code',
      tag: '开发',
      title: '三样东西写插件',
      desc: 'plugin.json、一个 DLL、一个目录。API v8 提供 9 个接口，VB 和 C# 都能写。'
    }
  ],

  /* ---------- 6. 演示标签页 ---------- */
  showcase: {
    tabs: [
      {
        id: 'home',
        label: '启动页',
        title: '打开就是这一屏',
        desc: '正版 / 离线、皮肤、版本选择都在左边；右边跟当前版本的说明，切换版本不用来回跳。',
        bullets: ['启动游戏与版本设置并排', '新闻页跟着当前版本走', '浅色主题用同一套布局']
      },
      {
        id: 'plugins',
        label: '插件管理',
        title: '每个插件，权限写在脸上',
        desc: '已发现的插件排成一行：版本、作者、权限串、最低 API、签名来源。禁用之后入口立刻消失，已下载的内容不删。',
        bullets: ['18 项权限，低 / 中 / 高三级', '未签名会标注来源需自行确认', '导入、重新扫描、下载市场']
      },
      {
        id: 'market',
        label: '插件市场',
        title: '市场不用开浏览器',
        desc: '发现 3 条、已安装 2 条。索引从镜像拉取，断网时读本地缓存；条目里有作者、体积和权限。',
        bullets: ['一键安装，装完直接出现在顶栏', '收藏与已禁用分开看', '索引由社区维护']
      },
      {
        id: 'link',
        label: '联机',
        title: '不用开服，一串房间码',
        desc: 'EasyTier 打一条点对点的隧道，启动器再把它装成一块虚拟网卡，游戏以为你们在同一个局域网里。',
        bullets: ['房主 10.114.114.1，加入者 .2~.254', 'TUN 需要管理员权限与 wintun', '对称 NAT 与 CGNAT 仍然连不上']
      }
    ]
  },

  /* ---------- 7. 使用步骤 ---------- */
  steps: [
    { no: '01', title: '下载并解压', desc: 'Windows 10 / 11 x64，需要 .NET Framework 4.8。6,156,288 字节，免安装；首次启动会问要不要迁移旧 PCL 的设置。' },
    { no: '02', title: '安装插件', desc: '打开插件市场，挑需要的装上。装完顶栏会多出对应页签，禁用不会删掉已经下载的内容。' },
    { no: '03', title: '写你自己的', desc: '建一个目录，放 plugin.json 和一个 DLL，实现 OnLoad / OnUnload；回到插件页点「重新扫描」，它就出现在列表里。' }
  ],

  /* ---------- 8. 市场条目（复用评价卡版式） ---------- */
  testimonials: [
    {
      quote: '提供启动器「下载」页面：安装游戏、Mod、整合包、资源包、光影包与数据包。仅申请 ui 权限。',
      name: '下载页面',
      role: '官方 · v1.0.1 · 3.8 KB · 2026-11-08',
      initials: '下'
    },
    {
      quote: '提供启动器「联机」页面：房间码 P2P 直连，支持虚拟局域网（TUN）模式。仅申请 ui 权限。',
      name: '联机页面',
      role: '官方 · v1.0.1 · 3.8 KB · 2026-11-08',
      initials: '联'
    },
    {
      quote: '在「启动」页右侧卡片里和 AI 对话：流式输出、代码块、用量显示、本地保存历史；可调用工具管理实例与模组。',
      name: 'AI 助手',
      role: '第三方 · v0.1.0 · 34 KB · API v7 · 15 项权限',
      initials: 'AI'
    }
  ]
};
