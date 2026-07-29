# BudsDock UI/UX 与可访问性审查报告

> 历史记录：本文是设置窗口结构性重做前的只读审计，尺寸、绑定和交互结论不得作为当前状态使用。当前实现与验收要求以`docs/DESIGN.md`和`docs/TESTING.md`为准。

> 范围：`D:\AI Coding\dock\src\BudsDock`（含 Views / Resources / Services / ViewModels / Models / Converters / Interop）
> 模式：只读静态分析 + 关键运行时绑定复查。未对仓库任何文件做修改。
> 文档对照：`docs/DESIGN.md`、`docs/reference-dock.png`、`docs/TESTING.md`。
> 评估维度：参考图还原度 / 深浅主题 / 图标三种外观 / 透明度-间距-缩放 / 横纵排列 / 双语 / 鼠标穿透与恢复 / 悬停放大与淡发光 / XAML 绑定 / 运行时资源切换 / 键盘访问 / 主题对比度 / 布局溢出 / 误操作防护。

严重度分级：**P0 致命** · **P1 高** · **P2 中** · **P3 低 / 建议**。

---

## 0. 总体结论

| 维度 | 结论 |
| --- | --- |
| 参考图与文档风格 | 文档(`DESIGN.md`)与主题色调、规格数据（默认 54px、24% 倒影、34% 发光、112%/104% 悬停、18px 圆角等）全部在 XAML/CS 中以一致数值落地；缺一个托盘菜单图标本地化入口、缺 `Slider` Touch/Mouse hover 焦点环。 |
| 运行时资源切换 | `ThemeService.Apply` 与 `LocalizationService.Apply` 通过`MergedDictionaries`替换实现，全部用户可见文案/主题走 `DynamicResource`，切换可即时生效；**唯一例外是 `Window.Title` 与 `Window` 派生属性在子 Window 上的初次抓取时机**——已对设置窗口确认可重解析，但有 1 处隐患（P2）。 |
| 三种图标外观 | DataTemplate 通过`DataTrigger`在 Original/Tile/Monochrome 三态切换；Monochrome 通过 `OpacityMask` 应用于 `AccentBrush`，Tile 使用低对比度底板；满足规范。1 个潜在视觉副作用：倒影会复制 `DropShadowEffect`（P3）。 |
| 鼠标穿透恢复 | 双通道：托盘 + `Ctrl+Alt+D` 全局热键。WS_EX_LAYERED/TRANSPARENT/TOOLWINDOW 在 `NativeWindowService.ApplyClickThrough` 中合并设置。`RecoveryHotkey` 失注册时通过气泡兜底。设计合理；**唯一缺口**：Dock 自身在穿透模式下完全失去键盘可达性（已写明在 P1）。 |
| 悬停放大与淡发光 | `DockViewModel.SetHover` 给当前与相邻图标分配 `HoverScale`，绑定到 `ScaleTransform`；`GlowOpacityConverter` 在 `IsHovered && GlowIntensity` 时输出强度驱动 `DropShadowEffect.Opacity`。`EnableHoverAnimation = false` 时与 `ClientAreaAnimation=false` 时均退化为 1.0。正确。 |
| 横纵排列 | `DockPositionService.OrientationFor`：上/下→Horizontal，左/右→Vertical，Free→保留当前。`DockOrientationConverter` 双向往返。`Orientation` 同步：`ItemsControl.ItemsPanel` 通过 `RelativeSource AncestorType=ItemsControl` 拿 DataContext 解析。正确。 |
| 双语 | `Strings.en-US.xaml` 与 `Strings.zh-CN.xaml` 两套字典键名完全对齐（含系统托盘、`Tray` 菜单、`MessageBox` 提示）。`LocalizationService.Apply` 切换并设置 `CurrentUICulture`。**仍有两处硬编码双语字符串与一组无 DynamicResource 的标题栏字符**（见 P2）。 |
| 布局溢出 | 设置窗口默认 1080×720 在 1024×768 屏幕上会越界；Padding/Margin 系统整体节奏 8/12/18/24 落实到位。详见 P0-1、P2。 |
| 误操作 | 中英文 `MessageBox` 二次确认（删除项）齐全；切换穿透、自动启动等高风险操作**无确认弹窗**（设计预期不弹，但需明确文档化）。 |

