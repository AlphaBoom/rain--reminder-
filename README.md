# Rain Reminder · 早晨降雨提醒

Windows 11 上常驻的 C#/.NET 10 应用。在工作日早餐时段，若今天剩余时间有降水风险，就弹出一次“记得查看天气、考虑带伞”的提醒。

- 真正的 Windows 服务 `RainReminder`，随系统自动启动；无需保持应用窗口开启。
- 桌面提醒进程随用户登录启动，**托盘图标默认隐藏**，可以在设置里打开。
- 首次运行选择地点和早晨时段，默认 **06:00–10:00**，按所选地点的当地时间判断。
- 自动跳过周末和日本法定假日，包含振替休日、国民の休日；使用内阁府官方日历，每周刷新并缓存。
- 默认：任一剩余小时降水概率 ≥ 40%，**或**降水量 ≥ 0.1 mm 时提醒。降水包含雨、阵雨和雪。
- 无雨不打扰；每人每天最多一次。解锁后才弹窗，错过早晨时段不会在下午补弹。
- 无雨时每 30 分钟更新一次；天气网络错误 5 分钟后重试。休眠时不唤醒电脑，恢复后继续检查。
- 不需要 API Key，不依赖 Python。Windows 安装包自带 .NET 运行时。

## 安装

1. 下载 Windows 构建产物 `rain-reminder-win-x64.zip` 并完整解压。
2. 双击 `scripts/Install.cmd`，接受 Windows 管理员权限提示以注册系统服务。
3. 在首次设置窗口搜索地点、确认时段并保存。之后关闭设置即可。

重新设置：开始菜单搜索 **Rain Reminder**。即使隐藏了托盘图标，也能打开现有后台进程的设置窗口（最多约 2 秒）。关闭设置不会停止提醒。

卸载：管理员 PowerShell 运行 `C:\Program Files\RainReminder\Uninstall.ps1`。默认保留设置；追加 `-RemoveSettings` 可删除公共配置和假日缓存。个人的当天提醒记录保留在本用户 AppData 中，不影响新日期。若执行策略阻止脚本，可使用 `powershell.exe -ExecutionPolicy Bypass -File "C:\Program Files\RainReminder\Uninstall.ps1"`。

这是未签名的个人工具，安装包目前未做代码签名。

## 工作日与假日日历

日本内阁府 CSV 已附带 1955–2027 年数据。运行时自动获取新版，网络不可用时继续使用覆盖当年的缓存；当年日历完全不可用时暂停提醒并重试，以免在法定假日错误弹窗。公司特有休假可填入“额外不提醒的日期”。年末年初等公司休假不属于法定假日的部分不会被自动排除。

天气预报仅作出门前提示。提醒提供“查看天气”按钮，在默认浏览器中搜索所选地区的天气。提醒窗口会保留到关闭，不依赖 Windows 通知中心或勿扰模式。

## 为什么有两个进程

Windows 系统服务运行在 Session 0，不能直接向登录用户显示交互窗口。因此：

- `RainReminder.Service.exe`：以 **LocalService** 运行，读取配置、更新假日与天气，写入预报状态。
- `RainReminder.Tray.exe`：以普通用户权限运行，负责首次设置、读取状态、显示提醒；可完全隐藏托盘图标。

桌面进程通过安装时建立的登录计划任务启动。服务与桌面通过受 ACL 限制的 JSON 文件交换数据，不监听任何网络端口。公共配置只允许安装用户和管理员修改，服务只读配置；服务状态由服务写入，普通用户只读。可执行文件安装在 Program Files。

一台电脑使用一组地点配置；安装时绑定执行安装的 Windows 用户。多用户独立地点配置目前不支持。仅退出桌面进程时，服务继续检查，但无法显示提醒，直到从开始菜单重新打开应用或再次登录。

## 源码构建

需要 .NET 10 SDK。Windows 上运行：

```powershell
./scripts/Build.ps1
```

输出 `dist/rain-reminder-win-x64.zip`。也可传入 `-Runtime win-arm64` 构建 ARM64（ARM64 未在 x64 构建机上执行桌面烟雾测试）。

核心逻辑测试可以跨平台运行，无额外测试库：

```sh
dotnet run --project tests/RainReminder.Tests -c Release
```

GitHub Actions 自动执行逻辑测试、Windows 打包、窗体构造烟雾测试，以及临时构建机上的服务安装卸载检查，并上传安装包。该 CI 不会自动将服务安装到你的电脑，也不代替真实 Windows 11 的登录、锁屏和休眠验收。

## 数据与状态

- 公共配置：`%ProgramData%\RainReminder\config\settings.json`
- 天气与假日缓存、服务日志：`%ProgramData%\RainReminder\status\`
- 个人提醒记录与桌面日志：`%LocalAppData%\RainReminder\`

天气数据：Open-Meteo；地名：GeoNames；假日：日本内阁府。来源和说明见 [data/README.md](data/README.md)。
