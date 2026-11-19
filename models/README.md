# 模型规范与替换说明 (`crack_seg.onnx`)

`models/crack_seg.onnx` 为预训练裂缝语义分割模型（基于 MATLAB DeepLabv3+ / ResNet-18 导出，默认输入尺寸 `512×512×3`）。

## 路径查找顺序

程序启动时，`ImgCodec.ResolveAsset("crack_seg.onnx")` 按以下顺序查找模型文件：
1. 可执行文件所在目录（`AppContext.BaseDirectory`）；
2. 当前工作目录（`Directory.GetCurrentDirectory()`）及其 `models/` 子目录；
3. 从可执行文件目录向上逐级回溯 6 层父目录及其 `models/` 子目录。

## 模型输入输出规范

`OnnxSegmenter` 在加载时读取 ONNX 元数据，支持替换为 PyTorch / TensorFlow / MATLAB / Ultralytics 导出的语义分割模型（U-Net、SegFormer、DeepLabV3+、HRNet、BiSeNet 等）：

* **输入张量布局**：`NCHW` $(B, 3, H, W)$ 或 `NHWC` $(B, H, W, 3)$。
* **输入数据类型与范围**：`float32`，RGB 通道顺序，像素值归一化至 $[0.0, 1.0]$。
* **Batch 维度**：若第 0 维为动态轴（`-1`），切片推理默认使用 `Batch=4`；若算子不支持多 Batch，捕获异常后回退至 `Batch=1`。
* **输出张量格式**：
  * **单通道**：$(B, 1, H, W)$、$(B, H, W, 1)$ 或 $(B, H, W)$；当输出值超出 $[0, 1]$ 时自动执行 `Sigmoid`。
  * **双通道**：$(B, 2, H, W)$ 或 $(B, H, W, 2)$，通道 `0` 为背景、通道 `1` 为前景；当双通道和偏离 `1.0` 时自动执行 `Softmax`。

替换模型时，将新模型命名为 `crack_seg.onnx` 并覆盖 `models/crack_seg.onnx` 或置于可执行文件同级目录。