---

## 1. P0 — 致命 / 阻塞级

### P0-1 设置窗口默认尺寸在小屏上整体越界，无法看到标题栏
- **位置**：`src/BudsDock/Views/SettingsWindow.xaml:9`
- **现象**：`Width="1080" Height="720"` + `MinWidth="920" MinHeight="620"` + `WindowStartupLocation="CenterScreen"`。
- **影响**：1366×768 屏幕下底部约 48px 被裁掉；1024×768 屏幕下高度高于可视区 100px+，标题栏与右下角的手柄都看不见，用户无法拖动 / 关闭，只能 Alt+F4（会触发隐藏而非最小化）。
- **修复建议**：将硬尺寸改为 `SizeToContent="WidthAndHeight"` + 一个合理默认值（如 `Width="880"` `Height="640"` `MinWidth="720"` `MinHeight="540"`）。或者保留 `1080×720` 但增加 `MaxWidth`/`MaxHeight` 取屏幕工作区约束（`SystemParameters.WorkArea`）。

### P0-2 `ModernButton` 默认模板里 `IsEnabled="False"` 状态下边框/前景对比度丢失
- **位置**：`src/BudsDock/Resources/Styles.xaml:75-77`
- **现象**：`Trigger IsEnabled=False` 只把 `Root.Opacity` 设到 `0.42`，但 `Opacity` 同时压低 `Foreground` 与 `Background`——文字 `TextPrimaryBrush` 与按钮背景 `SurfaceAltBrush` 同时变透明。浅色主题下 `TextPrimary #202431 × 0.42` 叠加 `SurfaceAlt #E9EDF5 × 0.42` 后，对比度约 4.6:1，接近 WCAG AA 红线；深色主题下 `TextPrimary #F1F3FA × 0.42` 与 `SurfaceAlt #292E3B × 0.42` 复合后约 6:1，勉强通过大字号 AA，但未达 AAA。
- **影响**：禁用按钮的"不可点"语义依赖该透明度反馈；色弱用户可能误激活。
- **修复建议**：用 `TextSecondaryBrush` 而非整体透明度表达禁用——或保留透明度，但确保禁用态文字使用 `TextSecondary` + 取消 `Background` 透明度叠加。

---

## 2. P1 — 高 / 显著缺陷

### P1-1 Dock 窗口对键盘不可达，且图标按钮无焦点环
- **位置**：`src/BudsDock/Views/DockWindow.xaml:22-37`、`src/BudsDock/Views/DockWindow.xaml.cs:13`（Window 派生）。
- **现象**：
  - `WindowStyle="None"` + `WS_EX_TOOLWINDOW` 把 Dock 从 Alt+Tab 任务切换器移除。
  - 鼠标穿透模式（`WS_EX_TRANSPARENT`）让 Dock 完全无法接收输入；恢复路径只有托盘与 `Ctrl+Alt+D` 热键，已文档化。
  - `DockIconButton` 模板显式 `FocusVisualStyle="{x:Null}"`，没有任何键盘焦点环；按钮没有 `TabIndex`，键盘焦点无法 tab 到任何图标。
- **影响**：完全闭锁的鼠标体验，**屏幕阅读器在所有模式下均读不出图标名称**（`Button` 没有 `AutomationProperties.Name`，`ToolTip` 不被读屏）。
- **修复建议**：
  - 添加 `<Setter Property="AutomationProperties.Name" Value="{Binding EffectiveName, RelativeSource=...}"/>` ——或为 `Button` 增加 `ToolTipService.ShowOnDisabled` 与 Focusable 默认开启；
  - 把 `FocusVisualStyle` 替换为自定义描边：`Style="{x:Static SystemParameters.FocusVisualStyle}"` + 圆角；
  - 为 Dock 增加一条 CLI 自定义焦点环：菜单按钮 `F10` 打开设置、方向键在图标间移动、`Enter`/`Space` 启动。

