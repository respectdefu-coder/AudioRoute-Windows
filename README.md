# AudioRoute — Windows 音频输出自动切换

本地运行的 Windows 桌面工具，可按应用或窗口标题自动切换系统默认播放设备，支持手动切换、当前输出音量、静音、托盘控制和可选登录自启。附完整源码、已编译 EXE、构建脚本和测试。

GitHub 仓库中的源码可直接构建；已编译的完整运行包位于 [`downloads/AudioRoute-Windows.zip`](downloads/AudioRoute-Windows.zip)。下载后解压即可运行。

新版加入“扬声器＋双向切换箭头”图标，覆盖 EXE、主窗口、规则编辑窗口和托盘。`assets/AudioRoute.svg` 为矢量源文件，`AudioRoute.ico` 包含 16–256px 共 9 个尺寸；可使用 `tools/generate-icon.ps1` 重新生成 ICO 与 PNG 预览，无需联网。

v1.0.2 修复“进程名”模式的“浏览”按钮：现在可以选择 EXE，自动填写文件名；“完整路径”模式填写完整路径。窗口标题和域名规则隐藏不适用的文件浏览按钮，取消选择会保留原有匹配内容。

## 直接运行

1. 解压整个压缩包，进入 `AudioRoute\bin`，双击 `AudioRoute.exe`。保留旁边的 `AudioRoute.exe.config`。
2. 在“无规则命中时回退到”中选择扬声器，点击“保存回退设备”。
3. 点击“添加规则”：例如进程名 `vlc.exe` → 耳机，优先级 `100`。无需输入示例中不存在的设备 ID，直接在下拉列表中选择本机设备。
4. 启动 VLC，稳定命中后自动切到耳机；退出 VLC 后自动回退。若 VLC 在后台仍运行，进程规则会继续命中，可勾选“仅当前台”改变这一行为。
5. 关闭主窗口会隐藏到托盘；右键托盘图标选择“退出”才会结束运行。

再次双击 `AudioRoute.exe` 或桌面快捷方式，会直接显示并置前已运行的主窗口；隐藏到托盘或最小化后也可以这样打开，不会再弹出“已在运行”的提示，也不会启动第二套后台监听。升级时请先从托盘退出旧版本，再运行新版本。

程序需要 Windows 10/11 和 .NET Framework 4.8 或更高的 4.x 版本，不需要管理员权限，不需要 .NET 6/8 SDK，不需要安装 NuGet 包。较早的 Windows 10 若缺少 .NET Framework 4.8，需要先安装它。交付 EXE 为 AnyCPU，在本机以 x64 运行；尚未在 Windows 10、x86 或 ARM64 上实机验证。

首次启动没有规则、没有回退设备，不会主动改变系统输出。软件不会替你启用开机启动。

## 规则与优先级

| 触发类型 | 示例 | 语义 |
|---|---|---|
| 进程名 | `vlc` / `vlc.exe` | 不区分大小写，完整进程名匹配，不支持通配符 |
| 可执行文件完整路径 | `C:\Program Files\VideoLAN\VLC\vlc.exe` | 完整路径匹配；可通过“浏览”选择 |
| 任意窗口标题关键字 | `会议` | 可见顶层窗口标题包含关键字 |
| 浏览器标题关键字 | `YouTube` / `哔哩哔哩` | 仅在支持的浏览器进程中匹配标题 |
| 浏览器标题中的域名 | `example.com` | 标题实际包含域名或其子域名时匹配 |

**网站检测采用标题启发式，无法确认真实访问 URL。** 大多数网页标题不包含域名，建议用“浏览器标题关键字”。标题中出现域名也不能证明当前页面来自该域名。程序不会读取地址栏、浏览历史、网络请求或后台标签页；若后台标签页持续播放而当前标签页已变化，不会继续识别原页面。需要严格域名与后台标签页检测时，应另行开发浏览器扩展。本交付没有伪装这一能力。

支持浏览器进程：Chrome、Edge、Firefox、Brave、Opera、Vivaldi、Internet Explorer、Arc 的常见进程名。其他浏览器可用“任意窗口标题关键字”或进程规则。

- 优先级范围 0–9999，数字越大越优先；相同优先级按列表中的添加顺序。通过编辑优先级改变顺序。
- 规则可添加、编辑、删除、启用、禁用，并可限定“仅当前台”。
- 目标设备离线时跳过该规则，继续尝试低优先级的在线规则；设备重新连接后自动参与匹配。
- 进程规则表示“进程存在”，不是“正在播放音频”。不勾选前台限定时，最小化/后台进程仍然命中。
- 浏览器规则对应每个浏览器窗口当前可见的标签标题；不勾选前台限定时，非前台窗口也可以命中。
- 点击“查看窗口标题”可以检查实际可匹配的文字。标题只在内存中显示，不会写入运行日志。

