# 训练脚本说明 (`training/`)

本目录包含 `crack_seg.onnx` 的 MATLAB 数据集转换脚本、训练导出脚本及示例数据集。

## 文件说明

* `convert_poly_to_mask.m`：将 YOLOv8 多边形标注 `.txt` 或二值掩码图像转换为配对的单通道二值掩码（`.png`，`0` = 背景，`255` = 裂缝）。
* `training.m`：构建 `deeplabv3plus`（ResNet-18 骨干）或 `unetLayers` 网络，执行训练、测试集 `IoU` / `BFScore` 评估，并剥离损失层导出 `crack_seg.onnx`。
* `dataset/`：Roboflow Concrete Surface Crack Detection 数据集（CC BY 4.0，共 1,299 张图像与 YOLOv8 标注）。

## 运行步骤

1. 在 MATLAB 中将工作目录切换至 `training/`。
2. 修改 `convert_poly_to_mask.m` 中的 `imgDir` 与 `annoDir`（例如指向 `dataset/train/images` 与 `dataset/train/labels`），运行脚本生成 `dataset_seg/`。
3. 运行 `training.m`，训练完成后在当前目录生成 `crack_seg.onnx`。
4. 将 `crack_seg.onnx` 复制至 `../models/crack_seg.onnx`。


