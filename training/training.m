
clear; clc;

% 配置训练参数与网络架构

MODEL_TYPE = 'deeplabv3plus';
inputSize  = [512, 512, 3];
dataRoot   = 'dataset_seg';
imgDir     = 'dataset_seg/images';
maskDir    = 'dataset_seg/masks';

classNames = ["background", "crack"];
labelIDs   = [0, 255];

if exist(fullfile(dataRoot, 'train', 'images'), 'dir') && exist(fullfile(dataRoot, 'train', 'masks'), 'dir')
    imdsTrain = imageDatastore(fullfile(dataRoot, 'train', 'images'));
    pxdsTrain = pixelLabelDatastore(fullfile(dataRoot, 'train', 'masks'), classNames, labelIDs);
    if numel(imdsTrain.Files) ~= numel(pxdsTrain.Files)
        error('训练集图像数量 (%d) 与掩码数量 (%d) 不一致，请重新运行 convert_poly_to_mask.m！', ...
            numel(imdsTrain.Files), numel(pxdsTrain.Files));
    end

    if exist(fullfile(dataRoot, 'valid', 'images'), 'dir') && exist(fullfile(dataRoot, 'valid', 'masks'), 'dir')
        imdsVal = imageDatastore(fullfile(dataRoot, 'valid', 'images'));
        pxdsVal = pixelLabelDatastore(fullfile(dataRoot, 'valid', 'masks'), classNames, labelIDs);
        if numel(imdsVal.Files) ~= numel(pxdsVal.Files)
            error('验证集图像数量 (%d) 与掩码数量 (%d) 不一致！', numel(imdsVal.Files), numel(pxdsVal.Files));
        end
    else
        rng(42);
        nTr = numel(imdsTrain.Files);
        idx = randperm(nTr);
        nVal = max(1, round(0.10 * nTr));
        imdsVal   = subset(imdsTrain, idx(1:nVal));   pxdsVal   = subset(pxdsTrain, idx(1:nVal));
        imdsTrain = subset(imdsTrain, idx(nVal+1:end)); pxdsTrain = subset(pxdsTrain, idx(nVal+1:end));
    end

    if exist(fullfile(dataRoot, 'test', 'images'), 'dir') && exist(fullfile(dataRoot, 'test', 'masks'), 'dir')
        imdsTest = imageDatastore(fullfile(dataRoot, 'test', 'images'));
        pxdsTest = pixelLabelDatastore(fullfile(dataRoot, 'test', 'masks'), classNames, labelIDs);
        if numel(imdsTest.Files) ~= numel(pxdsTest.Files)
            error('测试集图像数量 (%d) 与掩码数量 (%d) 不一致！', numel(imdsTest.Files), numel(pxdsTest.Files));
        end
    else
        imdsTest = []; pxdsTest = [];
    end
elseif exist(imgDir, 'dir') && exist(maskDir, 'dir')
    imds = imageDatastore(imgDir);
    pxds = pixelLabelDatastore(maskDir, classNames, labelIDs);

    if numel(imds.Files) ~= numel(pxds.Files)
        error('图像数量 (%d) 与掩码数量 (%d) 不一致，请重新运行 convert_poly_to_mask.m！', ...
            numel(imds.Files), numel(pxds.Files));
    end

    % 按 80% : 10% : 10% 划分训练集、验证集与测试集
    rng(42);
    numImages = numel(imds.Files);
    shuffledIdx = randperm(numImages);

    numTrain = round(0.80 * numImages);
    numVal   = round(0.10 * numImages);

    trainIdx = shuffledIdx(1 : numTrain);
    valIdx   = shuffledIdx(numTrain + 1 : numTrain + numVal);
    testIdx  = shuffledIdx(numTrain + numVal + 1 : end);

    imdsTrain = subset(imds, trainIdx); pxdsTrain = subset(pxds, trainIdx);
    imdsVal   = subset(imds, valIdx);   pxdsVal   = subset(pxds, valIdx);
    imdsTest  = subset(imds, testIdx);  pxdsTest  = subset(pxds, testIdx);
