# WinOptimizer

模块化 **Windows 10 / 11 一键系统优化、电竞低延迟调优与开发环境部署工具**。

参考 GitHub 主流开源优化项目（`hellzerg/optimizer`、`ChrisTitusTech/winutil`、`SophiApp`、`AtlasOS`）架构设计，基于 **.NET 8 + WPF** 原生深色 Fluent 界面构建。所有优化项均支持**实时状态检测、单项开关切换、批量一键执行、已优化/未优化筛选与无损一键恢复默认**。

---

## 下载与使用

前往右侧 **[Releases](../../releases)** 页面直接下载最新编译好的单文件可执行程序（内置 UAC 管理员提权，双击即用）：

- **`WinOptimizer-v1.0.0-win-x64-standalone.exe`（推荐）**：完整免环境独立单文件版，自带 .NET 8 运行时，新装纯净版 Windows 亦可直接运行。
- **`WinOptimizer-v1.0.0-win-x64-lite.exe`**：极速轻量版（< 400 KB），适用于已安装 .NET 8 Desktop Runtime 的电脑。

---

## 已内置功能一览

### 一、游戏帧率与低延迟调优（14 项）
1. **网游发包零延迟（关闭 Nagle 攒包算法）**：为活动网卡写入 `TcpAckFrequency=1`、`TCPNoDelay=1`、`TcpDelAckTicks=0`，消除小包等待延迟。
2. **网游防拉回（关闭 TCP RSC & 300ms 快速重传）**：关闭接收段合并并将初始重传超时由 `1000ms` 提速至 `300ms`。
3. **关闭连按 5 次 `Shift` 粘滞键弹窗 & 键鼠输入队列优化**：Win32 实时生效，适配高回报率键鼠（1K/4K/8K Hz）。
4. **开启 Win11 全局高精度时钟 & 禁用动态时钟节拍**：`GlobalTimerResolutionRequests=1` + `disabledynamictick yes`，并自动清除低效强制 HPET。
5. **混合大小核专属（强制游戏线程优先跑满 P-Core 大核）**：通过原生 `powrprof.dll` 设置内核合法异构调度策略（`Policy=2`），无需关小核、零反作弊风险。
6. **前台 3 倍时间片倾斜 (`0x26`) &《永劫无间》专属高优先级**：`Win32PrioritySeparation=0x26` + `NarakaBladepoint.exe` 官方 `PerfOptions` 高 CPU/IO 优先级。
7. **NVIDIA 着色器缓存扩容至「无限制」**：通过官方 `nvapi64.dll` DRS 接口直接写入全局 3D 配置（`0x00AC8497 = 0xFFFFFFFF`），消除初见特效编译卡顿。
8. **彻底关闭鼠标加速（1:1 原始输入）**：清零指针曲线并调用 `SPI_SETMOUSE` 免重启实时生效。
9. **关闭内核隔离「内存完整性」(HVCI / VBS)**：消除虚拟化二级地址转换损耗，提升平均帧与 1% Low 帧。
10. **关闭 Game Bar & 禁用 Windows 游戏录制和广播（保留游戏模式）**：关闭 Xbox Game Bar 覆盖层、手柄唤醒与启动提示，禁用组策略「Windows 游戏录制和广播」及后台广播服务 (`BcastDVRUserService`)，同时保留系统 `Game Mode` 调度。
11. **MMCSS 游戏调度优先级拉满 & 解除网络节流**：`NetworkThrottlingIndex=FFFFFFFF`、`SystemResponsiveness=0` 与 `Tasks\Games` 高权重。
12. **硬件加速 GPU 计划 (HAGS) 开关**：支持 DLSS 3/4 帧生成必开切换或双屏掉帧排障。
13. **关闭多平面叠加 (MPO)**：写入 `OverlayTestMode=5`，修复高刷/双屏切屏黑屏与视频花屏。
14. **开启「窗口化游戏优化」(DXGI Flip 低延迟)**：升级旧版 DX10/DX11 无边框游戏为现代独立翻转模型。