### P1-2 设置窗口自定义标题栏按钮缺少可达性名称，且图标字符不可本地化
- **位置**：`src/BudsDock/Views/SettingsWindow.xaml:41-42`
- **现象**：`Content="—"` 与 `Content="✕"` 是 Unicode 字形，`ToolTip` 用 `DynamicResource` 绑定，但 `Button` 没有 `AutomationProperties.Name`；`Window.Title` 仍可能渲染为系统默认 "BudsDock Settings"（依赖所属主题），与最小化按钮字符同样不可本地化。
- **修复建议**：添加 `<Button.Style><Style>` 中 `AutomationProperties.Name="{TemplateBinding ToolTip}"`，或显式绑定到 `DynamicResource`。`Window.Title` 改为完全由 `DynamicResource` 提供，并把 `Title` 设置时机从 `InitializeComponent` 后的 `Loaded` 中重新评估（见 P2-3）。

### P1-3 `DropShadowEffect Color={DynamicResource GlowColor}` 与主题字典加载顺序的隐患
- **位置**：`src/BudsDock/Views/DockWindow.xaml:105`、`src/BudsDock/Services/ThemeService.cs:9-30`
- **现象**：`App.xaml` 启动顺序为 `Styles → Theme.Dark → Strings.zh-CN`，主题切换通过 `Remove + Add` 完成。当前实现正常工作。但 `<DropShadowEffect.Color>` 使用 `{DynamicResource GlowColor}`，而 `GlowBrush` 也是主题资源但**未被 XAML 引用**——若有人后续将 `Color` 改成 `Brush`（常见失误），会因为资源类型不匹配直接抛 `XamlParseException`；运行期也很难调试。
- **修复建议**：增加单元测试断言"`DockSurfaceBrush`/`AccentBrush`/`GlowColor` 等每个 key 在两个主题字典中类型一致"——或将 `GlowColor` 改名为 `GlowEffectColor` 表明用途，`GlowBrush` 也被实际使用。

### P1-4 设置窗口 Tab 顺序丢失：自定义 `TabControl` 模板只渲染 `SelectedContent`，键盘 Tab 无法定位到非激活页面
- **位置**：`src/BudsDock/Views/SettingsWindow.xaml:67-72`
  ```xml
  <TabControl.Template>
    <ControlTemplate TargetType="TabControl">
      <ContentPresenter Content="{TemplateBinding SelectedContent}"/>
    </ControlTemplate>
  </TabControl.Template>
  ```
- **现象**：模板完全剥离 TabStrip，让左侧 `ListBox` 充当导航。但 `TabControl` 的 `KeyboardNavigation.TabNavigation` 默认 `Cycle`，焦点进入 TabControl 内容区后无法通过方向键在兄弟页面间切换；用户必须先点 ListBox 才能切换。
- **修复建议**：
  - 为 `ListBox` 绑定按键（`InputBindings` + `KeyBinding`）绑定 `Ctrl+1..4` 切页；
  - 或在 `TabControl` 上增加 `FocusManager.FocusedElement="{Binding ElementName=ContentHost, Mode=OneWay}"` 让首次进入 TabControl 时焦点落到第一项。

### P1-5 取消键（Esc）未在任何窗口生效
- **位置**：`src/BudsDock/Views/SettingsWindow.xaml.cs`、`src/BudsDock/Views/DockWindow.xaml.cs`
- **现象**：设置窗口没有任何 `InputBinding Key="Escape"`；在 `MessageBox` 之外，用户无法用 Esc 关闭弹出的设置（除 Alt+F4 会触发 `Hide()`，但容易被误以为是关闭程序）。Dock 窗口在启用 `IsClickThrough` 时也无法从 Dock 本体关闭穿透。
- **修复建议**：在 `SettingsWindow` 添加 `KeyBinding Key="Escape" Command="...Close"`；Dock 窗口在显式穿透时允许 `Shift+Ctrl+Alt+D` 单键组合在穿透态自恢复（与原 `Ctrl+Alt+D` 协调）。

---

## 3. P2 — 中 / 体验明显但可绕开

