#  UI设计风格文档

## 一、设计理念与整体风格

### 1.1 设计定位

本上位机系统采用**现代工业软件设计风格**，融合了Material Design的设计理念与工业控制软件的专业性，整体呈现：

- **专业可靠**：适合工业环境的严谨设计
- **现代简洁**：扁平化设计语言，去除冗余装饰
- **用户友好**：清晰的视觉层次和直观的交互反馈
- **性能优先**：高效的渲染机制保证实时数据流畅显示

### 1.2 核心设计原则

- **KISS原则**：界面布局简洁直观，降低学习成本
- **视觉一致性**：统一的色彩、字体、圆角、阴影规范
- **即时反馈**：所有交互都有明确的视觉状态变化
- **信息密度平衡**：在有限空间内合理组织大量参数

------

## 二、色彩系统设计

### 2.1 主色调方案（蓝色系）



```csharp
// 核心蓝色定义
PrimaryColor   #1976D2  // 主蓝色 - 用于主要操作、标题
AccentColor    #2196F3  // 强调蓝 - 用于悬停状态、次要元素
DarkColor      #0D47A1  // 深蓝色 - 用于按下状态、强调
LightColor     #BBDEFB  // 浅蓝色 - 用于背景高亮
```

**应用场景**：

- 主按钮背景、标题栏
- Tab选中指示器
- 图表主曲线
- 重要数据高亮

### 2.2 功能色彩体系



```csharp
SuccessColor   #4CAF50  // 成功绿 - 启动、打开、成功状态
WarningColor   #FF9800  // 警告橙 - 暂停、清空操作
DangerColor    #F44336  // 危险红 - 停止、关闭、删除操作
```

**语义化应用**：

- 绿色按钮 → "打开串口"、"建立通讯"、"开始记录"
- 红色按钮 → "关闭串口"、"断开通讯"、"停止记录"、"紧急停止"
- 橙色按钮 → "暂停"、"清空数据"

### 2.3 中性色彩系统



```csharp
BackgroundColor    #F5F5F5  // 全局背景
CardBackgroundColor #FFFFFF  // 卡片/面板背景
BorderColor         #E0E0E0  // 边框颜色
TextColor          #333333  // 主要文本
```

### 2.4 图表配色方案



```csharp
ChartSeriesColor1  #1976D2  // 设备流量曲线
ChartSeriesColor2  #4CAF50  // 阿里卡特流量曲线
ChartSeriesColor3  #FF9800  // 辅助数据
ChartSeriesColor4  #F44336  // 警告阈值
```

------

## 三、组件设计规范

### 3.1 按钮设计系统

#### 标准按钮（StandardButtonStyle）



```xml
<!-- 视觉特征 -->
- 圆角: 5px
- 阴影: DropShadowEffect (BlurRadius=3, Opacity=0.2)
- 高度: 28-35px
- 内边距: 12,6
- 字体: 12px, SemiBold
```

**状态变化**：



```
正常状态 → PrimaryColor背景
悬停状态 → AccentColor背景 + 阴影加深
按下状态 → DarkColor背景 + 阴影减弱
禁用状态 → Opacity=0.5
```

#### 语义化按钮变体

- **StartButtonStyle**：绿色背景，用于启动/打开操作
- **StopButtonStyle**：红色背景，用于停止/关闭操作

### 3.2 输入控件设计

#### TextBox设计规范



```xml
- 高度: 28px (标准), 34px (标定参数页), 100px (阿里卡特流量设定)
- 圆角: 3px
- 边框: #E0E0E0
- 内边距: 6,4
- 字体: 12px
```

**焦点状态**：



```
默认 → #E0E0E0边框
焦点 → PrimaryColor边框
悬停 → AccentColor边框
```

#### ComboBox设计

- 自定义下拉箭头图标
- 圆角边框3px
- 下拉面板白色背景，底部圆角3px

### 3.3 容器与面板设计

#### ContentPanelStyle（卡片容器）



