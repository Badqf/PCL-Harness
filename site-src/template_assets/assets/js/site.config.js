/* ============================================================================
 * site.config.js —— 模板内容配置（改这一个文件就能换成你的产品）
 * ----------------------------------------------------------------------------
 * ⚠️ 除 links 外，以下文案均为「演示占位内容」，用于展示版式效果。
 *    换产品时：改 brand / text / stats / features / showcase / steps /
 *    testimonials 即可，不需要动 index.html 和 style.css。
 *
 * 真实开源项目：https://github.com/Badqf/PCL-Harness
 * 本文件使用普通全局变量（非 ES Module），便于直接双击 index.html 本地预览。
 * ==========================================================================*/

window.SITE = {
  /* ---------- 1. 品牌 ---------- */
  brand: {
    name: 'Aurora',              // 产品名（导航栏、页脚）
    short: 'AU',                 // Logo 里的缩写，2 个字符最佳
    tagline: '让好产品，自己说话'   // 页脚一句话
  },

  meta: {
    version: 'v1.0.0',           // 版本徽章
    badge: '开源项目 · 欢迎共建',  // Hero 顶部徽章
    license: '开源许可',          // 页脚许可说明（按需改成 MIT / GPL 等）
    year: null                   // null = 自动使用当前年份
  },

  /* ---------- 2. 链接（已指向真实仓库，可按需增减） ---------- */
  links: {
    github: 'https://github.com/Badqf/PCL-Harness',
    issues: 'https://github.com/Badqf/PCL-Harness/issues',
    releases: 'https://github.com/Badqf/PCL-Harness/releases',
    docs: 'https://github.com/Badqf/PCL-Harness#readme',
    market: 'https://github.com/Badqf/PCL-Harness-Market',
    email: ''                    // 留空则不显示邮箱入口
  },

  /* ---------- 3. 页面文案 ---------- */
  text: {
    navHome: '首页',
    navOverview: '产品概览',
    navFeatures: '核心功能',
    navShowcase: '演示',
    navReviews: '用户评价',
    navContact: '联系我们',

    heroTitleLead: '为创造而生的',
    heroTitleAccent: '下一代产品体验',
    heroDesc: '简洁、快速、可扩展。把复杂的留给底层，把简单留给每一个使用者。这是一款真正为效率与体验而设计的开源产品。',
    heroPrimary: '查看源码',
    heroSecondary: '探索功能',

    overviewEyebrow: '产品概览',
    overviewTitle: '一个产品，解决一整条链路的问题',
    overviewDesc: '从第一次打开到深度使用，每个环节都经过重新思考。没有冗余步骤，也没有需要反复查阅文档才能理解的设置项。',
    overviewBullets: [
      '统一的工作流，减少在不同工具之间来回切换',
      '清晰的默认配置，零门槛开始，按需逐步深入',
      '开放接口与完整文档，方便二次开发与集成'
    ],

    featuresEyebrow: '核心功能与亮点',
    featuresTitle: '把细节做到位，体验自然不一样',
    featuresDesc: '六个设计原则，贯穿产品的每个模块。',

    showcaseEyebrow: '产品演示',
    showcaseTitle: '眼见为实，看看它如何工作',
    showcaseDesc: '下方为界面示意占位图。替换为真实截图时，把图片放进 assets/img/ 并修改 index.html 中的对应面板即可。',

    stepsEyebrow: ' HOW IT WORKS ',
    stepsTitle: '三步开始使用',

    reviewsEyebrow: '用户评价',
    reviewsTitle: '来自真实使用者的反馈',
    reviewsDesc: '以下为模板示例文案，请替换为已获授权的真实评价。',

    contactEyebrow: '联系我们',
    contactTitle: '有问题，或想一起把它做得更好？',
    contactDesc: '这是开源项目，最有效的沟通方式是 GitHub Issues。填写右侧表单会生成一份预填好的 Issue 草稿，不会向任何服务器发送数据。',
    contactSideTitle: '其他联系渠道',
    formNote: '模板演示：提交不会发送数据，只会在本地生成 GitHub Issue 草稿链接。',
    formSubmit: '生成 Issue 草稿',
    formSending: '正在生成…'
  },

  /* ---------- 4. Hero 数据条（label 上、value 下） ---------- */
  stats: [
    { value: '100%', label: '开源透明' },
    { value: '< 1s', label: '冷启动响应' },
    { value: '6+', label: '核心模块' }
  ],

  /* ---------- 5. 功能卡片 ----------
   * icon 可选：bolt / layers / puzzle / palette / shield / code / globe / spark
   * ------------------------------------------------------------------------ */
  features: [
    {
      icon: 'bolt',
      tag: '性能',
      title: '轻快响应',
      desc: '精简的核心与按需加载策略，让每一次操作都即时反馈，不把时间浪费在等待上。'
    },
    {
      icon: 'layers',
      tag: '架构',
      title: '模块化设计',
      desc: '功能彼此解耦，可单独启用或替换。升级某个模块不会影响其他部分的稳定性。'
    },
    {
      icon: 'puzzle',
      tag: '扩展',
      title: '开放插件体系',
      desc: '清晰的接口与示例工程，让第三方扩展像搭积木一样简单，生态由社区共同生长。'
    },
    {
      icon: 'palette',
      tag: '体验',
      title: '自由主题',
      desc: '明暗两套配色与可调的强调色，界面会跟随系统偏好，也支持随时手动切换。'
    },
    {
      icon: 'shield',
      tag: '可靠',
      title: '数据在自己手里',
      desc: '配置与数据本地保存，变更可回滚，不需要把隐私交给任何第三方服务。'
    },
    {
      icon: 'code',
      tag: '开源',
      title: '完全透明',
      desc: '源码、构建流程与版本记录全部公开，欢迎审阅、复刻，也欢迎提交第一个 PR。'
    }
  ],

  /* ---------- 6. 演示区标签页 ----------
   * 每个 tab 对应 index.html 里一个 data-panel 面板
   * ------------------------------------------------------------------------ */
  showcase: {
    tabs: [
      {
        id: 'home',
        label: '主界面',
        title: '一眼看到最重要的信息',
        desc: '首页把状态、最近项目与常用操作收在一屏内，减少来回跳转。',
        bullets: ['关键状态实时可见', '常用操作一键直达', '信息密度可自行调整']
      },
      {
        id: 'modules',
        label: '功能模块',
        title: '按需要组合你的工作流',
        desc: '模块独立开关，启用后自动出现在导航中，停用即收起，界面始终干净。',
        bullets: ['按需启用，互不干扰', '统一的配置入口', '支持热更新与回滚']
      },
      {
        id: 'theme',
        label: '个性化',
        title: '调成你喜欢的样子',
        desc: '配色、密度与排版都能微调，深浅主题切换即时生效，无需重启。',
        bullets: ['深浅主题一键切换', '强调色自由调节', '偏好本地持久化']
      }
    ]
  },

  /* ---------- 7. 使用步骤 ---------- */
  steps: [
    { no: '01', title: '获取项目', desc: '从 GitHub 仓库克隆源码，或直接下载最新的 Release 构建。' },
    { no: '02', title: '按需配置', desc: '修改配置文件，启用需要的模块，几分钟即可完成初始化。' },
    { no: '03', title: '开始使用', desc: '运行起来后，有问题随时提 Issue，也欢迎参与共建。' }
  ],

  /* ---------- 8. 用户评价（示例数据，务必替换） ---------- */
  testimonials: [
    {
      quote: '最打动我的是「不用设置就能用」。装好之后几乎没有学习成本，界面干净，该有的功能一个不少。',
      name: '示例用户 A',
      role: '独立开发者',
      initials: 'A'
    },
    {
      quote: '模块化这一点做得很克制，没有为了扩展而扩展。我们团队只开了需要的部分，维护成本明显降低了。',
      name: '示例用户 B',
      role: '开源社区维护者',
      initials: 'B'
    },
    {
      quote: '源码和构建流程都公开，遇到问题能自己定位，提的 Issue 也很快有回应，这种透明度很难得。',
      name: '示例用户 C',
      role: '产品设计师',
      initials: 'C'
    }
  ]
};
