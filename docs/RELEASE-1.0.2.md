# BudsDock 1.0.2

修复从全屏应用切回桌面后，Dock偶发消失、延迟出现或持续无法恢复的问题。

- 窗口切换和最小化事件触发状态检查，保留500毫秒兜底检查。
- 修复系统最小化、底层窗口隐藏和置顶状态丢失后的恢复，自动恢复不抢占当前应用焦点。
- 关闭全屏避让后，仍可从系统最小化恢复。
- 按保存的显示器选择判断全屏，避免最小化临时坐标影响判断。
- Portable内置.NET 10.0.14。

45项基础回归测试、12项真实窗口测试全部通过，包含50次连续切换和界面线程阻塞后的恢复。Portable与Compact均通过深浅主题、中英文和WPF绑定检查。详细结果与验证范围见[验收说明](https://github.com/Buds-2025/budsdock/blob/v1.0.2/docs/UPGRADE-1.0.2.md)。

下载后解压，退出旧版并运行新版`BudsDock.exe`。原有图标、位置与外观设置继续使用。

| 发行包 | 运行要求 |
| --- | --- |
| `BudsDock-1.0.2-win-x64-portable.zip` | Windows 11 x64，自带运行时 |
| `BudsDock-1.0.2-win-x64-compact.zip` | Windows 11 x64，已安装.NET 10 Desktop Runtime x64 |

随附SHA256校验文件与JSON清单。