```xml
<Style TargetType="Border">
    - 背景: White
    - 边框: #EEEEEE, 1px
    - 圆角: 6px
    - 内边距: 12px
    - 阴影: Direction=315°, ShadowDepth=1, BlurRadius=6
</Style>
```

#### GroupBox设计



```xml
- 边框: #E8E8E8
- 背景: White
- 圆角: 10px
- 内边距: 16px
- 标题字体: 13px, SemiBold
- 阴影: BlurRadius=6, Opacity=0.08
```

### 3.4 TabControl现代设计

#### ModernTabItem样式



```xml
- 去除传统Tab外观
- 底部边框指示器（2px厚）
- 默认状态: 透明背景, 灰色文本(#666666)
- 选中状态: PrimaryColor底边框, PrimaryColor文本, SemiBold字体
- 悬停状态: AccentColor底边框和文本
- 内边距: 16,10
```

**视觉特点**：

- 极简设计，无背景色块
- 底部线条作为选中指示
- 平滑的颜色过渡动画

------

## 四、布局架构设计

### 4.1 整体网格布局



```
┌─────────────────────────────────────────────────────┐
│  顶部标题栏 (Height=50) - PrimaryColor背景           │
├──────────┬──────────────────────────────────────────┤
│  左侧栏  │  右侧Tab区域 (270px + *)                 │
│  270px   │  - 参数1                               │
│          │  - 常用2                               │
│  - 串口  │  - 参数3                               │
│  - 设备  │  - 参数4                               │
│  - 读取  │  - 参数5                               │
│          │  - 参数6                               │
├──────────┴──────────────────────────────────────────┤
│  底部状态栏 (Height=25) - DarkColor背景             │
└─────────────────────────────────────────────────────┘
```

### 4.2 左侧固定面板设计

- **宽度**: 270px固定宽度
- **组件**: 三个堆叠的ContentPanel
- **间距**: 10px垂直间距
- **功能**: 串口设置、设备参数、标定参数读取

### 4.3 右侧Tab区域设计

- **宽度**: 自适应填充剩余空间
- **边距**: 左侧10px间隔
- **背景**: 透明
- **内容**: 六个功能Tab页

### 4.4 响应式内边距系统



```csharp
DefaultMargin      = 4   // 小间距
DefaultPadding     = 6   // 内部填充
DefaultCornerRadius = 3   // 默认圆角
```

------

## 五、字体排版系统

### 5.1 字体族定义



```xml
主字体: "Microsoft YaHei UI, Segoe UI, Tahoma, sans-serif"
代码字体: "Consolas"
```

**字体选择理由**：

- Microsoft YaHei UI → 优秀的中文显示效果
- Segoe UI → 现代Windows系统字体
- Consolas → 等宽字体，适合数据和日志显示

### 5.2 字号层级体系



```
H1 - 标题栏:      18px, Bold
H2 - Group标题:   15px, Bold
H3 - 区域标题:    13px, SemiBold
正文 - 标签:      12px, Normal
正文 - 数据:      12px, SemiBold
小字 - 说明:      11px, Normal
```

### 5.3 文本色阶



```csharp
主要文本:  #333333  // Label、TextBox
次要文本:  #666666  // Tab默认状态
说明文本:  #999999  // 提示信息
标题文本:  #FFFFFF  // 标题栏、按钮文本
```

------

## 六、交互设计模式

### 6.1 状态反馈机制

#### 按钮状态机



```
[正常] --鼠标悬停→ [悬停]
[正常] --鼠标按下→ [按下]
[悬停] --鼠标移开→ [正常]
[按下] --鼠标释放→ [正常]
[任何状态] --禁用→ [禁用: 透明度50%]
```

#### 输入框焦点管理



```
[获得焦点] → 边框变蓝(PrimaryColor)
[失去焦点] → 边框恢复灰(#E0E0E0)
[悬停]     → 边框变浅蓝(AccentColor)
```

### 6.2 视觉反馈强化

