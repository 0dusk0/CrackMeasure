%% convert_poly_to_mask.m - 将 YOLOv8 多边形(.txt) 或 外部掩码图 统一转换为 1对1 像素级二值标签 PNG
clear; clc;

% 模式选择：
%   'poly_txt' - 读取 YOLOv8 多边形顶点 .txt 标签并生成二值掩码 PNG (推荐)
%   'mask'     - 规整已有的黑白二值掩码图 (兼容 logical / {0,1} / {0,255} / RGB 掩码)
MODE = 'poly_txt';

% 仅保留的目标类别 ID（设为 [] 则保留全部类别）
TARGET_CLASS_IDS = 0;

% 是否保留无裂缝的纯背景负样本图像（true = 生成全黑掩码参与训练，降低误报率）
KEEP_EMPTY_IMAGES = true;

% 若 dataset 下存在 train/valid/test 官方划分目录，则遍历各划分并保留划分结果
USE_DATASET_SPLITS = true;
datasetRoot = 'dataset';
outRootDir  = 'dataset_seg';

imgDir  = 'dataset/train/images';  % 单目录模式：原始裂缝图片文件夹
annoDir = 'dataset/train/labels';  % 单目录模式：YOLOv8 多边形 .txt 文件夹（或已有掩码文件夹）
outImgDir  = 'dataset_seg/images';
outMaskDir = 'dataset_seg/masks';

if exist(outRootDir, 'dir'), rmdir(outRootDir, 's'); end

if USE_DATASET_SPLITS && exist(fullfile(datasetRoot, 'train', 'images'), 'dir')
    splits = {'train', 'valid', 'test'};
    totalValid = 0; totalPoly = 0;
    for s = 1:numel(splits)
        sp = splits{s};
        spImgDir  = fullfile(datasetRoot, sp, 'images');
        spAnnoDir = fullfile(datasetRoot, sp, 'labels');
        if ~exist(spImgDir, 'dir'), continue; end
        [vc, pc] = convertSplit(spImgDir, spAnnoDir, fullfile(outRootDir, sp, 'images'), fullfile(outRootDir, sp, 'masks'), MODE, TARGET_CLASS_IDS, KEEP_EMPTY_IMAGES);
        fprintf('[%s] 转换完成：%d 对图像与掩码 PNG（多边形 %d 个）。\n', sp, vc, pc);
        totalValid = totalValid + vc;
        totalPoly  = totalPoly + pc;
    end
    fprintf('全部划分转换完成！共生成 %d 对「图像 + 二值掩码 PNG」（解析多边形实例 %d 个）。\n', totalValid, totalPoly);
    fprintf('输出根目录: %s/{train,valid,test}\n', outRootDir);
else
    [validCount, polyCount] = convertSplit(imgDir, annoDir, outImgDir, outMaskDir, MODE, TARGET_CLASS_IDS, KEEP_EMPTY_IMAGES);
    fprintf('数据集转换完成！共生成 %d 对严格对齐的「图像 + 二值掩码 PNG」（解析多边形实例 %d 个）。\n', validCount, polyCount);
    fprintf('图像目录: %s\n掩码目录: %s\n', outImgDir, outMaskDir);
end

function [validCount, polyCount] = convertSplit(imgDir, annoDir, outImgDir, outMaskDir, MODE, TARGET_CLASS_IDS, KEEP_EMPTY_IMAGES)
if exist(outImgDir, 'dir'),  rmdir(outImgDir, 's');  end
if exist(outMaskDir, 'dir'), rmdir(outMaskDir, 's'); end
mkdir(outImgDir);
mkdir(outMaskDir);

imgFiles = [dir(fullfile(imgDir, '*.jpg')); dir(fullfile(imgDir, '*.png')); dir(fullfile(imgDir, '*.jpeg'))];
validCount = 0;
polyCount  = 0;