### P2-1 倒影 `VisualBrush` 会复制 `DropShadowEffect`
- **位置**：`src/BudsDock/Views/DockWindow.xaml:155-174`
- **现象**：`<VisualBrush Visual="{Binding ElementName=IconVisual}" .../>` 把 `IconVisual` 整个子图（包括 `DropShadowEffect BlurRadius=22` 的发光层）一并镜像，再叠加渐隐 `OpacityMask`。结果：每个图标下方会出现一条"光晕渐隐带"，参考图（`reference-dock.png`，按 `DESIGN.md` 描述"倒影只用于建立层次，不与图标本体竞争"）应当只有图标本身的渐隐。
- **修复建议**：把发光移到 `IconVisual` 之外、单独挂在 `Grid.Row=0` 的另一个 `Grid` 上，让倒影只绑镜像图标本体。或把发光 `Opacity` 在 `IsHovered == false` 时强制归零以降低视觉副作用。

### P2-2 浮动拖拽在锁定 / 穿透态下行为不一致
- **位置**：`src/BudsDock/Views/DockWindow.xaml.cs:129-150`、`src/BudsDock/Models/AppSettings.cs:26-27`
- **现象**：`IsPositionLocked` 由复选框与托盘菜单双向控制；但没有任何 UI 反馈"已锁定"——Dock 视觉上仍是可拖的样子，用户按住空白拖动无反应，体感像失灵。
- **修复建议**：在 Dock 表面加一个隐藏的悬浮图标（类似 macOS Launchpad 的锁标），或把鼠标在空白处的 `Cursor` 在锁定态切回 `Arrow`，托盘上把已锁菜单项变灰。

### P2-3 启动时的双语文案闪烁
- **位置**：`src/BudsDock/App.xaml:9-11`、`src/BudsDock/App.xaml.cs:56-57`
- **现象**：
  - `App.xaml` 启动时即合并 `Theme.Dark` + `Strings.zh-CN` 两个字典；
  - `OnStartup` 中 `LocalizationService.Apply(settings.Language)` 与 `ThemeService.Apply(settings.ThemeMode)` 在窗口显示**之后**才被调用（带 `await SettingsService.LoadAsync()`）；
  - 设置窗口是延迟构造 `_settingsWindow = new SettingsWindow { DataContext = _settingsViewModel };`，所以语言切换可以正确初始化，**但 Dock 窗口已在第一次 `Show()` 时使用 zh-CN + Dark**；
  - 设置窗口标题 `Title="{DynamicResource Settings.Title}"` 在 `InitializeComponent` 时解析一次，并在字典替换后通过 `DynamicResource` 重新解析——但这一行为依赖 `Window.Title` 的 DP 元数据默认值。经验上：关闭 / 重开窗口后才会刷新，**当前实现下首次显示仍正常**（因为窗口初始化晚于 `Apply`）。
- **修复建议**：把主题与字符串 `Apply` 调用挪到 `OnStartup` 第一行（同步阻塞 `LoadAsync` 或使用 `XamlReader.Load`），确保字典在第一个 `Show` 之前就位；或为 `MainWindow` 使用 `Loaded` 后再做一次轻量重渲染。

### P2-4 高风险动作缺少二次确认
- **位置**：`src/BudsDock/ViewModels/SettingsViewModel.cs`、`src/BudsDock/Services/TrayService.cs`
- **现象**：
  - 删除单个图标有确认（行 108-114）；
  - **导入配置**（`ImportCommand`，行 204-225）覆盖当前设置不会弹窗确认；
  - **恢复外观默认值**（`ResetAppearanceCommand`，行 236-250）会一次性覆盖 12 个外观字段；
  - **托盘菜单切换** Lock / ClickThrough 是即时生效（行 47-53）。
- **影响**：误点到托盘 → 切到穿透 → 整个屏幕"丢失"Dock。
- **修复建议**：导入配置与恢复外观至少用 `MessageBox` 二次确认；ClickThrough 切换伴随一次性气泡提醒可恢复。

