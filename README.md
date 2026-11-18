# KeyTool

KeyTool 是一款面向 macOS 和 Windows 的按键助手，用于管理、录制和后台执行键鼠任务。

## 功能

- 任务管理页：查看任务状态、执行轮次、计划、快捷键和动作数量
- 动作编辑：键盘组合（支持重复次数）、鼠标点击、鼠标移动、滚动、等待
- 操作录制：全局捕获键盘、鼠标点击、滚动和节流后的移动，并自动记录操作间等待时间
- 执行计划：手动、固定间隔、每日定时
- 循环控制：手动输入循环次数、无限循环、实时执行时间和随时停止
- 全局快捷键：为每个任务设置独立启动与停止快捷键
- 后台运行：macOS 菜单栏、Windows 系统托盘
- 本地持久化：任务数据保存为 JSON，不依赖网络
- 指定目标：向运行中的目标应用或窗口发送输入，不主动切换前台

## macOS

系统要求：macOS 14 或更高版本。

已构建应用：

```text
dist/mac/KeyTool.app
```

本地构建：

```sh
chmod +x mac/scripts/build-app.sh mac/scripts/check-core.sh
./mac/scripts/build-app.sh
open dist/mac/KeyTool.app
```

核心检查：

```sh
./mac/scripts/check-core.sh
```

首次运行后，在“系统设置 → 隐私与安全性 → 辅助功能”中允许 KeyTool。操作录制使用 CoreGraphics Event Tap，同样依赖该权限。

发布脚本会生成同时支持 Apple Silicon 和 Intel 的通用应用。本地构建使用绑定 bundle 标识的临时签名，正式分发时应替换为 Developer ID 证书签名。

macOS 任务数据：

```text
~/Library/Application Support/KeyTool/tasks.json
```

## Windows

系统要求：Windows 10/11 x64，安装 .NET 8 SDK 用于源码构建。发布包为自包含单文件，运行不需要预装 .NET。

已构建程序：

```text
dist/windows/win-x64/KeyTool.exe
dist/windows/KeyTool-Windows-x64.zip
```

在 Windows PowerShell 中构建：

```powershell
.\windows\build-windows.ps1
```

也可以在 macOS/Linux 上交叉发布：

```sh
chmod +x scripts/build-windows.sh
./scripts/build-windows.sh
```

如果 `dotnet` 不在 `PATH` 中，可指定 SDK：

```sh
KEYTOOL_DOTNET=/path/to/dotnet ./scripts/build-windows.sh
```

Windows 版提供三种输入方式：“自适应”在目标前台时使用真实前台输入，后台任务首次发送时建立一次受控激活并保持到任务停止或用户真正切回目标窗口；“前台兼容”始终短暂激活指定目标后通过系统输入发送按键和鼠标点击，完成后恢复原窗口；“纯后台消息”只投递 Win32 窗口消息，不激活目标，但部分 Unity 游戏可能忽略按键。鼠标移动、滚轮和操作录制使用 Win32 窗口消息与低级输入钩子。关闭主窗口后程序继续留在系统托盘运行；托盘菜单可以重新打开窗口、停止全部任务或退出程序。

当前仓库已在 macOS 上完成 Windows x64 交叉编译和 XAML 编译校验；Win32 输入兼容性仍应根据实际目标程序在 Windows 环境进行验收。

Windows 任务数据：

```text
%LOCALAPPDATA%\KeyTool\tasks.json
```

## 操作录制

1. 打开任务编辑页，点击“录制操作”。
2. 切换到目标窗口，正常执行键盘、鼠标点击、滚动和移动。
3. 切回 KeyTool 后点击“停止并保存”。

录制内容会追加到当前动作序列。相邻操作的真实等待时间会自动转换为“等待”动作；鼠标移动按 200ms 节流，避免生成过大的动作列表。

## 后台输入限制

两个平台都只向“目标窗口/目标进程”发送输入，不会把按键发给其他窗口：

- macOS 使用 CoreGraphics 进程级事件，目标实际是运行中的应用。
- Windows 的前台兼容模式会短暂激活目标窗口，结束后恢复原窗口。
- Windows 的自适应模式根据目标是否在前台动态选择输入路径。
- Windows 的纯后台消息模式只使用 `PostMessage`，兼容性最低。

自适应模式不会在每次后台动作前后反复切换激活状态：同一任务对同一目标只建立一次后台激活租约，并在用户真实切回目标窗口或停止任务时结束。若仍出现输入异常，请使用前台兼容模式。前台兼容模式兼容性最高，但每次按键会有一个很短的前台焦点切换。若 Windows 目标是管理员权限程序，KeyTool 通常也需要以管理员身份运行。

## 项目结构

```text
mac/Package.swift               macOS Swift Package 配置
mac/Sources/KeyTool/            macOS SwiftUI 应用源码
mac/Support/                    macOS 图标资源和 Info.plist
mac/scripts/                    macOS 构建、检查与图标生成脚本
windows/KeyTool.Windows/        Windows WPF 应用
scripts/                        Windows 跨平台构建脚本
dist/mac/                      macOS 通用应用包
dist/windows/                  Windows x64 程序与压缩包
```