for i = 1:numel(imgFiles)
    imgPath = fullfile(imgFiles(i).folder, imgFiles(i).name);
    [~, name, ext] = fileparts(imgFiles(i).name);
    info = imfinfo(imgPath);
    W = info.Width;
    H = info.Height;
    bwMask = false(H, W);

    if strcmpi(MODE, 'poly_txt')
        txtPath = fullfile(annoDir, [name, '.txt']);
        if isfile(txtPath)
            fid = fopen(txtPath, 'r');
            while ~feof(fid)
                rawLine = fgetl(fid);
                if ~ischar(rawLine), break; end

                lineStr = strtrim(rawLine);
                if isempty(lineStr), continue; end

                nums = str2double(strsplit(lineStr));
                nums = nums(~isnan(nums));
                if isempty(nums), continue; end

                clsId = nums(1);
                if ~isempty(TARGET_CLASS_IDS) && ~ismember(clsId, TARGET_CLASS_IDS)
                    continue;
                end

                % YOLOv8 多边形格式 [class_id, x1, y1, x2, y2, ..., xn, yn]
                if numel(nums) >= 7 && mod(numel(nums) - 1, 2) == 0
                    coords = nums(2:end);
                    xs = max(1, min(W, coords(1:2:end) * W));
                    ys = max(1, min(H, coords(2:2:end) * H));
                    bwMask = bwMask | poly2mask(xs, ys, H, W);
                    polyCount = polyCount + 1;

                % 兼容标准 5 列 YOLO 矩形框 [class_id, cx, cy, w, h]
                elseif numel(nums) == 5
                    cx = nums(2) * W;
                    cy = nums(3) * H;
                    bw = nums(4) * W;
                    bh = nums(5) * H;
                    x1 = max(1, min(W, round(cx - bw / 2)));
                    x2 = max(1, min(W, round(cx + bw / 2)));
                    y1 = max(1, min(H, round(cy - bh / 2)));
                    y2 = max(1, min(H, round(cy + bh / 2)));
                    bwMask(y1:y2, x1:x2) = true;
                else
                    warning('文件 %s 存在列数异常的行（共 %d 列），已跳过该行。', txtPath, numel(nums));
                end
            end
            fclose(fid);
        elseif ~KEEP_EMPTY_IMAGES
            continue;
        end

    elseif strcmpi(MODE, 'mask')
        maskCandidates = [dir(fullfile(annoDir, [name, '.png'])); ...
                          dir(fullfile(annoDir, [name, '.jpg'])); ...
                          dir(fullfile(annoDir, [name, '.bmp']))];
        if isempty(maskCandidates)
            if ~KEEP_EMPTY_IMAGES, continue; end
        else
            maskPath = fullfile(maskCandidates(1).folder, maskCandidates(1).name);
            [rawMask, map] = imread(maskPath);
            if ~isempty(map)
                rawMask = ind2gray(rawMask, map);
            elseif size(rawMask, 3) >= 3
                rawMask = rgb2gray(rawMask(:, :, 1:3));
            elseif size(rawMask, 3) == 2
                rawMask = rawMask(:, :, 1);
            end
            if islogical(rawMask)
                bwMask = rawMask;
            else
                maxVal = double(max(rawMask(:)));
                if maxVal <= 1
                    bwMask = rawMask > 0;
                else
                    bwMask = double(rawMask) > (0.5 * maxVal);
                end
            end
            if size(bwMask, 1) ~= H || size(bwMask, 2) ~= W
                bwMask = imresize(bwMask, [H, W], 'nearest');
            end
        end
    end

    if ~any(bwMask(:)) && ~KEEP_EMPTY_IMAGES
        continue;
    end

    validCount = validCount + 1;
    % 使用唯一序号前缀配对命名
    pairPrefix = sprintf('%06d_%s', validCount, name);
    copyfile(imgPath, fullfile(outImgDir, [pairPrefix, ext]));
    imwrite(uint8(bwMask) * 255, fullfile(outMaskDir, [pairPrefix, '.png']));
end
end