# 更新日志 / Changelog

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
This project adheres to [Semantic Versioning](https://semver.org/).

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

[1.0.1]: https://github.com/cgyihai/DTSHD.Encoder.Avalonia/releases
[1.0.0]: https://github.com/cgyihai/DTSHD.Encoder.Avalonia/releases