## 手动切换与回退

选中左侧设备后点击“切换到此设备”，或双击设备；托盘菜单也可直接选择。

程序使用以下顺序决定输出：

1. 自动切换暂停时，保持当前输出，手动控制仍有效。
2. 有在线自动规则命中时，使用最高优先级规则指定的设备。
3. 没有规则命中时，使用最近在本工具中手动选择的在线设备。
4. 手动选择为空或设备离线时，尝试预设的在线回退设备。
5. 都不可用或没有配置时，不向系统发起切换。

手动选择会保存并跨重启保留，也会在一次自动规则结束后恢复。点击“清除手动选择”才恢复优先使用预设回退设备。**在规则持续命中时手动切换，规则会在防抖/冷却后重新接管**；需要持续手动控制时先勾选“暂停自动切换”。暂停状态也会保存。

这里的“手动选择”指在 AudioRoute 中选择。未暂停时，从 Windows 声音设置或其他软件修改默认设备，可能被已有自动规则或空闲设置重新切回。

默认修改 Console 和 Multimedia 两个播放角色；可勾选“同时切换通信设备”一起修改 Communications。不会修改麦克风、应用混音器路由或音频驱动。

## 音量与静音

- 左下角滑块设置**当前系统默认 Multimedia 输出设备**的端点主音量，范围 0–100%。左侧列表选中其他设备但尚未切换时，滑块仍控制顶部显示的当前输出。
- “静音”控制当前输出端点的静音状态。移动音量滑块不会自动解除静音。
- 托盘提供“当前输出静音”“音量 +5%”“音量 -5%”。
- 程序每个采样周期刷新音量，因此 Windows 音量键或其他工具修改后也会同步显示。
- 切换设备时保留该设备原有音量和静音状态，避免把耳机与扬声器强制设为相同音量。音量由 Windows 管理，不额外写入本工具配置。
- 当前版本不提供每条规则的独立音量预设或逐应用音量混音。

## 防抖与音频连续性

每 800ms 采集一次进程和窗口快照，并在后台线程完成进程读取；通过状态变化识别启动、退出和窗口变化。它是采样式监听，不使用内核事件订阅，短于采样周期的进程可能被忽略。受保护进程的路径无法读取时，完整路径规则不会命中，可尝试进程名规则。

目标规则稳定至少 1000ms 才触发；无规则回退稳定至少 1500ms 才触发；两次成功切换间隔至少 2500ms；失败后至少等待 5000ms 重试。实际响应还受 800ms 采样粒度与系统负载影响。已是目标默认设备时不会重复调用切换接口。

切换过程中不重启音频服务、不禁用设备、不暂停应用、不修改音量。部分角色切换失败时尝试恢复先前角色，失败写入日志。但无法保证所有应用都无缝迁移：独占模式、固定设备的播放器、部分游戏/驱动可能继续使用原设备，需要在应用里选择“系统默认”或重新开始播放。

## 技术栈与结构

采用 **C# + WinForms + .NET Framework 4.8**：原生 Windows 控件和托盘集成直接，系统依赖少，提供可离线构建的单一 EXE；源码使用兼容系统 Framework 编译器的 C# 语法，不引入外部包。

- `src/CoreAudio.cs`：通过 COM 调用 Windows Core Audio 的 `IMMDeviceEnumerator` / `IMMDevice`，枚举输出端点、读取默认设备和友好名称。使用 `IAudioEndpointVolume` 调节主音量与静音。严格释放 COM 引用。
- `src/CoreAudio.cs` 中 `IPolicyConfig`：设置默认播放设备。**这是 Windows 未公开的 COM 接口，不属于微软承诺稳定的公共 API**；系统升级存在兼容风险。枚举与音量使用的是公开 Core Audio API。诊断会检查 PolicyConfig 接口是否可用；异常会显示在界面中。
- `src/ActivityMonitor.cs`：进程快照、`QueryFullProcessImageName`、`EnumWindows`、`GetWindowText`、`GetForegroundWindow`，处理进程退出竞争与访问受限。
- `src/RuleEngine.cs`：纯匹配与切换规划状态机，优先级、在线过滤、手动/回退选择、防抖、失败退避。使用单调计时避免系统时钟调整影响。
- `src/Storage.cs`：JSON 配置原子替换、上一版本备份、损坏恢复、滚动日志，以及当前用户的登录启动注册。
- `src/MainForm.cs` / `RuleDialog.cs` / `Theme.cs`：界面、规则编辑、音量和托盘操作。
- `src/Program.cs` / `InstanceActivation.cs`：STA 入口、单实例保护、窗口激活消息、只读诊断和截图模式。
- `tests/EngineTests.cs`：40 项核心行为断言。
- `tests/UiTests.cs`：24 项界面集成测试；使用真实窗口与进程采样，但注入模拟音频后端，覆盖文件选择、图标资源、重复启动通知恢复隐藏与最小化窗口。