### P2-5 拖拽放手判定 `FindAncestor<Button>` 在视觉特效边界可能误判
- **位置**：`src/BudsDock/Views/DockWindow.xaml.cs:131`
- **现象**：拖拽判定的 "是否点在图标上" 用了 `VisualTreeHelper.GetParent` 回溯，遇到 `DockIconButton` 模板里的 `Border`（背景透明但 `Padding=2`）就返回 `Button`。当 `DropShadowEffect BlurRadius=22` 视觉阴影延伸出图标周围 22px 时，用户点击阴影区域也会被视为"点到图标"。预期如此——但**当 `EnableHoverAnimation=false` 且阴影完全透明**时，阴影区域不应该响应（实际上仍响应）。
- **修复建议**：把发光改用 `Adorner` 层 + `IsHitTestVisible=false`；或判定改为 `HitTestVisible=true` 时才回溯。

### P2-6 主题资源切换时滑块 / 复选框交互区残留旧主题色
- **位置**：`Resources/Theme.Dark.xaml` & `Resources/Theme.Light.xaml`、`Styles.xaml` 中的 `Slider.Foreground={DynamicResource AccentBrush}`、`CheckBox` 默认模板未重写。
- **现象**：WPF 默认 `Slider` 在 `Thumb` 之外（含 `Track.DecreaseRepeatButton`）的 brush `SystemColors.Control` **不会**随 `DynamicResource` 改变；只有 `Foreground`（影响滑过的部分）会变。这是深浅切换最常见的"斑驳"感来源。
- **修复建议**：在 `Styles.xaml` 中给 `Slider` 与 `ComboBox` 显式 `Template`，并把所有 brush 用 `TemplateBinding`/`DynamicResource` 替换。或者用第三方库如 `MahApps.Metro` 的 `MetroSlider`。

### P2-7 ComboBox / ListBox 默认模板不会被自定义 `Styles.xaml` 完全替换
- **位置**：`src/BudsDock/Resources/Styles.xaml:124-130, 143-171`
- **现象**：`Style TargetType="ComboBox"` 只覆写了 `Foreground`/`Background`/`BorderBrush`/`Padding`/`MinHeight`，**没有 `Template` 覆盖**——实际下拉框的弹出层使用 Aero/系统默认模板；切到深色主题时弹出项的高亮 / 选中色仍是浅色系统色。`ListBoxItem` 已覆写，OK。
- **修复建议**：与 P2-6 一起，提供 `ComboBox` 的完整 `ControlTemplate`。

### P2-8 悬停放大在 Windows 关闭动画时会自动退化，无明显反馈
- **位置**：`src/BudsDock/ViewModels/DockViewModel.cs:115`
- **现象**：`!SystemParameters.ClientAreaAnimation` 时强制 `HoverScale=1.0`；这只影响当前 hovered 一帧——`EnableHoverAnimation` 复选框仍然勾选。用户在系统设置里关闭动画后再回来看，Dock "不动" 但界面显示"已启用"。
- **修复建议**：把 `EnableHoverAnimation` 自动与 `ClientAreaAnimation` 关联并显示提示文本。

---

## 4. P3 — 低 / 建议

### P3-1 `DockWindow.xaml.cs:131` 使用 `VisualTreeHelper.GetParent`，DataTrigger 命中 `Button` 但没有 `IconScope` 包装
- 建议：用 `Button.CommandParameter` 判定的更轻量做法，或显式声明 `_dragSurface` 名称。

### P3-2 参考图标集合的内置字体依赖 `Segoe Fluent Icons`
- **位置**：`src/BudsDock/Services/IconService.cs:165`
- **现象**：`new FontFamily("Segoe Fluent Icons")` 字体在 Windows 10（21H2 之前）及无独立更新包的 Windows Server 上**不存在**，字形会渲染为 □。
- **修复建议**：增加字体回退链：`new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol")`。