- **阴影动态变化**：按下时阴影深度从1→0，模糊半径3→2
- **颜色渐变过渡**：所有状态变化使用平滑过渡
- **光标变化**：可点击元素使用Hand光标

### 6.3 加载与状态显示

- **串口状态按钮**：根据状态动态改变文本和颜色
  - "打开串口" → 绿色
  - "关闭串口" → 红色
- **性能监控显示**：实时FPS和内存使用
- **日志彩色编码**：红色错误、绿色成功、橙色警告

------

## 七、数据可视化设计

### 7.1 图表设计规范（LiveCharts）

#### CartesianChart配置



```xml
- 性能优化: CacheMode="BitmapCache"
- 动画速度: 0.5秒
- 交互性: Hoverable="True"
- 数据提示: 自定义ToolTip
```

#### 坐标轴设计



```xml
AxisY (瞬时流量):
  - 标题: "瞬时流量"
  - 网格线: #EEEEEE, 1px
  - 文本颜色: #555555

AxisX (时间):
  - 标题: "时间"
  - 网格线: #EEEEEE, 1px
  - 文本颜色: #555555
```

### 7.2 实时数据展示区域

#### 统计信息面板



```xml
<Border Background="#F5F5F5">
  <StackPanel>
    - TextBlock: "📊 流量统计：" (Bold, 13px)
    - TextBlock: 实时统计数据 (PrimaryColor)
  </StackPanel>
</Border>
```

#### 设备参数显示

- **字体**: SemiBold, 12px
- **对齐**: 右对齐
- **颜色编码**:
  - 瞬时流量 → PrimaryColor
  - 累计流量 → AccentColor
  - 流量设定 → SuccessColor

------

## 八、图标与视觉元素

### 8.1 图标使用策略

本系统主要使用**Unicode符号和Emoji**作为图标：

- 📊 统计图标
- ⚡ 性能图标
- ⏸ 暂停图标
- ✓ 状态图标
- 👁 显示图标

**优势**：

- 无需额外的图标资源文件
- 可直接在文本中使用
- 跨平台兼容性好

### 8.2 分隔与分组

- **边框分隔**：1px的#E0E0E0线条
- **间距分隔**：使用Margin而非线条
- **背景分隔**：交替背景色区分区域

------

## 九、特殊UI模式

### 9.1 密码输入区域



```xml
<PasswordBox 
  Name="tbx_SimulationPassword"
  Width="120"
  Height="28"
  VerticalContentAlignment="Center"/>
```

### 9.2 日志显示区域



```xml
<RichTextBox 
  Background="#FAFAFA"
  FontFamily="Consolas"
  FontSize="11"
  Padding="8"
  IsReadOnly="True"
  VerticalScrollBarVisibility="Auto">
```

**特点**：

- 等宽字体便于数据对齐
- 浅灰背景降低视觉疲劳
- 只读模式防止误操作
- 自动滚动条

### 9.3 数据记录区



```xml
<TextBox
  Name="tbx_recording"
  Height="320"
  Background="#FAFAFA"
  TextWrapping="Wrap"
  FontFamily="Consolas"
  FontSize="11"/>
```

------

## 十、设计资源总结

### 10.1 核心设计Token速查表

| 类别 | 属性         | 值                         | 应用场景     |
| ---- | ------------ | -------------------------- | ------------ |
| 色彩 | PrimaryColor | #1976D2                    | 主按钮、标题 |
| 色彩 | SuccessColor | #4CAF50                    | 启动操作     |
| 色彩 | DangerColor  | #F44336                    | 停止操作     |
| 圆角 | 按钮         | 5px                        | 操作按钮     |
| 圆角 | 面板         | 6px                        | 容器卡片     |
| 圆角 | 输入框       | 3px                        | 文本框       |
| 阴影 | 卡片         | BlurRadius=6, Opacity=0.08 | 面板阴影     |
| 阴影 | 按钮         | BlurRadius=3, Opacity=0.2  | 按钮阴影     |
| 字号 | 标题         | 18px Bold                  | 标题栏       |
| 字号 | 正文         | 12px Normal                | 普通文本     |
| 间距 | Margin       | 4px                        | 小间距       |
| 间距 | Padding      | 6px                        | 内部填充     |