else
    error('未找到 dataset_seg/train 或 dataset_seg/images，请先运行 convert_poly_to_mask.m！');
end

numTest = 0;
if ~isempty(imdsTest), numTest = numel(imdsTest.Files); end
fprintf('数据集划分：训练集 %d 张 | 验证集 %d 张 | 测试集 %d 张\n', ...
    numel(imdsTrain.Files), numel(imdsVal.Files), numTest);


tbl = countEachLabel(pxdsTrain);
frequency = tbl.PixelCount ./ sum(tbl.PixelCount);
classWeights = median(frequency) ./ max(frequency, 1e-6);
classWeights = min(classWeights, 15.0);
fprintf('类别像素占比: background=%.2f%%, crack=%.2f%% | 类别权重: [%.2f, %.2f]\n', ...
    frequency(1)*100, frequency(2)*100, classWeights(1), classWeights(2));


numClasses = numel(classNames);
if strcmpi(MODEL_TYPE, 'deeplabv3plus')
    try
        lgraph = deeplabv3plusLayers(inputSize, numClasses, 'resnet18');
    catch
        warning('未检测到 ResNet-18 支持包，自动切换为 unetLayers 架构。');
        lgraph = unetLayers(inputSize, numClasses, 'EncoderDepth', 4);
    end
else
    lgraph = unetLayers(inputSize, numClasses, 'EncoderDepth', 4);
end


inLayerName = lgraph.Layers(1).Name;
newInLayer  = imageInputLayer(inputSize, 'Name', inLayerName, 'Normalization', 'none');
lgraph = replaceLayer(lgraph, inLayerName, newInLayer);

% 替换末尾像素分类层以注入类别平衡权重
pxLayer = pixelClassificationLayer('Name', 'pixelLabels', 'Classes', tbl.Name, 'ClassWeights', classWeights);
oldPxLayerName = lgraph.Layers(end).Name;
lgraph = replaceLayer(lgraph, oldPxLayerName, pxLayer);


trainDS = combine(imdsTrain, pxdsTrain);
valDS   = combine(imdsVal,   pxdsVal);

augmentedTrainDS  = transform(trainDS, @(data)augmentSegData(data, inputSize));
preprocessedValDS = transform(valDS,   @(data)preprocessSegData(data, inputSize));


miniBatchSize = 8; % 若 GPU 显存 < 6GB，可调小为 4
valFrequency  = max(1, floor(numel(imdsTrain.Files) / miniBatchSize));

options = trainingOptions('adam', ...
    'InitialLearnRate', 1e-4, ...
    'LearnRateSchedule', 'piecewise', ...
    'LearnRateDropFactor', 0.2, ...
    'LearnRateDropPeriod', 20, ...
    'L2Regularization', 1e-4, ...
    'MiniBatchSize', miniBatchSize, ...
    'MaxEpochs', 45, ...
    'Shuffle', 'every-epoch', ...
    'ValidationData', preprocessedValDS, ...
    'ValidationFrequency', valFrequency, ...
    'ExecutionEnvironment', 'auto', ...
    'Plots', 'training-progress', ...
    'Verbose', true, ...
    'VerboseFrequency', 25);

[net, info] = trainNetwork(augmentedTrainDS, lgraph, options);
save('trained_crack_seg.mat', 'net', 'inputSize');