状态决策与 COM 写入在 UI STA 线程串行执行，不重叠运行定时评估。进程快照在线程池采集，避免进程遍历阻塞界面。关键实现含中文注释。

参考资料：

- [微软：枚举音频端点](https://learn.microsoft.com/en-us/windows/win32/coreaudio/enumerating-audio-devices)
- [微软：读取默认音频端点](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint)
- [微软：IAudioEndpointVolume](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume)
- [微软：默认端点与流路由](https://learn.microsoft.com/en-us/windows/win32/coreaudio/getting-the-default-device-endpoint-for-stream-routing)
- [PolicyConfig 接口定义参考](https://github.com/amate/SetDefaultAudioDevice/blob/master/PolicyConfig.h)

## 构建和测试

在解压后的 `AudioRoute` 目录中打开 PowerShell：

```powershell
# 编译；使用 Windows 自带的 Framework C# 编译器，无需联网
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 编译并运行核心测试及界面集成测试
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test

# 运行；--tray 表示启动后直接隐藏到托盘
.\bin\AudioRoute.exe
.\bin\AudioRoute.exe --tray
```

`ExecutionPolicy Bypass` 仅作用于这次 PowerShell 进程，不修改系统执行策略。也可以按本机已有策略直接运行脚本。界面集成测试需要交互式 Windows 桌面，会短暂显示测试窗口，音频操作全部使用模拟后端，测试配置写入系统临时目录；`-TestDirectory` 可以指定其他测试目录。

Visual Studio 用户可安装“.NET 桌面开发”及 .NET Framework 4.8 Targeting Pack，打开 `AudioRoute.csproj` 构建。不要用仅有 `.NET` 运行时的 `dotnet` 命令代替 Framework 编译脚本。

只读诊断（包含本机设备名称和端点 ID，分享前可自行检查）：

```powershell
$report = Join-Path $env:TEMP 'AudioRoute-diagnostics.txt'
Start-Process .\bin\AudioRoute.exe -ArgumentList @('--diagnose', ('"' + $report + '"')) -Wait
Get-Content $report
```

诊断枚举设备、读取三个默认角色、读取当前音量/静音、检查 PolicyConfig COM 接口及采样能力，**不执行设备切换、音量或静音写入**。错误写到指定路径加 `.error.txt`。也可使用 `--preview 路径.png` 生成主窗口预览，不加载真实规则、不启用监听或托盘、不修改系统音频。

## 配置、启动与卸载

配置目录：`%LOCALAPPDATA%\AudioRoute`。

- `settings.json`：规则、回退设备、手动设备、暂停与通信设备选项。设备按稳定端点 ID 绑定，避免同名混淆；重装驱动导致 ID 变化时需重新选择。
- `settings.json.bak`：上一份配置。
- `settings.json.corrupt-时间戳`：损坏配置原件，启动时尝试从备份恢复并显示提示。
- `events.log`：切换和故障记录；约 1 MiB 时轮转到 `.old`。不保存采集到的窗口标题或进程快照。

勾选“登录 Windows 后启动”会写入当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AudioRoute`，以 `--tray` 启动；不需要管理员权限。启用后不要移动 EXE；移动后重新勾选以更新路径。Windows 启动应用设置也可能禁用这项启动，出现未自启时请检查那里。

卸载：先取消登录自启，再从托盘退出，删除解压目录即可。需要清除历史设置时，再自行删除 `%LOCALAPPDATA%\AudioRoute`。退出时保留当前系统设备与音量，不主动切回，以免突然打断正在使用的音频。

## 验证范围

开发机为 Windows 11（10.0.26100）、x64、.NET Framework 4.8.1。

- 编译成功，无编译警告。
- 40 项核心测试和 24 项 UI 集成测试通过。
- 真实 Core Audio 成功枚举本机 4 个输出端点，读取默认角色、当前音量与静音，PolicyConfig 接口查询成功。
- 已渲染并检查主窗口、规则编辑窗口。`screenshots` 中为模拟设备的测试界面截图。
- 为保持开发期间的系统状态，未对本机真实设备执行切换、音量或静音写入；也没有启用真实登录启动。实际听感、驱动切换、设备拔插以及 Windows 10 实机兼容性仍需使用者在目标环境验证。

首次使用可先将一个常用播放器配置为耳机规则，将回退设为扬声器，然后验证打开/关闭、暂停自动切换、手动控制以及音量/静音。若声音未迁移，先检查播放器是否固定绑定了设备。