### 二、性能、电源与存储（5 项）
15. **电源「卓越性能」满血调优版（GameX v2 全套 92 项参数）**：通过原生 `powrprof.dll` 毫秒级注入 CPU 核心停放全解锁、CPPC/SpeedShift 极速睿频、NVMe 0ms 转换延迟、禁用 PCIe ASPM 与 USB 选择性暂停等 92 项策略。
16. **锁死内核常驻物理内存 (`DisablePagingExecutive=1`) & 内置秒清备用内存 (ISLC)**。
17. **合并系统服务宿主进程 (`SvcHostSplitThresholdInKB`)**：减少大内存环境下拆分出上百个零碎 `svchost.exe`。
18. **关闭系统休眠 (`powercfg /h off`)**：立即清理 `hiberfil.sys`，腾出 10GB~30GB C 盘空间。
19. **SSD 读写加速**：禁用 NTFS 最后访问时间戳回写与 8.3 旧式短文件名开销。

### 三、安全与权限（3 项）
20. **关闭「打开文件 - 安全警告」弹窗**：写入附件管理器组策略并自动解除 `Downloads` 目录现有文件锁定标记。
21. **彻底关闭 UAC 权限确认弹窗 & SmartScreen 筛选器**。
22. **关闭 Windows Defender 防火墙**：关闭域/专用/公用防火墙配置，保留底层 `mpssvc` 服务存活以免破坏微软商店。

### 四、桌面与系统更新（7 项）
23. **资源管理器与任务栏清爽习惯合集**：默认进「此电脑」、显示后缀名与隐藏文件、隐藏任务栏搜索框/任务视图/Copilot 与桌面「了解此图片」图标。
24. **去除图标「蓝黄小盾牌」与「 - 快捷方式」字样**：自动生成原生 32 位透明 `Blank.ico` 并刷新图标缓存。
25. **切换右键菜单为 Win10 经典样式**：跳过 Win11 折叠菜单。
26. **精简开始菜单**：隐藏下方「全部」应用列表与推荐区域。
27. **彻底卸载并禁用 Win11 小组件（Widgets）**：离线秒卸 Appx 包并写入防自动重装策略。
28. **精简后台诊断遥测、广告推送与开始菜单联网 Bing 搜索**。
29. **暂停 Windows 自动更新 100 年**：写入 `FlightSettingsMaxPauseDays=36524` 并自动生成 100 年后到期时间戳，无需手动翻下拉框。

### 五、开发环境与工具一键部署（12 项）
基于微软官方 `winget` 静默部署并自动刷新 `PATH`，支持版本实时检测、批量安装、检查更新与卸载：
- **语言与运行库**：Python 3.12、Node.js LTS、Go、Rust (`rustup`)、Java JDK 21 LTS、.NET 8 SDK、Visual C++ 2015–2022 运行库
- **常用工具**：Git for Windows、`uv`、Visual Studio Code、7-Zip、Windhawk

### 六、右键菜单自定义管理（Context Menu Manager）
- 自动扫描 **桌面空白处、文件 (*)、文件夹、驱动器** 全部 `shell` 命令与 `shellex\ContextMenuHandlers` 扩展项，支持中文资源名解析（如 PeaZip、Git GUI/Bash、NVIDIA App/控制面板、便捷设置等）。
- **无损隐藏 / 恢复**：通过 Windows 原生 `LegacyDisable` 与 `ShellEx` 屏蔽机制一键开关，不删除原注册表结构，绝大部分项目下一次右键即刻生效；第三方残留项同时提供带二次确认的彻底删除功能。

### 七、侧边栏内置快捷工具
- **秒清备用内存 (ISLC)**：调用原生 `NtSetSystemInformation` 秒清 Windows Standby List 缓存内存。
- **清理旧着色器缓存**：安全清理 `DXCache` / `GLCache` / `D3DSCache` 过期文件。
- **一键重启资源管理器**。

---

## 二次开发与扩展指南

- **新增系统优化项**：在 `Tweaks/` 下新建类实现 [`ITweakModule`](Core/ITweakModule.cs) 接口，并在 [`TweakRegistry.cs`](Tweaks/TweakRegistry.cs) 追加一行 `new YourNewTweak()` 即可自动生成界面卡片与批量逻辑。
- **新增装机环境/软件**：在 [`EnvRegistry.cs`](Tweaks/EnvRegistry.cs) 列表中追加一个 `EnvPackageDefinition` 即可。

### 本地编译打包

```powershell
# 完整免环境独立单文件版
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o .\publish

# 轻量单文件版（需 .NET 8 Desktop Runtime）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o .\publish-lite
```

## License

[MIT](LICENSE)