### 10.2 设计系统文件结构



```
WPF-CS/MainWindow.xaml
├── Window.Resources (行20-403)
│   ├── 色彩定义 (行21-38)
│   ├── 基础数值定义 (行41-45)
│   ├── TabControl样式 (行47-90)
│   ├── 按钮样式 (行92-331)
│   ├── 容器样式 (行189-201)
│   ├── 控件样式 (行203-402)
│   └── GroupBox样式 (行333-370)
└── 主布局 (行405-1901)
    ├── 顶部标题栏 (行417-441)
    ├── 主内容区 (行443-1877)
    │   ├── 左侧面板 (行450-602)
    │   └── 右侧Tab (行604-1876)
    └── 底部状态栏 (行1879-1900)
```

------

## 十一、设计优势与特色

### 11.1 设计优势

1. **工业级可靠性**：色彩语义明确，操作反馈清晰
2. **信息密度优化**：在有限空间展示大量参数
3. **实时性能优先**：使用BitmapCache等优化技术
4. **可维护性强**：统一的样式系统，便于主题切换
5. **跨文化适配**：支持中英文双语界面

### 11.2 独特设计特色

- **双曲线对比显示**：同时显示Modbus和阿里卡特流量数据
- **性能监控可视化**：实时显示FPS和内存使用
- **状态驱动颜色**：按钮颜色根据功能语义动态变化
- **无衬线字体栈**：保证中文和数字的显示质量
- **现代化Tab设计**：底部线条指示器替代传统Tab

------

- 

------

## 附录：设计代码片段示例

### A. 标准按钮样式定义



```xml
<Style x:Key="StandardButtonStyle" TargetType="Button">
    <Setter Property="Background" Value="{StaticResource PrimaryColor}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Padding" Value="12,6"/>
    <Setter Property="Margin" Value="4"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}" 
                        CornerRadius="5"
                        BorderThickness="{TemplateBinding BorderThickness}" 
                        BorderBrush="{TemplateBinding BorderBrush}">
                    <Border.Effect>
                        <DropShadowEffect ShadowDepth="1" BlurRadius="3" 
                                        Opacity="0.2" Color="#40000000"/>
                    </Border.Effect>
                    <ContentPresenter HorizontalAlignment="Center" 
                                      VerticalAlignment="Center" 
                                      Margin="{TemplateBinding Padding}"/>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

### B. 现代Tab样式定义



```xml
<Style x:Key="ModernTabItem" TargetType="TabItem">
    <Setter Property="Padding" Value="16,10"/>
    <Setter Property="Margin" Value="0,0,5,0"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="#666666"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="TabItem">
                <Grid>
                    <Border Name="Border" 
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="0,0,0,2"
                            Margin="1,0,1,0">
                        <ContentPresenter x:Name="ContentSite"
                                        VerticalAlignment="Center"
                                        HorizontalAlignment="Center"
                                        ContentSource="Header"
                                        Margin="{TemplateBinding Padding}"/>
                    </Border>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsSelected" Value="True">
                        <Setter TargetName="Border" Property="BorderBrush" 
                                Value="{StaticResource PrimaryColor}"/>
                        <Setter Property="Foreground" 
                                Value="{StaticResource PrimaryColor}"/>
                        <Setter Property="FontWeight" Value="SemiBold"/>
                    </Trigger>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="Foreground" 
                                Value="{StaticResource AccentColor}"/>
                        <Setter TargetName="Border" Property="BorderBrush" 
                                Value="{StaticResource AccentColor}"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

------

**文档版本**：V1.0
 **最后更新**：2026-01-26
 **适用版本**：WL-Flow Meter Calibration V1.12
 **设计参考**：Material Design 3.0 + Windows 11 Fluent Design