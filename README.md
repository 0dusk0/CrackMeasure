<div align="center">

# CrackMeasurePro

### 表面裂缝分割与测量系统

![.NET](https://img.shields.io/badge/.NET-8.0%2B%20WinForms-512BD4?logo=dotnet)
![OpenCvSharp4](https://img.shields.io/badge/OpenCvSharp4-4.13-green?logo=opencv)
![ONNX Runtime](https://img.shields.io/badge/ONNX%20Runtime-DirectML%20%7C%20CPU-005CED?logo=onnx)
![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?logo=windows)
![License](https://img.shields.io/badge/License-MIT-yellow)
  
支持 DirectML 显卡推理 · 256 态查找表骨架细化 · 亚像素法向射线寻边测宽 · 悬停正交游标卡尺 · 交互式掩码修补 · CAD (`.dxf`) / Excel (`.xlsx`) / Word (`.doc`) / HTML 报告导出

</div>

---

## 效果展示

### 1. 裂缝编号标注
左侧原始图像。

![双图对比与编号标注](<docs/images/屏幕截图 2026-11-13 140935.png>)

### 2.几何测量参数与 $W_{\max}$ 靶标定位
切换至**完整测量参数**标签模式后，图面直接标注每条裂缝的走向分类、骨架长度 $L$、平均宽度 $W$ 与最大宽度 $W_{\max}$，并以 `⊕` 靶标标出屏蔽分叉区后的最大宽度截面位置。

![完整测量参数与Wmax靶标标注](<docs/images/屏幕截图 2026-11-13 141119.png>)

### 3. 二值分割掩码视图与交互式修补
在**掩码视图**下可查看二值化与形态学桥接后的分割掩码，配合顶部工具栏的**补画裂缝**与**擦除伪影**笔刷工具，可直接涂抹修补断缝或剔除背景干扰并重算几何参数。

![二值分割掩码视图](<docs/images/屏幕截图 2026-11-13 141135.png>)

### 4. 比例尺标定与自由游标卡尺测距
通过**标定比例尺**工具在标尺或参照物两端取点即可完成 `mm/px` 物理尺寸换算；使用**自由卡尺**可在画布任意两点拉线实时测量像素距离与物理长度（`mm`）。

![自由游标卡尺与比例尺测量](<docs/images/屏幕截图 2026-11-13 141544.png>)

### 5. 图文检测报告导出 (HTML / PDF / Word)
支持一键生成包含工程信息、超限预警结论、原图与测量标注对比图以及单缝几何参数明细表的检测报告，可直接打印或转存为 PDF / Word 文档。

![图文检测报告导出预览](<docs/images/屏幕截图 2026-11-13 141240.png>)

---

## 功能说明

1. **DirectML GPU 推理**
   * 基于 `Microsoft.ML.OnnxRuntime.DirectML`，在 Windows 下直接调用 NVIDIA / AMD / Intel 显卡推理，无需安装 CUDA / cuDNN；无可用 GPU 时回退至多线程 CPU。
   * 支持 512×512 二维 Hanning 窗加权重叠切片推理（Tiled Inference），消除大图切片边界拼接伪影。

2. **亚像素法向截面测宽**
   * **256 态 LUT 细化**：预计算 Zhang-Suen 细化与 90° 阶梯角点消除查找表，经 3 轮短毛刺修剪（Spur Pruning，$L \le 6\text{ px}$）提取单像素中心骨架。
   * **法向双线性插值寻边**：由 `9×9` 局部骨架二阶中心矩求解切线与正交法向量，使用 `Parallel.For` 沿正负法向在连续概率场上作双线性插值寻边，避免离散距离变换（EDT）在偶像素宽度上的 `1 px` 量化误差。
   * **分叉区极值屏蔽**：统计单条裂缝最大宽度 $W_{\max}$ 时屏蔽分叉交汇区，避免 T 型或十字分叉处面积膨胀导致 $W_{\max}$ 偏大，并以 `⊕` 标记最大宽度截面位置。

3. **画布交互工具**
   * **悬停正交卡尺探针**：光标靠近裂缝时吸附最近骨架点，绘制青色正交截面卡尺，显示局部亚像素缝宽、走向与编号；单击裂缝与右侧明细表选中。
   * **比例尺标定**：在图像参照物两端拖拽或点击两点，输入物理长度（`mm`），换算单张或全队列比例尺（`mm/px`）。
   * **自由卡尺**：在图上任意两点拉线测量像素距离与物理距离，不修改全局比例尺。
   * **画笔与橡皮擦**：涂抹修改分割掩码以补连断缝或擦除伪影，松开鼠标后重算骨架长度、宽度与分叉数；每张图像维护 12 步 `Ctrl+Z` 撤销栈。

4. **走向判别与防重叠标注排版**
   * 闭式求解 $2\times 2$ 协方差矩阵特征值与主倾角 $\theta \in [0^\circ, 180^\circ)$，按倾角与各向异性比分类为：水平横向、竖直纵向、斜向裂缝、网状/龟裂。
   * 基于碰撞检测排布带引线的矢量标签，支持“精简编号 / 完整测量参数 / 无文字”三种视图。

5. **数据与报告导出**
   * **AutoCAD 矢量图 (`.dxf`)**：输出 `AC1009 (R12)` 标准 DXF 文件，包含 `IMAGE_FRAME`（图框）、`CRACK_CONTOUR`（闭合轮廓）、`CRACK_SKELETON`（法向测宽截面线）、`CRACK_WMAX`（最大宽度靶标与标注文字）四个图层。
   * **Excel 表格 (`.xlsx`)**：导出《图像汇总》与《裂缝明细》两个工作表。
   * **检测报告 (`.html` / `.doc`)**：生成包含工程元数据、阈值预警统计、原图/结果对比图及明细表的 HTML 报告或 Word (`.doc` MHTML) 文档。

---

## 解析步骤

```text
输入图像 (BGR)
  │
  ├─► [1. 推理引擎 OnnxSegmenter]
  │     ├─ 大图模式：512×512 重叠切片 (Overlap=25%) + 2D Hanning 窗加权融合
  │     ├─ 小图模式：Letterbox 等比缩放推理
  │     └─ 解析单/双通道 Logits / Sigmoid / Softmax → 输出 uint8 概率图
  │
  ├─► [2. 形态学桥接与连通域分解 PostProcessor]
  │     ├─ 概率阈值二值化 + 椭圆结构元闭运算
  │     └─ 8-连通域标记 + 最小面积过滤
  │
  ├─► [3. 拓扑骨架提取 SkeletonEngine]
  │     ├─ 256-LUT Zhang-Suen 细化 + 90° 阶梯角点剔除
  │     ├─ 3 轮短毛刺修剪 (Spur Pruning, L ≤ 6px)
  │     └─ 交叉数 CN ≥ 3 分叉点提取与 3×3 膨胀连通域质心聚类
  │
  └─► [4. 几何测量与走向分析]
        ├─ 长度 L：Freeman 链码正交步长 (1.0) + 对角步长 (√2)
        ├─ 宽度 W：9×9 局部二阶矩法线估计 + 正负法向双线性插值射线寻边
        ├─ 极值 Wmax：屏蔽分叉交汇区 → 定位最大缝宽截面中心 ⊕
        └─ 走向分类：2×2 协方差矩阵特征分解 → 主倾角 (°) 与类别
```

---

## 仓库结构

```text
CrackMeasurePro/
├── LICENSE                          # MIT 协议
├── README.md                        # 说明文档
├── CrackMeasurePro.sln              # vs
├── docs/
│   └── TRAINING_GUIDE.md            # MATLAB 语义分割模型训练与 ONNX 导出说明
├── models/
│   ├── README.md                    # ONNX 模型输入输出规范与替换说明
│   └── crack_seg.onnx               # 预训练裂缝分割模型 
├── src/
│   └── CrackMeasurePro/
│       ├── CrackMeasurePro.csproj   # .NET WinForms 工程配置与 NuGet 依赖
│       ├── CrackCore.cs             # 推理、骨架细化、测宽与报告导出
│       └── MainForm.cs              # WinForms 主界面、画布渲染、交互工具与多线程队列
└── training/
    ├── README.md                    # 训练脚本目录说明
    ├── convert_poly_to_mask.m       # YOLOv8 多边形 .txt / 外部掩码 → 二值 PNG 转换脚本
    ├── training.m                   # MATLAB DeepLabv3+ / U-Net 训练、评估与 ONNX 导出脚本
    └── dataset/                     # 示例数据集 (Roboflow CC BY 4.0, 1299 张)
```

---

## 构建与运行

### 1. 环境要求

* **操作系统**：Windows 10 / 11 (x64)
* **SDK**：[.NET SDK](https://dotnet.microsoft.com/download)（工程默认 `<TargetFramework>net11.0-windows</TargetFramework>`；若使用 .NET 8.0 / 9.0 / 10.0 SDK，将 `src/CrackMeasurePro/CrackMeasurePro.csproj` 中的 `TargetFramework` 改为对应的 `net8.0-windows` / `net9.0-windows` / `net10.0-windows`）

### 2. 编译与运行

```powershell
git clone https://github.com/0dusk0/CrackMeasure1.git
cd CrackMeasure1

dotnet run --project src/CrackMeasurePro -c Release
```

也可使用 Visual Studio 2022+ 或 JetBrains Rider 打开 `CrackMeasurePro.sln` 编译运行（构建时自动将 `models/crack_seg.onnx` 复制至输出目录）。

### 3. 发布独立可执行程序

自包含目录（无需安装 .NET ）：

```powershell
dotnet publish src/CrackMeasurePro/CrackMeasurePro.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -o publish
```

单文件可执行程序（分发时需保留同目录下的 `crack_seg.onnx`）：

```powershell
dotnet publish src/CrackMeasurePro/CrackMeasurePro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish_single
```

---

## 交互操作与快捷键

| 操作 / 快捷键 | 说明 |
| :--- | :--- |
| **鼠标滚轮** | 以光标为中心缩放画布（`5%` ~ `3000%`），支持单视图及双图同步对比视图 |
| **鼠标左键拖拽**（漫游模式） | 平移画布视口 |
| **鼠标悬停裂缝**（漫游模式） | 吸附最近骨架剖面，绘制正交游标卡尺并显示局部宽度 |
| **鼠标左键单击裂缝** | 选中裂缝并定位右侧明细表对应行 |
| **鼠标左键双击画布** | 自适应窗口居中显示（Fit to Window） |
| **标定比例尺** | 在参照物两端拖拽或点击两点，输入物理长度（`mm`）换算 `mm/px` |
| **自由卡尺** | 在任意两点拉线测量像素长度与物理距离（`mm`） |
| **补画裂缝 / 擦除伪影** | 涂抹修改分割掩码，松开左键后重算拓扑与几何指标 |
| **`[` / `]`** | 减小 / 增大画笔与橡皮擦半径（`1 px` ~ `35 px`） |
| **`Ctrl + Z`** | 撤销上一步画笔或橡皮擦修改（上限 12 步） |
| **鼠标右键 / `ESC`** | 取消当前工具，返回【漫游/探针】模式 |

---

## 模型训练与替换

* 模型输入输出规范与替换方法见 [`models/README.md`](models/README.md)。
* MATLAB 数据转换、网络训练与 ONNX 导出流程见 [`docs/TRAINING_GUIDE.md`](docs/TRAINING_GUIDE.md) 与 [`training/README.md`](training/README.md)。

---

## 数据集说明

`training/dataset/` 包含用于验证训练流程的公开数据集：
* **名称**：Concrete Surface Crack Detection (Roboflow Universe, v3)
* **协议**：[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
* **来源**：[Roboflow Universe](https://universe.roboflow.com/vijayalakshmi-2yshx/concrete-surface-crack-detection-using-yolov5-model/dataset/3)

---

## 开源协议

[MIT License](LICENSE)

---


