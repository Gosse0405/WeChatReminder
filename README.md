# WeChatReminder

一个基于 .NET 8 / WPF 的 Windows 微信托盘提醒工具。

这个项目通过定位系统托盘中的微信图标，周期性采样图标区域的亮度变化，判断图标是否处于“闪烁”状态；当检测到微信可能有未处理消息、且微信窗口当前不在前台时，程序会弹出提醒窗口，并支持一键尝试打开微信。

## 项目特点

- 托盘常驻运行，无主窗口打扰
- 自动查找微信托盘图标并持续监控
- 基于屏幕采样和亮度变化识别托盘闪烁
- 仅在微信不在前台时触发提醒，避免重复打扰
- 支持“立即查看 / 10 分钟后提醒 / 1 小时后提醒”
- 支持自定义“打开微信”快捷键，默认 `Ctrl+Alt+W`
- 支持托盘菜单测试提醒、切换详细日志、退出程序
- 打开微信失败时会尝试窗口激活和托盘图标回退路径

## 技术栈

- .NET 8
- WPF
- Windows Forms `NotifyIcon`
- Windows UI Automation
- Win32 API
- 屏幕区域采样与亮度分析

目标框架见项目文件：

- `net8.0-windows`

## 工作原理

程序的主流程大致如下：

1. `WeChatTrayLocator` 通过 UI Automation 在任务栏通知区域中查找微信托盘图标。
2. 找到图标后，提取一个较稳定的采样区域 `SampleRect`。
3. `ScreenCaptureHelper` 以固定周期截取该区域，并计算平均亮度。
4. `FlashPatternAnalyzer` 根据最近一段时间的亮度高低变化，判断图标是否处于闪烁状态。
5. `App.xaml.cs` 结合微信前台状态、提醒冷却时间、延后提醒状态等条件，决定是否展示提醒弹窗。
6. 用户点击“立即查看”后，程序会优先发送预设快捷键；若没有成功，则继续尝试激活微信窗口或点击微信托盘图标。

从实现上看，这不是对微信消息内容的读取，而是对“微信托盘图标是否闪烁”的桌面级检测。

## 运行环境

- Windows 10 / Windows 11
- 已安装微信桌面版，并且程序运行时微信处于登录状态
- 开发运行需要 .NET 8 SDK

建议：

- 尽量让微信托盘图标保持在可见通知区域，检测会更稳定
- 使用系统默认任务栏通知区域时，兼容性通常更好

## 快速开始

在项目目录执行：

```powershell
dotnet restore
dotnet build .\WeChatReminder.csproj
dotnet run --project .\WeChatReminder.csproj
```

构建产物默认会出现在：

```text
bin\Debug\net8.0-windows\
```

如需生成发布版本，可执行：

```powershell
dotnet publish .\WeChatReminder.csproj -c Release
```

上面这条命令可以生成发布目录，但默认不等于“单个 exe 文件可直接发人”。

更准确地说：

- 如果只是执行 `dotnet publish -c Release`，通常会得到一组发布文件
- 对方机器如果没有合适的 .NET 运行时，程序可能无法直接运行
- 这更适合开发者本机验证，不一定适合直接分发给普通用户

## 打包给别人直接使用

如果你希望把程序直接发给别人，并尽量减少对方环境依赖，建议发布为：

- 指定 Windows 运行时
- 自包含发布
- 单文件 exe

推荐命令：

