# WHITE ALBUM 2 DS（AYN Thor 双屏适配）

WHITE ALBUM 2 DS 是 WHITE ALBUM2 Godot 移植版的双屏分支：本目录及 `script/thor/` 增加 AYN Thor（上下双屏 Android 掌机）支持。上屏照常运行游戏，下屏是一个用游戏自身 UI 素材拼成的伴侣界面。

## 功能

**下屏页面**（页签可点，也可用 L1 切换）

| 页面 | 内容 |
| --- | --- |
| 文本记录 Backlog | 最新的台词在最上面，正在显示的一句不重复列出。点语音图标重播原声；没有配音的句子显示青色图标，点击用系统语音朗读日文原文。点一句台词打开「查词」，长按收藏。 |
| 收藏 Favorites | 「名台词」：收藏的句子（中文、日文原文、语音）；「生词本」：查词时加入的单词，可发送到 AnkiDroid。删除需要点两次确认。 |
| 快捷操作 Command | 存档、读档、快速存档、快速读档、设置、隐藏文本、自动、快进、返回、回到标题；出现选项时自动切到这一页，可直接点选。 |
| 操作说明 Guide | 操作说明，以及设置：文字大小、日文原文、汉字注音、系统语音自动朗读。 |

底栏和游戏内各页面一样显示当前 BGM 名称，音量条随音乐跳动；右侧显示状态和操作提示。

**学日语**
- 日文原文：中文补丁保留了原版脚本的字符串表顺序，所以每句中文都能找到 `script.pak` 中对应的日文原句，用游戏的日文字体绘制。
- 汉字注音：Kuromoji（IPADIC）在设备上分词得到读音，转换成游戏自己的注音标记 `<R漢字|かな>`；原作自带的注音保持不变。
- 查词：把一句日文拆成实词，显示读音、词性和释义（中文维基词典优先，JMdict 英文补充）。按读音、词性和常用度给同形词排序（いる → 居る），数词+量词等复合词合在一起查（三人）；点词条展开全部释义和其他同形词，「上一句 / 下一句」或十字键左右切换句子。登场角色名用 Kuromoji 用户词典标注读音（雪菜 せつな）。
- 生词本 → AnkiDroid：通过 AnkiDroid 的 ContentProvider 接口自动创建牌组「WHITE ALBUM2」和模板「WHITE ALBUM 2 DS 生词」，卡片包含单词、读音、释义、带注音的例句。第一次发送时需要在上屏弹出的系统窗口里允许访问 AnkiDroid。
- 系统语音：没有配音的句子可用 Android TTS 朗读日文原文（需要系统装有日语语音）。中文无法朗读：补丁把汉字映射到了别的字形槽位。如果 `Wa2Res/narration/<脚本>/<文本编号>.ogg` 存在（自己在电脑上预先生成的旁白录音，不随本项目发布），优先播放它们，走游戏的语音音量。

**手柄**
- 剧情中：A 推进，B 返回/关闭菜单，X 自动，Y 隐藏文本，按住 R1 快进，L1 切换下屏页面，十字键上下滚动文本记录。
- 标题与各菜单页：十字键/左摇杆在按钮之间移动（青色选框），A 确定，B 返回。

**快速存档**：使用菜单看不到的独立存档位 `sav100.sav`，不占用 100 个普通存档格。

## 实现概要

- `script/thor/ThorDualScreen.cs`（autoload）：下屏画面渲染在离屏 SubViewport 里，有变化时读回像素交给 Android；下屏触摸以信号形式传回；手柄映射。
- `android/Wa2ThorPlugin.java`（Godot v2 Android 插件）：在第二块屏幕上显示 `Presentation`，把帧画进 SurfaceView；BGM 音量条只重画那一小块；Kuromoji、SQLite 词典、TextToSpeech、AnkiDroid 接口都在这里。
- `export_plugin.gd`：导出时把 Java 源码拷进 gradle 构建模板，注册插件，加入 Kuromoji 依赖、AnkiDroid 权限与包可见性声明，并给 `build.gradle` 加上 META-INF 合并规则。
- 中文补丁重新映射了汉字字形槽位，所以界面文字用系统中文字体，只有游戏文本（台词、人名、选项）用游戏位图字体。

对上游代码的改动很小：`Wa2Func`/`Wa2EngineMain` 给文本记录条目记下来源（脚本名和字符串编号），`Wa2Label` 增加可选的居中注音排版（`CenterRuby`），`Wa2Resource` 拆出 `ReadEntry`，并修正了 Android 上返回音效的文件名大小写。

## 构建

需要 Godot 4.5.x .NET 版及 Android 导出模板、.NET 9 SDK、JDK 17、Android SDK。

1. 本仓库不含游戏素材（`assets/grp`、`assets/se`、`assets/fonts`）。需要从自己的合法副本准备，详见仓库根目录 `ASSET_POLICY.md`。
2. 生成词典（不随仓库分发，约 55 MB）：
   ```
   python addons/thor_dualscreen/tools/build_dict.py <源数据目录> addons/thor_dualscreen/dict/thor_dict.db
   ```
   源数据目录需要包含 `JMdict_e.gz`（EDRDG）和 kaikki.org 的中文维基词典日语词条 `zh-ja-1.jsonl`（「日语」）、`zh-ja-2.jsonl`（「日語」）。
3. 在编辑器里启用插件 `WHITE ALBUM 2 DS`（project.godot 已启用），安装 Android 构建模板，用导出预设 `wa2ds` 导出（应用名 WHITE ALBUM 2 DS，包名 `com.example.wa2.thor`，沿用以便覆盖安装）。

桌面预览：`godot --path . -- --thor-mock [--thor-jp] [--thor-tab=N] [--thor-words] --thor-shot=out.png` 会把下屏画到一个 PNG。

## 第三方数据与许可

- Kuromoji 0.9.0（Apache-2.0），含 IPADIC 词典（IPADIC 许可）。
- JMdict © Electronic Dictionary Research and Development Group，CC BY-SA 4.0。
- 中文维基词典词条（经 kaikki.org / wiktextract 提取），CC BY-SA。
- 以上词典数据只用于生成 `thor_dict.db`，不提交到仓库；分发生成的数据库时须保留署名并以相同许可共享。

## 已知限制

- 读取在本版本之前创建的存档时，存档里的文本记录没有来源信息，因此这些旧条目没有日文原文。
- 汉字注音来自通用词典，少数词可能读错（例如「白旗」）；主要角色名已单独标注，原作自带的注音不受影响。
- 只在 AYN Thor 上测试过。
