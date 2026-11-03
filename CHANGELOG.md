# 更新日志 / Changelog

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
This project adheres to [Semantic Versioning](https://semver.org/).

---

## [1.1.1] - 2026-11-03

修复**编码一直卡在「编码中… 99%」**（引擎实际已出错或已退出，界面却无限等待）。
Fixes encoding appearing **stuck at "Encoding… 99%"** while the engine had actually failed or exited.

### 🐛 修复 / Fixed
- **99% 是估算值**：引擎未上报进度且无输出文件时，旧版按"已用时间 ÷ 音频时长"估算并封顶 99%，超过音频时长后永远停在 99%。
  现改为不定进度条 +「等待引擎进度」，只显示引擎真实进度或按输出文件大小的估算（标注"约"）。
  *The 99% was a time-based estimate capped at 99%; it is gone — an indeterminate bar is shown until the engine reports progress.*
- **引擎结束/出错消息被覆盖**：引擎发来的完成/错误（'X' 帧）只写日志，进度轮询又把状态改回"编码中"。现以引擎结果为准立即结束并显示错误原因。
  *The engine's finished/error frame is now honoured instead of being overwritten by the progress poller.*
- **中文路径导致引擎打不开文件**：命令与 .cfg 改用系统 ANSI 代码页（与官方 GUI 一致，原为 UTF-8）；含中文的路径优先转换为 8.3 短路径；
  .cfg 不再因中文用户名的 %TEMP% 而无法读取；仍无法表示时弹窗说明具体路径，而不是静默失败。StreamTools 同样处理。
  *Commands and .cfg are sent in the system ANSI code page; non-ASCII paths are converted to 8.3 short paths; unrepresentable paths are reported.*
- **任务句柄不一致**：引擎自行分配任务句柄，旧版假设与本地编号相同；现按引擎返回的句柄绑定任务，进度/终止/队列操作都指向正确任务。
  *Engine job handles are now mapped to local jobs, so progress and cancel target the right job.*
- **引擎崩溃/退出无感知**：socket 断开或 DtsJobQueue 进程消失时立即标记失败；长时间无任何动静（5 分钟提示、45 分钟判定失败）。
  *Engine disconnect/exit is detected; long stalls are reported.*
- 输出路径统一转为绝对路径并自动创建输出目录；上次遗留的日志改为在提交任务**之前**删除（旧版提交后才删，可能删掉引擎刚写的错误日志）。
  *Absolute output path, output folder auto-created, stale log removed before (not after) submitting.*
- 日志记录实际发送给引擎的命令，便于排查。
  *The exact engine command is logged.*

---

## [1.1.0] - 2026-11-03

新增**音轨自动识别导入**与**鼠标悬浮提示**，并接入 GitHub Actions 自动编译 / 发布。
Adds **automatic track recognition on import**, **hover tooltips**, and GitHub Actions build / release automation.

### ✨ 新增 / Added
- **音轨自动识别导入**：分轨模式下随意导入一条音轨（浏览或拖放），自动识别它属于哪个声道并放入对应输入框，
  同时扫描同目录同组的其它声道一并导入。例如点任意一行的「浏览…」选择 `Movie.FL.wav`，会放入 **L**，
  并自动导入 `Movie.FR / FC / LFE / SL / SR / BL / BR`。
  *Import any single track (browse or drag & drop): its channel is recognized, it lands in the right row, and the rest of its group in the folder is imported automatically.*
- **多种声道命名识别**：标准写法（`L/R/C/LFE/Ls/Rs/Lss/Rss/Lsr/Rsr/Lrs/Rrs/Lb/Rb/Cs/S/Lw/Rw/Lh/Rh/Lhs/Rhs/Oh/Ch/Lt/Rt`）、
  ffmpeg/SMPTE 写法（`FL/FR/FC/LFE/SL/SR/BL/BR/BC/TFL/TFR/TC`）、英文全称（`Left`、`Sub`、`Left Surround`、`Rear Right`、`Front_Left`…）、
  中文（`左/右/中置/重低音/左环绕/右后`…）；分隔符 `_ - . 空格 () []` 均可，声道标记后可带后缀（`Movie_L_48k_24bit.wav`），
  也支持无分隔符的 `MovieFL.wav`。
  *Recognizes standard, ffmpeg/SMPTE, English-word and Chinese channel names, any common separator, trailing suffixes and separator-less names.*
- **声道布局自动识别**：按识别到的声道集合自动切换到最匹配的声道布局（如 FL/FR/FC/LFE/SL/SR/BL/BR → 7.1 Lss/Rss/Lsr/Rsr，
  L/R → 2.0）；单文件输入按 WAV `dwChannelMask` / 声道数自动选择布局。编码页新增「自动识别声道布局」开关（默认开启，持久化）。
  *The channel layout follows the recognized channels (or the WAV channel mask for single-file input); toggle on the Encode page.*
- **智能输入模式切换**：分轨模式拖入多声道 WAV 自动切到单文件输入；单文件模式拖入分轨声道文件自动切到分轨输入。
  *Dropping a multichannel WAV in split mode switches to single-file input, and vice versa.*
- **鼠标悬浮提示**：编码页 / StreamTools / 设置页的按钮、输入框、下拉框与选项均提供中英双语说明；
  声道标签悬浮显示该声道支持的全部文件名写法；StreamTools 各标签页悬浮显示功能说明。设置页新增「鼠标悬浮提示」开关（实时生效）。
  *Bilingual hover tooltips across all pages; channel labels list the names they recognize; toggle in Settings.*
- **GitHub Actions 自动编译**：每次推送自动编译 win-x64 单文件自包含 exe 并上传构建产物；合并到 `main` 或推送 `v*` 标签时
  按版本号自动创建 Release（发布说明取自本更新日志）。
  *CI builds the win-x64 single-file exe on every push and publishes a Release for new versions on `main` / `v*` tags.*

### 🔧 优化 / Improved
- 导入日志显示每个声道的识别结果，以及仍未导入的声道。
  *The log lists each recognized channel and any channels still missing.*
- 修复旧版无分隔符匹配会把 `Final.wav` 之类文件误判为 L 声道的问题（现要求大小写边界）。
  *Separator-less matching no longer mistakes names like `Final.wav` for the L channel.*
- Linux 下无 GLX 时回退软件渲染，避免启动崩溃。
  *Falls back to software rendering on Linux when GLX is unavailable.*
- README 补充从源码构建所需的 Avalonia-Fluent-UI 源码步骤。
  *README now documents fetching the Avalonia-Fluent-UI source for local builds.*

---

## [1.0.2] - 2026-07-15

新增**实时中英双语切换**，并补齐 StreamTools 元数据面板功能。
Adds **real-time Simplified Chinese / English switching** and completes the StreamTools metadata panels.

### ✨ 新增 / Added
- **实时语言切换（简体中文 / 英文）**：设置页新增「语言」下拉，**免重启即时生效**；语言持久化，默认跟随系统。
  界面文案（导航、页面标题、编码页与 StreamTools 的全部字段/按钮/标签、设置页、引擎状态）全部本地化。
  *Real-time language switch (zh-Hans / en) from Settings — no restart; the entire UI is localized.*
- **StreamTools · 重打时间码 / 元数据**：选择文件后自动读取该码流当前的起始时间码 / 帧率 / 对白归一化并预填，
  与官方 DTSTools 面板行为一致。
  *Restripe / Metadata now auto-read the stream's current TC / frame rate / dialnorm on file select, matching the official panels.*
- **启动时自动检查更新**（可在设置页开关，默认开启）：仅在发现新版本时弹一次提示，静默、非阻塞。
  *Auto check-for-updates at startup (toggle in Settings, on by default) — prompts only when a newer version exists.*

### 🔧 优化 / Improved
- 关于页版本号统一取自程序集；文档（README / CHANGELOG）更新。
  *About version derives from the assembly; docs updated.*

---

## [1.0.1] - 2026-07-15

聚焦**跨机器兼容**、**分辨率/DPI 自适应**与**编码稳定性**，并新增 GitHub 在线更新。
Focus on **cross-machine compatibility**, **resolution/DPI adaptivity**, **encoding stability**, plus a new GitHub update check.

### ✨ 新增 / Added
- **GitHub 在线更新**：「关于」页新增「检查更新」，可查询最新发布版本并一键「前往下载」。
  *Online update check on the About page — query the latest release and jump to the download page.*
- **关于页项目主页链接**，版本号统一取自程序集（单一来源）。
  *Project homepage link on About; version now derives from the assembly (single source of truth).*

### 🐛 修复 / Fixed
- **他机运行图标空白**：导航图标改为**内嵌矢量路径**，不再依赖系统字体（Segoe Fluent Icons / MDL2），
  在精简版 Win10 / Server 等缺字体环境下也能正常显示。
  *Blank navigation icons on other machines — icons are now embedded vectors, no longer depending on system fonts.*
- **单文件多声道声道重排**：修复整帧读取不完整时可能导致声道错位、或将合法文件误判为「数据不完整」的问题。
  *Fixed a partial-read bug in single-file channel remapping that could misalign channels or wrongly report "incomplete data".*
- **1080p / 缩放屏窗口过大**：修复物理像素与逻辑像素混用导致的窗口过大甚至超出屏幕；现按显示器 DPI 正确自适应并居中。
  *Fixed oversized/off-screen window on 1080p and scaled displays (physical vs logical pixel mismatch); now DPI-adaptive and centered.*
- **Win10 云母背景**：统一仅在 Windows 11 启用 Mica，避免子窗口在 Win10 上错误尝试云母背景。
  *Mica is now gated to Windows 11 across all windows.*
- **可移植性**：移除硬编码的开发机工具目录绝对路径，工具集探测改为纯相对/可移植路径。
  *Removed a hardcoded developer tool-directory path; tool discovery is now fully portable.*

### 🔧 优化 / Improved
- 刷新率自适应说明与合成管线保持（跟随显示器 vsync，不锁 60fps）。
  *Refresh-rate adaptive rendering (follows display vsync).*
- 清理无用 `using` 引用；解决仓库合并冲突并统一 LICENSE / README / 关于页署名。
  *Cleaned unused usings; resolved merge conflicts and unified authorship.*

---

## [1.0.0] - 2026-07-13

- 首个公开版本：DTS-HD Master Audio 编码 GUI（音频编码 / 比特流元数据 / 下混 / 队列 / StreamTools）。
  *Initial public release: DTS-HD Master Audio encoding GUI.*

[1.1.1]: https://github.com/cgyihai/DTSHD.Encoder.Avalonia/releases/tag/v1.1.1
[1.1.0]: https://github.com/cgyihai/DTSHD.Encoder.Avalonia/releases/tag/v1.1.0
[1.0.2]: https://github.com/cgyihai/DTSHD.Encoder.Avalonia/releases
[1.0.1]: https://github.com/cgyihai/DTSHD.Encoder.Avalonia/releases
[1.0.0]: https://github.com/cgyihai/DTSHD.Encoder.Avalonia/releases