```powershell
dotnet publish .\WeChatReminder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

这套参数的作用是：

- `--self-contained true`
  - 把 .NET 运行时一起打进去，对方一般不用额外安装 .NET
- `PublishSingleFile=true`
  - 尽量合并为单文件发布
- `IncludeNativeLibrariesForSelfExtract=true`
  - 把 WPF 相关原生库也并入单文件，避免输出一堆额外 dll
- `EnableCompressionInSingleFile=true`
  - 压缩单文件体积
- `DebugType=None` 和 `DebugSymbols=false`
  - 不输出调试符号文件，避免多生成一个 `.pdb`

发布完成后，输出目录通常类似：

```text
bin\Release\net8.0-windows\win-x64\publish\
```

对当前项目，这套命令已经验证可以在 `publish` 目录里只生成 `1` 个主 `.exe` 文件。

也就是说，通常可以直接把这个 exe 发给别人运行。

### 是否可以直接发给别人

可以，但要注意下面几点：

- 如果你使用的是上面的 `--self-contained true` + `PublishSingleFile=true`，对方一般不需要额外安装 .NET
- 如果对方也是 64 位 Windows，优先使用 `-r win-x64`
- 如果对方机器可能是 ARM Windows，需要改成对应运行时，例如 `win-arm64`
- 某些安全软件或 Windows SmartScreen 可能会对未签名 exe 做拦截提示，这属于桌面程序分发中的常见现象

### 推荐分发方式

建议把 `publish` 目录中的主 exe 单独测试一次，再发给别人。

如果你只想发一个文件，优先使用上面的“单文件 exe”方式。

### 是发单个 exe，还是整个 publish 目录

这取决于你用的是哪种发布方式：

- 如果你用的是普通发布：

```powershell
dotnet publish .\WeChatReminder.csproj -c Release
```

这时通常不能只发 `.exe`，因为程序还会依赖同目录下的 `.dll`、`.json` 等文件。

这种情况下，正确做法是：

- 发送整个 `publish` 目录
- 或者把整个 `publish` 目录压缩成 `.zip` 再发给别人

- 如果你用的是单文件发布：

```powershell
dotnet publish .\WeChatReminder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

这种情况下，通常可以直接发主 `.exe` 文件。

对这个项目来说，如果你就是想“发给别人直接双击运行”，推荐优先使用：

- 单文件发布
- 然后把主 exe 发给对方

如果你想更稳妥一些，也可以把这个 exe 单独压成一个 zip 再发，对方下载后解压运行即可。

### 图标说明

项目当前已经接入：

```text
Assets\logo.ico
```

并且同一个图标会同时用于：

- 生成后的 `exe` 文件图标
- 程序运行时的托盘图标
- 应用内窗口图标

实现方式如下：

- `WeChatReminder.csproj` 通过 `<ApplicationIcon>Assets\logo.ico</ApplicationIcon>` 设置 exe 图标
- `Assets\logo.ico` 同时作为项目资源打包，保证单文件发布后运行时也能读取
- `TrayIconService` 会优先加载内置的 `logo.ico` 作为托盘图标

如果你后续想更换图标，只需要替换 `Assets\logo.ico`，然后重新执行 `dotnet publish` 即可。

注意：

- Windows 程序图标建议使用 `.ico`
- `.ico` 中最好包含多种尺寸，例如 `16x16`、`32x32`、`48x48`、`256x256`

如果你后续还要长期分发给别人，建议继续补充：

- 安装包
- 应用图标和版本信息
- 数字签名
- 首次运行说明

## 使用说明

### 启动后会发生什么

- 程序启动后不会显示主窗口，而是驻留在系统托盘
- 启动后大约有 `10` 秒监控预热时间
- 预热完成后还有约 `5` 秒提醒抑制时间
- 因此首次启动后的前十几秒内，属于正常的初始化阶段

### 提醒触发条件

满足以下条件时，程序才会尝试弹出提醒：

- 已完成启动预热
- 成功找到微信托盘图标
- 检测到托盘图标正在闪烁
- 微信窗口当前不在前台
- 当前不处于提醒冷却、延后提醒或“立即查看”后的短暂抑制期

### 提醒弹窗操作

提醒弹窗提供三种操作：

- `立即查看`：尝试打开或激活微信
- `10 分钟后提醒`：当前闪烁会话延后 10 分钟再次提醒
- `1 小时后提醒`：当前闪烁会话延后 1 小时再次提醒

### 托盘菜单功能

当前托盘菜单支持：

- 测试提醒弹窗
- 设置打开微信快捷键
- 开关详细日志
- 退出程序

另外，双击托盘图标也会触发一次测试提醒弹窗，便于快速检查 UI 效果。

## 配置与数据文件