if ~isempty(imdsTest) && ~isempty(imdsTest.Files)
    tempPredDir = fullfile(tempdir, 'crack_seg_preds');
    if exist(tempPredDir, 'dir'), rmdir(tempPredDir, 's'); end
    mkdir(tempPredDir);

    testImgDS = transform(imdsTest, @(I)im2single(imresize(ensureRGB(I), inputSize(1:2))));
    pxdsResults = semanticseg(testImgDS, net, 'MiniBatchSize', miniBatchSize, 'WriteLocation', tempPredDir, 'Verbose', false);

    tempGtDir = fullfile(tempdir, 'crack_seg_gt');
    if exist(tempGtDir, 'dir'), rmdir(tempGtDir, 's'); end
    mkdir(tempGtDir);

    gtFilesResized = cell(numel(pxdsTest.Files), 1);
    for k = 1:numel(pxdsTest.Files)
        gtMask = imread(pxdsTest.Files{k});
        gtMask = imresize(gtMask, inputSize(1:2), 'nearest');
        outGtPath = fullfile(tempGtDir, sprintf('gt_%05d.png', k));
        imwrite(gtMask, outGtPath);
        gtFilesResized{k} = outGtPath;
    end
    pxdsGtResized = pixelLabelDatastore(gtFilesResized, classNames, labelIDs);

    metrics = evaluateSemanticSegmentation(pxdsResults, pxdsGtResized, 'Verbose', false);
    crackIoU = metrics.ClassMetrics.IoU("crack");
    crackBF  = metrics.ClassMetrics.MeanBFScore("crack");
    fprintf('测试集评估完成！裂缝类别 IoU: %.2f%% | 边界 F1 (BFScore): %.2f%%\n', ...
        crackIoU * 100, crackBF * 100);
end

lgraphExport = layerGraph(net);
lastLayerName = net.Layers(end).Name;
lgraphExport = removeLayers(lgraphExport, lastLayerName);
dlnetExport  = dlnetwork(lgraphExport);

exportONNXNetwork(dlnetExport, 'crack_seg.onnx', 'OpsetVersion', 12);
fprintf('\n======================================================\n');
fprintf('导出 crack_seg.onnx 成功！\n');
fprintf('网络输入尺寸: [%d, %d, 3] (范围 0~1)\n', inputSize(1), inputSize(2));
fprintf('网络输出类别: 通道 0=background, 通道 1=crack 概率\n');
fprintf('======================================================\n');

function I = ensureRGB(I)
    if size(I, 3) == 1
        I = repmat(I, [1, 1, 3]);
    elseif size(I, 3) > 3
        I = I(:, :, 1:3);
    end
end

function out = augmentSegData(data, targetSize)
    if istable(data)
        data = table2cell(data);
    end
    
    numRows = size(data, 1);
    out = cell(numRows, 2);
    
    for i = 1:numRows
        I = ensureRGB(data{i, 1});
        C = data{i, 2};
        if size(C, 3) > 1
            C = C(:, :, 1); 
        end

        % 颜色抖动增强
        I = jitterColorHSV(I, 'Contrast', 0.25, 'Hue', 0.04, 'Saturation', 0.15, 'Brightness', 0.20);

        % 随机仿射变换
        tform = randomAffine2d('XReflection', true, 'YReflection', true, ...
            'Rotation', [-25, 25], 'Scale', [0.85, 1.15]);
        rout = affineOutputView(size(I), tform, 'BoundsStyle', 'centerOutput');
        I = imwarp(I, tform, 'OutputView', rout, 'Interp', 'linear', 'FillValues', 128);
        
        % 标签变换并填充越界区域为背景
        C = imwarp(C, tform, 'OutputView', rout, 'Interp', 'nearest');
        cats = categories(C);
        C(isundefined(C)) = cats{1};

        I = im2single(imresize(I, targetSize(1:2), 'bilinear'));
        C = imresize(C, targetSize(1:2), 'nearest');
        
        out{i, 1} = I;
        out{i, 2} = C;
    end
end

function out = preprocessSegData(data, targetSize)
    if istable(data)
        data = table2cell(data);
    end
    
    numRows = size(data, 1);
    out = cell(numRows, 2);
    
    for i = 1:numRows
        I = ensureRGB(data{i, 1});
        C = data{i, 2};
        if size(C, 3) > 1
            C = C(:, :, 1); 
        end
        
        cats = categories(C);
        C(isundefined(C)) = cats{1};

        I = im2single(imresize(I, targetSize(1:2), 'bilinear'));
        C = imresize(C, targetSize(1:2), 'nearest');
        
        out{i, 1} = I;
        out{i, 2} = C;
    end
end