### P3-3 `Brushes.White` 与硬编码 `#BFFFFFFF` / `#00FFFFFF`
- **位置**：`src/BudsDock/Views/DockWindow.xaml:170-172`、`src/BudsDock/Services/IconService.cs:167`
- **现象**：内置图标的字形填充 `Brushes.White`；倒影渐隐使用硬编码的 Alpha 值。
- **影响**：在深色 / 浅色主题下都"白底白字"——参考图风格（"低对比度主题底片"）下应使用 `DynamicResource` 让灰阶跟随主题。
- **修复建议**：把 `IconTileBrush`（已存在）应用为内阴影或微高光，并增加 `IconForegroundBrush`。

### P3-4 `LocalizationService.Resolve` 在反复切换 `System` 时不会回到系统语言
- **位置**：`src/BudsDock/Services/LocalizationService.cs:41-51`
- **现象**：第一次调用 `Apply(System)` 后 `CurrentUICulture` 被强制设为 zh-CN 或 en-US；之后 `Apply(System)` 读取的是被改写后的 `CurrentUICulture`，**不是真实的系统首选语言**。
- **修复建议**：缓存第一次读到的 `CultureInfo.InstalledUICulture`，或仅在第一次 resolve 时读取并锁定。

### P3-5 单实例 `Mutex` 通知现有实例
- **位置**：`src/BudsDock/App.xaml.cs:41-48`
- **现象**：第二个进程启动时只弹一次 `MessageBox` 后 `Shutdown`；不通知已有 Dock 显示自身 / 打开设置窗口。
- **修复建议**：使用命名管道把请求转发给首个实例，由其打开设置。

### P3-6 列表项滚动条 `Visibility` 默认自动而 ListBox 背景 `Transparent` 与表面同色，视觉上滑块消失
- **位置**：`Resources/Styles.xaml:143-146`、设置 → 图标页 `ListBox`。
- **修复建议**：将 `ScrollViewer.VerticalScrollBarVisibility` 一致设为 `Auto` 并在 `Resources/Styles.xaml` 给 `ScrollViewer` 加 `Foreground={DynamicResource AccentBrush}`。

### P3-7 图标管理页的 ListBox 选中态缺 `AutomationProperties`
- **位置**：`src/BudsDock/Views/SettingsWindow.xaml:97-130`
- 建议对每个 `ListBoxItem` 模板补 `ToolTip`/`AutomationProperties.Name` 与图标焦点环。

### P3-8 `DockPositionService.Calculate` 处理 `Free` 时仍走默认 `BottomCenter` 计算
- **位置**：`src/BudsDock/Services/DockPositionService.cs:30`
- **现象**：函数对 `Free` 仍以 BottomCenter 行为回退——调用方已经过滤 `Free`，但作为一个公共方法，仍可能误用。建议显式 `throw new ArgumentOutOfRangeException`。

### P3-9 反射 VisualBrush 影响性能
- 每个图标持有 `VisualBrush` 镜像主图标图层（含 DropShadow）。当 `DockItems.Count` 大、或者 `DropShadow` blur 提高时会显著掉帧。考虑在 `VisualMode != Original` 或 `ReflectionOpacity <= 0.01` 时不渲染。

### P3-10 `AcceleratorKey` / `AccessKey` 在设置页未使用
- 例如 `&Icons`、`&Appearance`、`&Position`、`&System` 可以通过下划线访问键启用 `Alt+I/A/P/S`。

### P3-11 测试断言 `DockPlacement.ScreenCenter` 与 `DockPlacement.Free` 走相同 BottomCenter 分支无覆盖
- `tests/.../Program.cs:35-61` 仅覆盖 TopBottom / LeftRight / Clamp / Bounds / DefaultItems / Bundle。建议补 `Free` 与 `ScreenCenter` 的测试。

### P3-12 `_dockWindow.RecoveryHotkeyRegistered` 检查与气泡提示只在启动后一帧执行
- **位置**：`App.xaml.cs:84-90`
- 若热键被晚于启动的进程占用（例如另一个程序后启动），气泡不会提示；建议监听 `UserPreferenceChanged` 类机制不可，因为此事件无系统级通知，可在 `RestoreInteraction` 内部每次重新检测。

---

## 5. 资源 / 颜色 / 对比度速查表