程序会将用户配置和日志写入：

```text
%LOCALAPPDATA%\WeChatReminder\
```

主要文件如下：

- `open_wechat_hotkey.txt`
  - 保存“立即查看”时用于尝试打开微信的快捷键
  - 默认值为 `Ctrl+Alt+W`
- `detailed_logging.txt`
  - 保存是否开启详细日志
  - 内容为 `True` 或 `False`
- `logs\app.log`
  - 当前日志文件

日志策略：

- 单个日志文件超过 `1 MB` 时自动归档
- 最多保留 `3` 份历史归档日志

## 项目结构

```text
WeChatReminder
├─ App.xaml / App.xaml.cs
├─ Models
├─ Native
├─ Services
├─ UI
└─ WeChatReminder.csproj
```

关键文件职责：

- `App.xaml.cs`
  - 应用入口
  - 负责启动、状态协调、提醒调度、打开微信流程
- `Services\WeChatFlashMonitorService.cs`
  - 托盘图标监控主服务
  - 定时采样并维护闪烁状态
- `Services\WeChatTrayLocator.cs`
  - 查找微信托盘图标
  - 提供托盘点击/激活能力
- `Services\ScreenCaptureHelper.cs`
  - 对托盘图标区域做屏幕采样并计算平均亮度
- `Services\FlashPatternAnalyzer.cs`
  - 根据亮度历史判断是否闪烁
- `Services\ReminderTimingCoordinator.cs`
  - 管理提醒防抖、闪烁结束确认、延后提醒
- `Services\OpenWeChatHotkeyService.cs`
  - 解析并发送用于打开微信的快捷键
- `Services\TrayIconService.cs`
  - 托盘图标、右键菜单、气泡通知
- `Services\AppLogger.cs`
  - 日志写入、重复日志折叠、归档轮转
- `UI\ReminderOverlayWindow.*`
  - 提醒弹窗界面与动画
- `UI\HotkeySettingsWindow.*`
  - 快捷键配置窗口
- `Native\NativeMethods.cs`
  - Win32 相关封装，如窗口激活、点击屏幕点位等

## 打开微信的策略

当用户点击“立即查看”时，程序会按以下顺序尝试：

1. 发送已配置的快捷键
2. 检查微信是否已进入前台
3. 尝试直接激活微信主窗口
4. 回退为激活或点击微信托盘图标
5. 再次尝试窗口激活

这套策略的目标是提高“从提醒直接回到微信”的成功率。

## 调试建议

如果你在开发或排查问题，建议优先使用下面几种方式：

- 在托盘菜单中开启“详细日志”
- 查看 `%LOCALAPPDATA%\WeChatReminder\logs\app.log`
- 使用“测试提醒弹窗”先验证 UI 与交互流程
- 如果“立即查看”没有成功，优先检查快捷键配置是否符合你的本机环境

## 已知限制与注意事项

- 该项目仅适用于 Windows 桌面环境
- 检测依赖微信托盘图标能够被定位并采样，不是通过读取微信消息内容实现
- 如果微信托盘图标被隐藏、被其他悬浮菜单遮挡，或桌面环境较特殊，识别准确性可能下降
- “立即查看”的成功率依赖当前快捷键配置、微信窗口状态以及托盘图标可访问性
- 项目当前未包含自动化测试工程，回归验证主要依赖手动运行与日志排查

## 适合继续完善的方向

如果后续要继续维护这个项目，可以优先考虑：

- 增加发布说明和安装包脚本
- 增加异常场景下的诊断开关和状态面板
- 为核心检测逻辑补充可单测的抽象层
- 为不同任务栏布局、缩放比例和多显示器场景增加兼容性验证

## 总结

这是一个典型的“Windows 桌面辅助工具”项目：整体结构不复杂，但实现细节很贴近真实桌面环境，重点在于托盘定位、屏幕采样、提醒节流以及打开微信的多级回退策略。

如果你的目标是继续迭代它，这份代码已经具备一个比较清晰的基础骨架，适合继续往“稳定性”“兼容性”和“可维护性”三个方向推进。
