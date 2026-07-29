# BudsDock

BudsDock是一款面向Windows 11及以上系统的WPF桌面Dock。它提供可拖动的透明图标栏、程序快捷启动、自定义图标、深浅主题、位置锁定、鼠标穿透、系统托盘和全屏避让。

## 1.0.0功能

- 支持EXE、LNK文件；可在设置页添加，也可拖到Dock空白区域添加。
- 首次运行预置“我的电脑、控制面板、文件资源管理器、Microsoft Edge、回收站”。
- 支持导入PNG、JPG、BMP、GIF、ICO图标。导入文件复制到`%LocalAppData%\BudsDock\icons`。
- 提供原图、圆角底片、单色图标三种外观模式。
- 可调整背景透明度、图标统一尺寸、图标间距、Dock整体缩放、内边距、圆角、倒影和发光强度。
- 图标悬停默认放大到150％，相邻与第二级邻居按距离轻微放大并自动让位；倍率可独立调整。
- 悬停发光从图标像素提取代表色，使用大范围、低亮度的柔和光晕；关闭动画或Windows客户端动画后，图标立即恢复为100％。
- Dock右键菜单和托盘菜单采用与深色设置面板一致的黑底、白字、圆角和悬停样式。
- 提供深色、浅色两套主题，可跟随Windows或手动覆盖。
- 支持简体中文和英文，可跟随Windows显示语言。
- Dock可在主显示器任意拖动；顶部、底部、左侧、右侧和屏幕中心可一键定位。底部居中时，Dock底边距屏幕底部为当前任务栏高度的2.5倍。
- 顶部和底部自动横向排列，左侧和右侧自动纵向排列。
- “固定位置”仅禁止移动；“鼠标穿透”让鼠标事件直接传递给下层窗口。
- 鼠标穿透后可通过系统托盘或`Ctrl＋Alt＋D`恢复交互。
- 默认保持置顶；检测到全屏应用时自动隐藏。
- 默认开机启动，可在设置中关闭。
- 设置窗口支持960×680宽屏双栏与900DIP以下的顶部导航、单栏主从布局，最小可用尺寸为640×480DIP。
- 配置实时保存，页脚显示正在保存、已保存或保存失败状态；支持导出、导入`.budsdock`配置包，包内包含已导入的自定义图标。导入前自动保留恢复副本；为避免外部配置静默改写系统状态，导入会保留本机开机启动选择，并默认关闭鼠标穿透。
- 配置加载会规范化空集合、非法枚举和越界值；遇到未来版本配置时先保留副本并停止覆盖。

## 运行

1. 解压`BudsDock-1.0.0-win-x64-portable.zip`。
2. 运行`BudsDock.exe`。
3. 右键Dock或双击系统托盘图标打开设置。

目标电脑无需预装.NET。当前仅提供x64版本，支持Intel和AMD处理器的Windows 11电脑。

## 便携版说明

程序文件可以放在任意用户有写入和执行权限的目录。配置、日志和导入图标保存在：

```text
%LocalAppData%\BudsDock
```

开机启动项记录当前`BudsDock.exe`路径。移动程序目录后，请手动启动一次BudsDock，应用会刷新启动项。

## 安全与权限

- BudsDock默认以普通用户权限运行。
- 只有用户为单个图标启用“以管理员身份运行”时，Windows才会显示UAC确认。
- 应用不访问网络，不包含在线更新、遥测或账户系统。
- “移除图标”只修改Dock配置，不会删除原程序或自定义图标源文件。
- 当前版本未进行商业代码签名，Windows SmartScreen可能显示未知发布者提示。

## 已知限制

- 只在Windows主显示器显示，不支持每屏一个Dock。
- 透明底板为WPF半透明材质，当前版本未启用系统级实时背景模糊。
- 自定义SVG和WebP图标尚未支持。
- 仅支持EXE、LNK和内置Windows Shell目标；微软商店应用枚举尚未加入。
- 便携版没有安装器、自动更新和代码签名。

## 开发

要求：Windows 11、.NET 10 SDK、x64。

```powershell
dotnet build .\BudsDock.sln -c Release -p:Platform=x64
dotnet run --project .\tests\BudsDock.Tests\BudsDock.Tests.csproj -c Release -p:Platform=x64
dotnet publish .\src\BudsDock\BudsDock.csproj -c Release -p:Platform=x64 -p:PublishProfile=Portable -o .\artifacts\publish\BudsDock-1.0.0-win-x64
python .\scripts\package_release.py
```

工程不依赖第三方NuGet包。32项自动化测试覆盖定位、任务栏间距、悬停倍率、极端缩放光晕边界、动态图标发光、全部设置边界、配置迁移、异步命令、防抖与并发保存、保存失败、配置恢复只读保护、版本门禁、导入事务、订阅者隔离、资源一致性、托盘状态和关键XAML交互约束。手工验收项见`docs/TESTING.md`。

## English summary

BudsDock is a Windows 11 desktop Dock built with WPF and .NET 10. The portable x64 build is self-contained. It supports custom application icons, dark/light themes, free positioning on the primary display, click-through recovery via tray or `Ctrl+Alt+D`, full-screen avoidance, startup registration, and bilingual UI.