| 组合 | 计算比 | WCAG |
| --- | --- | --- |
| Dark: `TextPrimary #F1F3FA` × `WindowBackground #151820` | ~14.0 | AAA |
| Dark: `TextPrimary #F1F3FA` × `Surface #20242F` | ~11.7 | AAA |
| Dark: `TextSecondary #B7BECD` × `WindowBackground #151820` | ~9.0 | AAA |
| Dark: `Accent #8EA6FF` × `Surface #20242F` | ~5.6 | AA（large text only，普通文本不达标） |
| Dark: `IconTile #3C46566E` × `DockSurface #E6222631` | 1.9（仅作底片可接受） | — |
| Light: `TextPrimary #202431` × `WindowBackground #F3F5FA` | ~14.5 | AAA |
| Light: `TextPrimary #202431` × `Card #FAFBFE` | ~14.0 | AAA |
| Light: `TextSecondary #5D667A` × `Card #FAFBFE` | ~6.7 | AA |
| Light: `Accent #4F6D8` × `Card #FAFBFE` | ~5.4 | AA（large text），普通文本略低 |
| Light: `Danger #C34F5A` × `SurfaceAlt #E9EDF5` | ~4.6 | AA（large only） |

**修复建议**：
- `Accent.Light` 由 `#4F6FD8` 提到 `#3E5BC2` 或更深约 0.1 L\*，可确保普通字号 AA。
- `Danger` 文字用在按钮上时加 `FontWeight=SemiBold`（当前 `#A1A8B5` × `#C34F5A` 接近 AA 临界）。
- `IconTileColor` 在 Light 主题下与 `DockSurface` 仅相差约 1.5 L\*，建议深 0.05 L\* 以建立"底片"层次。

---

## 6. 文件级问题速查（按行号定位）

| 文件 | 行 | 级别 | 问题 |
| --- | --- | --- | --- |
| `src/BudsDock/App.xaml` | 7-11 | P2 | 资源字典顺序与运行时切换间接相关，挪到同步路径会更稳 |
| `src/BudsDock/App.xaml.cs` | 45 | P3 | 二次启动 MessageBox 硬编码双语，可走 `Message.AlreadyRunning` |
| `src/BudsDock/App.xaml.cs` | 84-90 | P3 | 热键检测仅启动一帧，无后续轮询 |
| `src/BudsDock/Views/SettingsWindow.xaml` | 9 | **P0** | `Width=1080` 在小屏越界 |
| `src/BudsDock/Views/SettingsWindow.xaml` | 41-42 | **P1** | 自定义标题栏按钮缺可访问性名称 |
| `src/BudsDock/Views/SettingsWindow.xaml` | 67-72 | **P1** | 自定义 TabControl 模板让键盘无法跨页 |
| `src/BudsDock/Views/SettingsWindow.xaml` | 143-144 | P3 | `Icons.Empty` 文案在 ListBox 未选中时覆盖中心，建议让出更多空间显示提示 |
| `src/BudsDock/Views/DockWindow.xaml` | 22-37 | **P1** | `DockIconButton` 去焦点环 + 无 `AutomationProperties.Name` |
| `src/BudsDock/Views/DockWindow.xaml` | 105 | **P1** | `DropShadowEffect.Color` 强依赖主题键；`GlowBrush` 资源未被使用 |
| `src/BudsDock/Views/DockWindow.xaml` | 155-174 | P2 | 倒影含发光导致 halo |
| `src/BudsDock/Views/DockWindow.xaml.cs` | 129-150 | P2 | 拖拽无锁定反馈 |
| `src/BudsDock/Resources/Styles.xaml` | 75-77 | **P0** | `IsEnabled=False` 透明度影响对比度 |
| `src/BudsDock/Resources/Styles.xaml` | 124-130, 138-141 | P2 | `Slider` / `ComboBox` 默认模板未覆盖，主题切换残留系统色 |
| `src/BudsDock/Resources/Theme.Dark.xaml` | 全 | P3 | `GlowBrush` 资源未引用 |
| `src/BudsDock/Resources/Theme.Light.xaml` | 同 | P3 | 同上 |
| `src/BudsDock/Services/ThemeService.cs` | 22-30 | **P1** | 字典 Add 用相对 URI，`MergeDictionaries` 调试不易 |
| `src/BudsDock/Services/LocalizationService.cs` | 41-51 | P3 | `Resolve(System)` 在第二次切换时拿到的是已被覆盖的 `CurrentUICulture` |
| `src/BudsDock/Services/IconService.cs` | 165 | P3 | 字体 `Segoe Fluent Icons` 在 Win10 21H2 之前无 |
| `src/BudsDock/ViewModels/DockViewModel.cs` | 115 | P2 | 与 `ClientAreaAnimation` 联动缺可见提示 |
| `src/BudsDock/ViewModels/SettingsViewModel.cs` | 31 | P2 | `ResetAppearance` / `Import` 无二次确认 |
| `src/BudsDock/Models/AppSettings.cs` | 26 | P2 | `IsPositionLocked` 无视觉反馈 |
| `src/BudsDock/Models/AppSettings.cs` | 33 | **P2** | `Hotkey` 字段被 UI 展示（"穿透恢复快捷键"），但 `NativeWindowService.RegisterRecoveryHotkey` 直接硬编码 `ModAlt \| ModControl, VkD`——设置项从不生效，纯属性漂移 |
| `src/BudsDock/Services/NativeWindowService.cs` | 39-40 | **P2** | 修复上一项：要么消费 `Settings.Hotkey`，要么隐藏该设置项 |
| `tests/BudsDock.Tests/Program.cs` | 35-61 | P3 | 缺 `Free` / `ScreenCenter` / `ResourceThemesCount` 测试 |

