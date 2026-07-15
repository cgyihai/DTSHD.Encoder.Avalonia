# DTS-HD.Encoder.Avalonia

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.0.3-8b44ac.svg)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011+-0078D4.svg)](https://www.microsoft.com/windows)

A modern GUI encoder front-end for **DTS-HD Master Audio Suite**, built on Avalonia 12 with GPU-accelerated WinUI Composition rendering pipeline.

> 🌐 [中文说明](#中文说明) · 🌍 [English](#english)

---

## 中文说明

### 项目简介

`DTS-HD.Encoder.Avalonia` 是一个基于 **Avalonia 12 + WinUIComposition GPU 合成管线** 的 DTS-HD Master Audio 编码 GUI 工具，提供现代化的 Fluent 设计界面与高刷新率动画体验。

### 特性

- 🎬 **120–144Hz 高刷新率动画** — 跟随显示器 vsync，不锁 60fps
- 🎨 **Mica 云母背景** — Windows 11+ GPU 硬件合成
- ⚡ **GPU 渲染** — ANGLE EGL (DirectX 11) + WinUI Composition
- 🧩 **MVVM 架构** — CommunityToolkit.Mvvm + 编译时绑定
- 🎵 **双模式编码** — 单文件 WAV / 分轨 WAV
- 📊 **实时进度** — 引擎帧进度 / 文件大小估算 / 时间估算三级回退
- 🎛 **混音预设** — 内置下混配置（5.1→2.0 等）
- 📝 **队列管理** — 多任务编码队列
- 🔧 **流工具** — 集成 DTS 流分析 / 信息转储

### 技术栈

| 组件 | 版本 |
|---|---|
| .NET | 10.0 |
| Avalonia | 12.0.3 |
| CommunityToolkit.Mvvm | 8.4.0 |
| Avalonia-Fluent-UI | 2.0.0 |

### ⚠️ 法律声明（重要）

本仓库 **不包含** 也 **不会分发** 任何 DTS, Inc. 的专有工具链，包括但不限于：

- `dtshd.exe` / `dtshdst.exe` / `DTSHDVerify.exe`
- `DTSToolFramewrk.exe` / `DtsJobQueue.exe`
- `MAS-SAS_Authorizer.exe` / `InfoDumper.exe`
- `AAFCOAPI.dll` / `aafParse.dll` / `DTSEncConfig.dll` / `DTSWin32.dll`

**用户需自行获取 DTS-HD Master Audio Suite 工具链**（合法授权），并放置于主程序同级的 `DTS-HD_Tool/` 目录下。本仓库仅提供 GUI 封装代码，不包含任何 DTS 专有算法或二进制文件。

`DTS`、`DTS-HD`、`DTS-HD Master Audio`、`MAS-SAS` 等均为 **DTS, Inc.** 的注册商标，本项目与 DTS, Inc. 无任何关联。

### 构建步骤

1. 安装 **.NET 10 SDK**
2. 克隆仓库
   ```bash
   git clone https://github.com/cgyihai/DTSHD.Encoder.Avalonia.git
   cd DTSHD.Encoder.Avalonia
   ```
3. 还原依赖并编译
   ```bash
   dotnet restore
   dotnet build -c Release
   ```
4. （可选）将 DTS 工具链放置于输出目录的 `DTS-HD_Tool/` 下
5. 运行
   ```bash
   dotnet run --project DTS-HD.Encoder.Avalonia -c Release
   ```

### 致谢

- [Avalonia](https://avaloniaui.net/) — MIT License
- [Avalonia-Fluent-UI](https://github.com/HiyorinI/AvaloniaFluentUI) — MIT License
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/Mvvm) — MIT License

### 许可证

本项目基于 [MIT License](LICENSE) 开源。

---

## English

### Overview

`DTS-HD.Encoder.Avalonia` is a modern GUI encoder front-end for **DTS-HD Master Audio Suite**, built on Avalonia 12 with GPU-accelerated WinUI Composition rendering pipeline, providing a Fluent design interface and high-refresh-rate animation experience.

### Features

- 🎬 **120–144Hz high refresh-rate animations** — Follows display vsync, no 60fps lock
- 🎨 **Mica backdrop** — Windows 11+ GPU hardware composition
- ⚡ **GPU rendering** — ANGLE EGL (DirectX 11) + WinUI Composition
- 🧩 **MVVM architecture** — CommunityToolkit.Mvvm + compiled bindings
- 🎵 **Dual-mode encoding** — Single-file WAV / multi-track WAV
- 📊 **Real-time progress** — Engine frame % / file-size estimation / time estimation three-tier fallback
- 🎛 **Downmix presets** — Built-in downmix configurations (5.1→2.0, etc.)
- 📝 **Queue management** — Multi-task encoding queue
- 🔧 **Stream tools** — Integrated DTS stream analysis / info dump

### Tech Stack

| Component | Version |
|---|---|
| .NET | 10.0 |
| Avalonia | 12.0.3 |
| CommunityToolkit.Mvvm | 8.4.0 |
| Avalonia-Fluent-UI | 2.0.0 |

### ⚠️ Legal Notice (Important)

This repository **does not contain** and **will not distribute** any proprietary DTS, Inc. toolchain, including but not limited to:

- `dtshd.exe` / `dtshdst.exe` / `DTSHDVerify.exe`
- `DTSToolFramewrk.exe` / `DtsJobQueue.exe`
- `MAS-SAS_Authorizer.exe` / `InfoDumper.exe`
- `AAFCOAPI.dll` / `aafParse.dll` / `DTSEncConfig.dll` / `DTSWin32.dll`

**Users must obtain the DTS-HD Master Audio Suite toolchain** (with proper license) separately and place it in the `DTS-HD_Tool/` directory next to the main executable. This repository only provides GUI wrapper code and contains no DTS proprietary algorithms or binaries.

`DTS`, `DTS-HD`, `DTS-HD Master Audio`, `MAS-SAS` are registered trademarks of **DTS, Inc.** This project is not affiliated with DTS, Inc. in any way.

### Build

1. Install **.NET 10 SDK**
2. Clone the repository
   ```bash
   git clone https://github.com/cgyihai/DTSHD.Encoder.Avalonia.git
   cd DTSHD.Encoder.Avalonia
   ```
3. Restore dependencies and build
   ```bash
   dotnet restore
   dotnet build -c Release
   ```
4. (Optional) Place the DTS toolchain under `DTS-HD_Tool/` in the output directory
5. Run
   ```bash
   dotnet run --project DTS-HD.Encoder.Avalonia -c Release
   ```

### Acknowledgements

- [Avalonia](https://avaloniaui.net/) — MIT License
- [Avalonia-Fluent-UI](https://github.com/HiyorinI/AvaloniaFluentUI) — MIT License
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/Mvvm) — MIT License

### License

This project is licensed under the [MIT License](LICENSE).