---

## 7. 一致性与设计规范符合性（DESIGN.md 复盘）

| 设计要求 | 实现位置 | 状态 |
| --- | --- | --- |
| 深浅色同色相中性色 | `Theme.Dark.xaml` / `Theme.Light.xaml` | ✓ |
| 默认图标 54px / 范围 28-112 | `AppSettings.cs:15` & `SettingsWindow.xaml` 滑块 | ✓ |
| 默认间距 12px / 范围 0-48 | `AppSettings.cs:16` & 滑块 | ✓ |
| 默认底板内边距 12px / 4-36 | `AppSettings.cs:18` & 滑块 | ✓ |
| 默认圆角 18 / 0-36 | `AppSettings.cs:19` & 滑块 | ✓ |
| 缩放 65-180% | `AppSettings.cs:17` | ✓ |
| 倒影默认 24%、自上而下渐隐 | `AppSettings.cs:20` & `DockWindow.xaml:169-173` | ✓（但带发光） |
| 悬停 112%、相邻 104% | `AppSettings.cs:22-23`、`DockViewModel.cs:121-126` | ✓ |
| 发光默认 34%、无位移 | `AppSettings.cs:21`、`DockWindow.xaml:105` | ✓ |
| 高权限走 UAC | `LauncherService.cs:30` | ✓ |
| 中英文在独立字典 | `Strings.*.xaml` | ✓ |
| 鼠标穿透后托盘 / 全局热键恢复 | `DockWindow.xaml.cs:51-61`、`TrayService.cs:51-53` | ✓ |

---

## 8. 修复优先级建议

1. **P0-1**：设置窗口尺寸策略（最简单、最高收益）。
2. **P0-2**：`IsEnabled=False` 模板改为 `TextSecondary`（避免禁用按钮文字"消失"）。
3. **P1-1 / P1-2**：Dock 与设置窗口的可访问性入门最小改动（添加 AutomationProperties.Name、自定义 FocusVisualStyle）。
4. **P1-4 / P1-5**：键盘导航（`Ctrl+数字`、`Esc` 关闭）。
5. **P2-1 ~ P2-8**：中优先级打磨，建议在一个迭代内统一处理（倒影、ComboBox/Slider 重写、二次确认）。
6. **P3** 系列作为下个迭代的 polish 项。

---

> 报告不修改任何源文件；如需将本报告拆分为单条 issue / 单个 PR 任务，可按 §6 表格逐行迁移。
