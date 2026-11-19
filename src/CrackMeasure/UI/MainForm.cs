using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using GdiFont = System.Drawing.Font;
using GdiPen = System.Drawing.Pen;
using GdiPoint = System.Drawing.Point;
using GdiSize = System.Drawing.Size;

namespace CrackMeasure
{
    internal sealed class BufferedCanvas : Panel
    {
        public BufferedCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    public sealed class MainForm : Form
    {
        private const int MaxMemoryCache = 30;
        private const int MaxUndoSteps = 12;

        private readonly OnnxSegmenter _segmenter = new();
        private readonly Dictionary<string, QueueItem> _items = new();
        private readonly List<string> _itemOrder = new();
        private readonly LinkedList<string> _lruList = new();
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly object _cacheLock = new();
        private readonly Dictionary<string, List<byte[]>> _undoStacks = new();

        private string? _currentId;
        private int? _selectedCrackId;
        private string _viewMode = "result";
        private string _activeTool = "pan";
        private float _zoom = 1.0f, _offX = 0f, _offY = 0f;
        private bool _fitMode = true;

        private (int X, int Y, float Ox, float Oy)? _panStart;
        private bool _panMoved;
        private PointF? _linePt1, _linePt2;
        private bool _lineDragging;
        private HoverHit? _hoverProbe;
        private PointF? _cursorImgXy;
        private readonly List<PointF> _strokePts = new();

        private int _hoverQueueRow = -1;
        private int _hoverDetailRow = -1;

        private volatile bool _isBusy, _cancelFlag, _closePending;
        private bool _suppressSelectionEvent;

        private Panel _leftCol = null!, _rightCol = null!;
        private StudioSplitter _leftSplitter = null!, _rightSplitter = null!;
        private RoundedButton _btnToggleLeft = null!, _btnToggleRight = null!;

        private StatusBadge _lblModelBadge = null!;
        private Label _lblStatus = null!;
        private Label _lblProbVal = null!, _lblBrushVal = null!, _lblZoomInfo = null!;
        private Label _lblCursorTelemetry = null!, _lblSelectedDetail = null!;
        private Panel _brushPreviewDot = null!;
        private CardPanel _cardQueue = null!, _cardKpi = null!, _cardDetail = null!;
        private KpiCard _kpiCount = null!, _kpiLen = null!, _kpiWmax = null!, _kpiWavg = null!;
        private ModernProgressBar _progressBar = null!;
        private Panel _statusIndicator = null!;
        private Color _statusDotColor = UiTheme.Success;

        private RoundedButton _btnOpenFiles = null!, _btnOpenFolder = null!, _btnDetectCur = null!, _btnDetectBatch = null!, _btnStop = null!;
        private RoundedButton _btnSaveCur = null!, _btnSaveDxf = null!, _btnReport = null!, _btnExportBatch = null!;
        private RoundedButton _btnFastReprocess = null!, _btnQueueRem = null!, _btnQueueClear = null!;
        private RoundedButton _btnUndo = null!;
        private DataGridView _gridQueue = null!, _gridDetail = null!;
        private ModernToggle _chkTileMode = null!;
        private ModernCheckBox _chkExportMask = null!, _chkExportDxf = null!;
        private DarkCheckPill _chkShowWmax = null!;
        private ModernSlider _trkProb = null!, _trkBrush = null!;
        private TextBoxField _scaleField = null!;
        private TextBox _txtScale = null!;
        private ModernStepper _numMinArea = null!, _numBridge = null!;
        private DarkSelectButton _cmbLabelStyle = null!;
        private readonly Dictionary<string, RoundedButton> _viewBtns = new();
        private readonly Dictionary<string, RoundedButton> _toolBtns = new();
        private BufferedCanvas _canvas = null!;
        private readonly ToolTip _toolTip = new() { AutoPopDelay = 6000, InitialDelay = 350, ReshowDelay = 120 };

        private readonly List<GdiFont> _ownedFonts = new();
        private GdiFont _boldGridFont = null!;
        private readonly GdiFont _canvasFont = new("Microsoft YaHei UI", 9f, FontStyle.Bold);

        private sealed record HoverHit(int Cid, float Sx, float Sy, PointF P1, PointF P2, float WPx, double WVal, string Unit, string Orient);

        public MainForm()
        {
            Text = "CrackMeasure — 表面裂缝分割与测量系统";
            Size = new GdiSize(1580, 920);
            MinimumSize = new GdiSize(1280, 750);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.AppBg;
            ForeColor = UiTheme.TextPrimary;
            Font = new GdiFont("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            KeyPreview = true;
            AllowDrop = true;

            UiTheme.EnableDarkTitleBar(this);

            DragEnter += OnFilesDragEnter;
            DragDrop += OnFilesDragDrop;

            BuildUi();
            Shown += (_, _) => InitModel();
            FormClosing += (_, e) =>
            {
                if (!_isBusy) return;
                e.Cancel = true;
                _cancelFlag = true;
                _closePending = true;
                SetStatus("正在停止后台任务，完成后自动退出...", UiTheme.Warning);
            };
            FormClosed += (_, _) =>
            {
                _segmenter.Dispose();
                _toolTip.Dispose();
                _canvasFont.Dispose();
                foreach (var f in _ownedFonts) f.Dispose();
                _ownedFonts.Clear();
                lock (_cacheLock)
                {
                    foreach (var kv in _cache.Values) kv.Dispose();
                    _cache.Clear();
                    _lruList.Clear();
                }
            };
        }

        private GdiFont OwnFont(float size, FontStyle style = FontStyle.Regular, string family = "Microsoft YaHei UI")
        {
            var f = new GdiFont(family, size, style);
            _ownedFonts.Add(f);
            return f;
        }

        private RoundedButton MakeButton(
            string text,
            IndustrialIconKind icon = IndustrialIconKind.None,
            int padX = 11,
            int height = 32,
            int radius = UiTheme.RadiusControl,
            float fontSize = 8.8f,
            FontStyle style = FontStyle.Bold)
        {
            return new RoundedButton
            {
                Text = text,
                IconKind = icon,
                IconSize = 14,
                IconGap = 5,
                AutoSize = true,
                MinimumSize = new GdiSize(0, height),
                CornerRadius = radius,
                Font = OwnFont(fontSize, style),
                Padding = new Padding(padX, 2, padX, 2),
                Margin = new Padding(2, 0, 2, 0)
            };
        }

        private static Panel MakeVDivider(Color color, int height = 20, int marginX = 6, int marginY = 7) => new()
        {
            Width = 1,
            Height = height,
            BackColor = color,
            Margin = new Padding(marginX, marginY, marginX, marginY)
        };

        private static void AlignGridHeaders(DataGridView grid)
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                if (col.DefaultCellStyle.Alignment == DataGridViewContentAlignment.NotSet)
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                col.HeaderCell.Style.Alignment = col.DefaultCellStyle.Alignment;
            }
        }

        private void BuildUi()
        {
            _boldGridFont = OwnFont(8.4f, FontStyle.Bold);

            // 1. 顶部工作台命令栏
            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = UiTheme.Surface,
                Padding = new Padding(12, 8, 12, 8)
            };

            var topGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.Surface
            };
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var leftFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            var rightFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            // 品牌标识区
            var brandIcon = new Panel { Width = 30, Height = 30, Margin = new Padding(0, 3, 8, 3), BackColor = UiTheme.Surface };
            brandIcon.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF br = new(0.5f, 0.5f, 28.5f, 28.5f);
                using var path = UiTheme.CreateRoundedRect(br, UiTheme.RadiusControl);
                using var grad = new LinearGradientBrush(br, UiTheme.Primary, Color.FromArgb(14, 116, 144), LinearGradientMode.ForwardDiagonal);
                e.Graphics.FillPath(grad, path);
                using var rim = new GdiPen(Color.FromArgb(90, 147, 197, 253), 1f);
                e.Graphics.DrawPath(rim, path);
                IndustrialIcons.DrawIcon(e.Graphics, IndustrialIconKind.BrandReticle, new RectangleF(5f, 5f, 20f, 20f), Color.White, 1.6f);
            };

            var lblTitle = new Label
            {
                Text = "CrackMeasure",
                Font = OwnFont(11f, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 7, 7, 4)
            };
            _toolTip.SetToolTip(brandIcon, "CrackMeasure — 表面裂缝分割与测量系统");
            _toolTip.SetToolTip(lblTitle, "CrackMeasure — 表面裂分割与测量系统");

            _lblModelBadge = new StatusBadge { Margin = new Padding(0, 7, 2, 5) };
            _lblModelBadge.SetState("加载中...", UiTheme.Warning, UiTheme.WarningSoftBg, UiTheme.WarningSoftBorder, UiTheme.WarningText);
            _toolTip.SetToolTip(_lblModelBadge, "正在初始化 ONNX 分割推理后端");

            leftFlow.Controls.Add(brandIcon);
            leftFlow.Controls.Add(lblTitle);
            leftFlow.Controls.Add(_lblModelBadge);
            leftFlow.Controls.Add(MakeVDivider(UiTheme.Border, 22, 6, 7));

            _btnOpenFiles = MakeButton("导入图片", IndustrialIconKind.AddImage, 7, 34);
            _btnOpenFiles.Margin = new Padding(2, 1, 2, 1);
            _btnOpenFiles.ApplyStudioSecondary();
            _btnOpenFiles.Click += (_, _) => ImportImages();
            _toolTip.SetToolTip(_btnOpenFiles, "导入单张或多张待测裂缝图片（也支持直接拖拽到画布）");

            _btnOpenFolder = MakeButton("导入目录", IndustrialIconKind.Folder, 7, 34);
            _btnOpenFolder.Margin = new Padding(2, 1, 2, 1);
            _btnOpenFolder.ApplyStudioSecondary();
            _btnOpenFolder.Click += (_, _) => ImportFolder();
            _toolTip.SetToolTip(_btnOpenFolder, "递归导入文件夹中的所有支持图像");

            leftFlow.Controls.Add(_btnOpenFiles);
            leftFlow.Controls.Add(_btnOpenFolder);
            leftFlow.Controls.Add(MakeVDivider(UiTheme.Border, 22, 5, 7));

            _btnDetectCur = MakeButton("检测当前", IndustrialIconKind.PlayScan, 9, 34);
            _btnDetectCur.Margin = new Padding(2, 1, 2, 1);
            _btnDetectCur.ApplySolidAccent(UiTheme.Primary, UiTheme.PrimaryHover, Color.White);
            _btnDetectCur.Click += (_, _) => DetectCurrentAsync();
            _toolTip.SetToolTip(_btnDetectCur, "对当前选中图像执行分割推理与测量");

            _btnDetectBatch = MakeButton("批量检测", IndustrialIconKind.BatchRun, 8, 34);
            _btnDetectBatch.Margin = new Padding(2, 1, 2, 1);
            _btnDetectBatch.ApplyStudioTinted(UiTheme.PrimarySoftBg, UiTheme.PrimarySoftText, UiTheme.PrimarySoftBorder);
            _btnDetectBatch.Click += (_, _) => DetectBatchAsync();
            _toolTip.SetToolTip(_btnDetectBatch, "按顺序检测队列中的全部图像");

            _btnStop = MakeButton("停止", IndustrialIconKind.StopSquare, 7, 34);
            _btnStop.Margin = new Padding(2, 1, 2, 1);
            _btnStop.ApplyStudioTinted(UiTheme.DangerSoftBg, UiTheme.DangerText, UiTheme.DangerSoftBorder);
            _btnStop.Enabled = false;
            _btnStop.Click += (_, _) => { if (_isBusy) { _cancelFlag = true; SetStatus("正在等待当前图片完成后安全停止...", UiTheme.Warning); } };
            _toolTip.SetToolTip(_btnStop, "在当前图像处理完成后安全终止队列");

            leftFlow.Controls.Add(_btnDetectCur);
            leftFlow.Controls.Add(_btnDetectBatch);
            leftFlow.Controls.Add(_btnStop);

            // 右侧面板开关 & 导出组
            _btnToggleLeft = MakeButton("", IndustrialIconKind.PanelLeft, 6, 34);
            _btnToggleLeft.Margin = new Padding(2, 1, 2, 1);
            _btnToggleLeft.Width = 32;
            _btnToggleLeft.AutoSize = false;
            _btnToggleLeft.ApplyDarkTone(true, UiTheme.SurfaceHover, UiTheme.PrimarySoftText);
            _btnToggleLeft.Click += (_, _) => ToggleLeftDock();
            _toolTip.SetToolTip(_btnToggleLeft, "显示 / 隐藏左侧队列与参数栏");

            _btnToggleRight = MakeButton("", IndustrialIconKind.PanelRight, 6, 34);
            _btnToggleRight.Margin = new Padding(2, 1, 2, 1);
            _btnToggleRight.Width = 32;
            _btnToggleRight.AutoSize = false;
            _btnToggleRight.ApplyDarkTone(true, UiTheme.SurfaceHover, UiTheme.PrimarySoftText);
            _btnToggleRight.Click += (_, _) => ToggleRightInspector();
            _toolTip.SetToolTip(_btnToggleRight, "显示 / 隐藏右侧测量概览与明细栏");

            _btnSaveCur = MakeButton("标注图", IndustrialIconKind.ExportImage, 7, 34);
            _btnSaveCur.Margin = new Padding(2, 1, 2, 1);
            _btnSaveCur.ApplyStudioSecondary();
            _btnSaveCur.Click += (_, _) => SaveCurrentImage();
            _toolTip.SetToolTip(_btnSaveCur, "保存当前图像的高清矢量标注结果图");

            _btnSaveDxf = MakeButton("CAD .dxf", IndustrialIconKind.CadLayer, 7, 34);
            _btnSaveDxf.Margin = new Padding(2, 1, 2, 1);
            _btnSaveDxf.ApplyStudioSecondary();
            _btnSaveDxf.Click += (_, _) => SaveCurrentDxf();
            _toolTip.SetToolTip(_btnSaveDxf, "导出 AutoCAD R12 矢量图层文件 (.dxf)");

            _btnReport = MakeButton("生成报告", IndustrialIconKind.ReportDoc, 7, 34);
            _btnReport.Margin = new Padding(2, 1, 2, 1);
            _btnReport.ApplyStudioSecondary();
            _btnReport.Click += (_, _) => ExportReportDialog();
            _toolTip.SetToolTip(_btnReport, "生成可打印 PDF 的 HTML 报告或 Word (.doc) 图文报告");

            _btnExportBatch = MakeButton("批量导出", IndustrialIconKind.BatchExport, 9, 34);
            _btnExportBatch.Margin = new Padding(2, 1, 0, 1);
            _btnExportBatch.ApplySolidAccent(UiTheme.SuccessFill, UiTheme.SuccessFillHover, Color.White);
            _btnExportBatch.Click += (_, _) => BatchExportDialog();
            _toolTip.SetToolTip(_btnExportBatch, "批量导出标注图、Excel 数据表、HTML 报告及可选 DXF/掩码");

            rightFlow.Controls.Add(_btnToggleLeft);
            rightFlow.Controls.Add(_btnToggleRight);
            rightFlow.Controls.Add(MakeVDivider(UiTheme.Border, 22, 4, 7));
            rightFlow.Controls.Add(_btnSaveCur);
            rightFlow.Controls.Add(_btnSaveDxf);
            rightFlow.Controls.Add(_btnReport);
            rightFlow.Controls.Add(MakeVDivider(UiTheme.Border, 22, 4, 7));
            rightFlow.Controls.Add(_btnExportBatch);

            topGrid.Controls.Add(leftFlow, 0, 0);
            topGrid.Controls.Add(rightFlow, 1, 0);
            topBar.Controls.Add(topGrid);
            AttachAdaptiveToolbarHeight(topBar, leftFlow, rightFlow, 54, 40);

            // 2. 底部全局状态栏
            var botBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = UiTheme.Surface,
                Padding = new Padding(14, 4, 14, 4)
            };

            var botLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = UiTheme.Surface
            };
            botLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16f));
            botLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            botLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            botLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176f));
            botLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _statusIndicator = new Panel { Dock = DockStyle.Fill, Size = new GdiSize(12, 16), Margin = new Padding(0), BackColor = UiTheme.Surface };
            _statusIndicator.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float y = (_statusIndicator.Height - 8f) * 0.5f;
                using var halo = new SolidBrush(Color.FromArgb(65, _statusDotColor));
                e.Graphics.FillEllipse(halo, 0f, y - 2f, 12f, 12f);
                using var br = new SolidBrush(_statusDotColor);
                e.Graphics.FillEllipse(br, 2f, y, 8f, 8f);
            };

            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.TextSecondary,
                Font = OwnFont(8.4f),
                AutoEllipsis = true,
                Text = "就绪"
            };

            var lblShortcuts = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = UiTheme.TextMuted,
                Font = OwnFont(8f),
                Padding = new Padding(8, 0, 12, 0),
                Text = "滚轮 缩放  ·  双击 适应  ·  [ ] 笔刷  ·  Ctrl+Z 撤销  ·  ESC 漫游"
            };

            _progressBar = new ModernProgressBar { Dock = DockStyle.Fill, Maximum = 1000, Margin = new Padding(0, 5, 0, 5) };

            botLayout.Controls.Add(_statusIndicator, 0, 0);
            botLayout.Controls.Add(_lblStatus, 1, 0);
            botLayout.Controls.Add(lblShortcuts, 2, 0);
            botLayout.Controls.Add(_progressBar, 3, 0);
            botBar.Controls.Add(botLayout);

            // 3. 三栏可调节精密工作区
            var workArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = UiTheme.AppBg };

            // 左栏 (292px)
            _leftCol = new Panel { Dock = DockStyle.Left, Width = 292, Padding = new Padding(0, 0, 2, 0), BackColor = UiTheme.AppBg };
            _cardQueue = new CardPanel { Title = "图像队列", BadgeText = "0 张", Dock = DockStyle.Fill, ContentPadding = 8 };
            BuildLeftQueueControls(_cardQueue);

            var paramCard = new CardPanel { Title = "检测与测量参数", BadgeText = "像素", Dock = DockStyle.Bottom, Height = 328, ContentPadding = 12 };
            paramCard.BadgeBackColor = UiTheme.PrimarySoftBg;
            paramCard.BadgeForeColor = UiTheme.PrimarySoftText;
            BuildLeftParamControls(paramCard);

            _leftCol.Controls.Add(_cardQueue);
            _leftCol.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 8, BackColor = UiTheme.AppBg });
            _leftCol.Controls.Add(paramCard);

            _leftSplitter = new StudioSplitter { Dock = DockStyle.Left, Width = 6, MinSize = 240, MinExtra = 460 };
            _leftSplitter.SplitterMoved += (_, _) => { if (_fitMode) FitToWindow(); };

            // 右栏 (412px，保证7列明细表无截断且无横向滚动条)
            _rightCol = new Panel { Dock = DockStyle.Right, Width = 412, Padding = new Padding(2, 0, 0, 0), BackColor = UiTheme.AppBg };
            _cardKpi = new CardPanel { Title = "测量概览", BadgeText = "未选择", Dock = DockStyle.Top, Height = 176, ContentPadding = 8 };
            BuildRightKpiCards(_cardKpi);

            _cardDetail = new CardPanel { Title = "裂缝明细表", BadgeText = "点击行联动图面", Dock = DockStyle.Fill, ContentPadding = 8 };
            BuildRightDetailGrid(_cardDetail);

            _rightCol.Controls.Add(_cardDetail);
            _rightCol.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = UiTheme.AppBg });
            _rightCol.Controls.Add(_cardKpi);

            _rightSplitter = new StudioSplitter { Dock = DockStyle.Right, Width = 6, MinSize = 330, MinExtra = 460 };
            _rightSplitter.SplitterMoved += (_, _) => { if (_fitMode) FitToWindow(); };

            // 中央光学测量视口区（统一 8px 弧度圆角边框）
            var centerCol = new RoundedViewportHost { Dock = DockStyle.Fill, CornerRadius = UiTheme.RadiusCard };
            BuildCenterCanvasArea(centerCol.Content);

            // Dock 顺序：Fill 先加，Splitter 紧随其所属面板之前添加（逆序停靠）
            workArea.Controls.Add(centerCol);
            workArea.Controls.Add(_leftSplitter);
            workArea.Controls.Add(_leftCol);
            workArea.Controls.Add(_rightSplitter);
            workArea.Controls.Add(_rightCol);

            Controls.Add(workArea);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border });
            Controls.Add(topBar);
            Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });
            Controls.Add(botBar);
        }

        private void ToggleLeftDock()
        {
            bool next = !_leftCol.Visible;
            _leftCol.Visible = next;
            _leftSplitter.Visible = next;
            _btnToggleLeft.ApplyDarkTone(next, UiTheme.SurfaceHover, UiTheme.PrimarySoftText);
            if (_fitMode) FitToWindow();
        }

        private void ToggleRightInspector()
        {
            bool next = !_rightCol.Visible;
            _rightCol.Visible = next;
            _rightSplitter.Visible = next;
            _btnToggleRight.ApplyDarkTone(next, UiTheme.SurfaceHover, UiTheme.PrimarySoftText);
            if (_fitMode) FitToWindow();
        }

        private void SetStatus(string text, Color? dotColor = null)
        {
            _lblStatus.Text = text;
            if (dotColor is { } c && c != _statusDotColor)
            {
                _statusDotColor = c;
                _statusIndicator.Invalidate();
            }
        }

        private void BuildLeftQueueControls(CardPanel parent)
        {
            var botRow = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = UiTheme.Surface, Padding = new Padding(0, 6, 0, 0) };
            var lblHint = new Label
            {
                Text = "点击切换 · 支持拖拽",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = OwnFont(8.1f),
                ForeColor = UiTheme.TextMuted
            };

            _btnQueueClear = MakeButton("清空", IndustrialIconKind.TrashClear, 8, 27, UiTheme.RadiusControl, 8.2f, FontStyle.Regular);
            _btnQueueClear.IconSize = 13;
            _btnQueueClear.Dock = DockStyle.Right;
            _btnQueueClear.ApplyStudioTinted(UiTheme.DangerSoftBg, UiTheme.DangerText, UiTheme.DangerSoftBorder);
            _btnQueueClear.Click += (_, _) => ClearQueue();

            _btnQueueRem = MakeButton("移除", IndustrialIconKind.RemoveItem, 8, 27, UiTheme.RadiusControl, 8.2f, FontStyle.Regular);
            _btnQueueRem.IconSize = 13;
            _btnQueueRem.Dock = DockStyle.Right;
            _btnQueueRem.ApplyStudioSecondary();
            _btnQueueRem.Click += (_, _) => RemoveSelectedItem();

            botRow.Controls.Add(lblHint);
            botRow.Controls.Add(_btnQueueRem);
            botRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6, BackColor = UiTheme.Surface });
            botRow.Controls.Add(_btnQueueClear);

            _gridQueue = CreateStyledGrid();
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 30, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "文件名", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 84, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleLeft } });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Width = 58, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "条数", Width = 44, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            AlignGridHeaders(_gridQueue);
            _gridQueue.CellMouseEnter += (_, e) => { if (e.RowIndex != _hoverQueueRow) { _hoverQueueRow = e.RowIndex; _gridQueue.Invalidate(); } };
            _gridQueue.MouseLeave += (_, _) => { if (_hoverQueueRow != -1) { _hoverQueueRow = -1; _gridQueue.Invalidate(); } };
            _gridQueue.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.RowIndex == _hoverQueueRow && !_gridQueue.Rows[e.RowIndex].Selected)
                    e.CellStyle!.BackColor = UiTheme.SurfaceHover;
                if (e.ColumnIndex == 2 && e.Value is string st)
                {
                    e.CellStyle!.Font = _boldGridFont;
                    e.CellStyle.ForeColor = st switch
                    {
                        "已完成" => UiTheme.SuccessText,
                        "已修补" => UiTheme.Teal,
                        "检测中" => UiTheme.Cyan,
                        "出错" or "读取失败" => UiTheme.DangerText,
                        _ => UiTheme.TextMuted
                    };
                }
            };
            _gridQueue.SelectionChanged += (_, _) =>
            {
                if (_suppressSelectionEvent || _gridQueue.SelectedRows.Count == 0) return;
                string? iid = _gridQueue.SelectedRows[0].Tag as string;
                if (iid != null && iid != _currentId)
                {
                    _currentId = iid;
                    _selectedCrackId = null;
                    _hoverProbe = null;
                    if ((_activeTool == "brush" || _activeTool == "eraser") && _items[iid].Summary == null) SetActiveTool("pan");
                    UpdateRightPanel(_items.GetValueOrDefault(iid));
                    FitToWindow();
                    UpdateTelemetryBar();
                }
            };

            parent.Controls.Add(_gridQueue);
            parent.Controls.Add(botRow);
        }

        private void BuildLeftParamControls(CardPanel parent)
        {
            var regular = OwnFont(8.7f);
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11,
                BackColor = UiTheme.Surface
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Row 0: Tile Mode Toggle
            _chkTileMode = new ModernToggle
            {
                Text = "高清重叠切片推理 (保留细缝)",
                Checked = true,
                Font = regular,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 8)
            };
            _toolTip.SetToolTip(_chkTileMode, "启用 512×512 Hanning 窗加权重叠切片推理，消除拼接伪影并保留细微裂缝");
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(_chkTileMode, 0, 0);
            grid.SetColumnSpan(_chkTileMode, 2);

            // Row 1: Threshold header
            var probRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 2, 0, 0)
            };
            probRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            probRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            probRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            probRow.Controls.Add(new Label
            {
                Text = "分割概率阈值",
                Font = regular,
                ForeColor = UiTheme.TextSecondary,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 2, 4, 2)
            }, 0, 0);
            _lblProbVal = new Label
            {
                Text = "0.45",
                Font = OwnFont(9.4f, FontStyle.Bold, "Consolas"),
                ForeColor = UiTheme.Cyan,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 1, 0, 2)
            };
            probRow.Controls.Add(_lblProbVal, 1, 0);
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(probRow, 0, 1);
            grid.SetColumnSpan(probRow, 2);

            // Row 2: Threshold slider
            _trkProb = new ModernSlider { Minimum = 15, Maximum = 90, Value = 45, Dock = DockStyle.Fill, Height = 24, Margin = new Padding(0, 0, 0, 7) };
            _trkProb.ApplyDarkTheme();
            _trkProb.ValueChanged += (_, _) => _lblProbVal.Text = (_trkProb.Value / 100.0).ToString("F2");
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(_trkProb, 0, 2);
            grid.SetColumnSpan(_trkProb, 2);

            // Row 3: Scale calibration
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = "比例尺 (mm/px)", Font = regular, ForeColor = UiTheme.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 8, 5) }, 0, 3);
            var scaleBox = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 2, 0, 5)
            };
            scaleBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            scaleBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scaleBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _scaleField = new TextBoxField("0", OwnFont(9f, FontStyle.Regular, "Consolas"), 28) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0) };
            _txtScale = _scaleField.Inner;
            _txtScale.TextChanged += (_, _) => UpdateTelemetryBar();
            var btnCalib = MakeButton("标定", IndustrialIconKind.CalibrateRuler, 8, 28, UiTheme.RadiusControl, 8.2f);
            btnCalib.IconSize = 13;
            btnCalib.Margin = new Padding(0);
            btnCalib.ApplyStudioTinted(UiTheme.PrimarySoftBg, UiTheme.PrimarySoftText, UiTheme.PrimarySoftBorder);
            btnCalib.Click += (_, _) => SetActiveTool("calib");
            _toolTip.SetToolTip(btnCalib, "在图像参照物两端拉线标定物理比例尺");
            scaleBox.Controls.Add(_scaleField, 0, 0);
            scaleBox.Controls.Add(btnCalib, 1, 0);
            grid.Controls.Add(scaleBox, 1, 3);

            // Row 4: Min area stepper
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = "最小面积 (px²)", Font = regular, ForeColor = UiTheme.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 8, 5) }, 0, 4);
            _numMinArea = new ModernStepper(5, 5000, 5, 35, OwnFont(9f, FontStyle.Regular, "Consolas"), 28) { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 5) };
            grid.Controls.Add(_numMinArea, 1, 4);

            // Row 5: Bridge kernel stepper
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = "断裂桥接核 (px)", Font = regular, ForeColor = UiTheme.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 8, 6) }, 0, 5);
            _numBridge = new ModernStepper(1, 25, 2, 5, OwnFont(9f, FontStyle.Regular, "Consolas"), 28) { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) };
            grid.Controls.Add(_numBridge, 1, 5);

            // Row 6: Fast reprocess
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _btnFastReprocess = MakeButton("应用新参数重算 (免重推)", IndustrialIconKind.Reprocess, 8, 32, UiTheme.RadiusControl, 8.7f);
            _btnFastReprocess.AutoSize = false;
            _btnFastReprocess.Height = 32;
            _btnFastReprocess.Dock = DockStyle.Fill;
            _btnFastReprocess.Margin = new Padding(0, 2, 0, 6);
            _btnFastReprocess.ApplyStudioTinted(UiTheme.PrimarySoftBg, UiTheme.PrimarySoftText, UiTheme.PrimarySoftBorder);
            _btnFastReprocess.Click += (_, _) => FastReprocessCurrent();
            grid.Controls.Add(_btnFastReprocess, 0, 6);
            grid.SetColumnSpan(_btnFastReprocess, 2);

            // Row 7: Divider
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 10f));
            var div = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), BackColor = UiTheme.Surface };
            div.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border });
            grid.Controls.Add(div, 0, 7);
            grid.SetColumnSpan(div, 2);

            // Row 8: Export subheader
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label
            {
                Text = "批量导出附带文件",
                Font = OwnFont(8f, FontStyle.Bold),
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 3)
            }, 0, 8);
            grid.SetColumnSpan(grid.GetControlFromPosition(0, 8)!, 2);

            // Row 9-10: Export options
            _chkExportMask = new ModernCheckBox { Text = "二值分割掩码 (_mask.png)", Font = regular, ForeColor = UiTheme.TextSecondary, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(_chkExportMask, 0, 9);
            grid.SetColumnSpan(_chkExportMask, 2);

            _chkExportDxf = new ModernCheckBox { Text = "CAD 矢量图层 (.dxf)", Checked = true, Font = regular, ForeColor = UiTheme.TextSecondary, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(_chkExportDxf, 0, 10);
            grid.SetColumnSpan(_chkExportDxf, 2);

            parent.Controls.Add(grid);
            void SyncParamHeight()
            {
                if (parent.Width <= 120) return;
                int innerW = Math.Max(120, parent.Width - parent.Padding.Horizontal);
                var wrapMax = new GdiSize(innerW, 0);
                _chkTileMode.MaximumSize = wrapMax;
                _chkExportMask.MaximumSize = wrapMax;
                _chkExportDxf.MaximumSize = wrapMax;
                int needH = grid.GetPreferredSize(wrapMax).Height + parent.Padding.Vertical + 4;
                int targetH = Math.Max(316, needH);
                if (parent.Height != targetH) parent.Height = targetH;
            }
            parent.SizeChanged += (_, _) => SyncParamHeight();
            parent.FontChanged += (_, _) => SyncParamHeight();
            SyncParamHeight();
        }

        private void BuildRightKpiCards(CardPanel parent)
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, BackColor = UiTheme.Surface };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _kpiCount = new KpiCard("检出裂缝数", UiTheme.PrimaryHover) { Dock = DockStyle.Fill };
            _kpiCount.SetHighlightColor(UiTheme.PrimarySoftText);

            _kpiLen = new KpiCard("裂缝总长 (∑L)", UiTheme.Cyan) { Dock = DockStyle.Fill };
            _kpiLen.SetHighlightColor(UiTheme.Cyan);

            _kpiWmax = new KpiCard("最大缝宽 (Wmax)", UiTheme.DangerText) { Dock = DockStyle.Fill };
            _kpiWmax.SetHighlightColor(UiTheme.DangerText);

            _kpiWavg = new KpiCard("均宽 / 分叉", UiTheme.Violet) { Dock = DockStyle.Fill };
            _kpiWavg.SetHighlightColor(UiTheme.Violet);

            grid.Controls.Add(_kpiCount, 0, 0);
            grid.Controls.Add(_kpiLen, 1, 0);
            grid.Controls.Add(_kpiWmax, 0, 1);
            grid.Controls.Add(_kpiWavg, 1, 1);
            parent.Controls.Add(grid);
        }

        private void BuildRightDetailGrid(CardPanel parent)
        {
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                BackColor = UiTheme.SurfaceHeader,
                Padding = new Padding(8, 0, 8, 0)
            };
            footer.Paint += (_, e) =>
            {
                using var p = new GdiPen(UiTheme.Border, 1f);
                e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            _lblSelectedDetail = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = OwnFont(8.1f),
                ForeColor = UiTheme.TextMuted,
                AutoEllipsis = true,
                Text = "悬停图面裂缝测算局部宽度 · 单击联动定位"
            };
            footer.Controls.Add(_lblSelectedDetail);

            _gridDetail = CreateStyledGrid();
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "编号", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 44, MinimumWidth = 44, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "走向", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 17f, MinimumWidth = 50, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "长度 L", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 19f, MinimumWidth = 54, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "均宽 W", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 19f, MinimumWidth = 54, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "最大 W", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 19f, MinimumWidth = 54, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分叉", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 44, MinimumWidth = 44, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "面积", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 18f, MinimumWidth = 50, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            AlignGridHeaders(_gridDetail);
            _gridDetail.CellMouseEnter += (_, e) => { if (e.RowIndex != _hoverDetailRow) { _hoverDetailRow = e.RowIndex; _gridDetail.Invalidate(); } };
            _gridDetail.MouseLeave += (_, _) => { if (_hoverDetailRow != -1) { _hoverDetailRow = -1; _gridDetail.Invalidate(); } };
            _gridDetail.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.RowIndex == _hoverDetailRow && !_gridDetail.Rows[e.RowIndex].Selected)
                    e.CellStyle!.BackColor = UiTheme.SurfaceHover;
                if (e.ColumnIndex == 0) e.CellStyle!.Font = _boldGridFont;
                else if (e.ColumnIndex == 4)
                {
                    e.CellStyle!.Font = _boldGridFont;
                    e.CellStyle.ForeColor = UiTheme.DangerText;
                }
            };
            _gridDetail.SelectionChanged += (_, _) =>
            {
                if (_suppressSelectionEvent || _gridDetail.SelectedRows.Count == 0) return;
                if (_gridDetail.SelectedRows[0].Tag is int cid)
                {
                    _selectedCrackId = cid;
                    UpdateSelectedDetailStrip();
                    _canvas.Invalidate();
                }
            };

            parent.Controls.Add(_gridDetail);
            parent.Controls.Add(footer);
        }

        private DataGridView CreateStyledGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = UiTheme.Surface,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                GridColor = UiTheme.Border,
                Font = OwnFont(8.4f)
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.SurfaceHeader;
            g.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextMuted;
            g.ColumnHeadersDefaultCellStyle.Font = OwnFont(8.2f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.SurfaceHeader;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.TextMuted;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(1, 0, 1, 0);
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.ColumnHeadersHeight = 30;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.DefaultCellStyle.BackColor = UiTheme.Surface;
            g.DefaultCellStyle.ForeColor = UiTheme.TextPrimary;
            g.DefaultCellStyle.Padding = new Padding(3, 0, 3, 0);
            g.DefaultCellStyle.SelectionBackColor = UiTheme.PrimarySoftBg;
            g.DefaultCellStyle.SelectionForeColor = UiTheme.PrimarySoftText;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(19, 27, 44);
            g.RowTemplate.Height = 29;
            return g;
        }

        private static void AttachAdaptiveToolbarHeight(Panel bar, FlowLayoutPanel leftFlow, FlowLayoutPanel rightFlow, int singleHeight, int rowStep = 34)
        {
            void Reflow()
            {
                int rightW = 0;
                int maxItemH = 0;
                foreach (Control c in rightFlow.Controls)
                {
                    var sz = c.AutoSize ? c.GetPreferredSize(GdiSize.Empty) : c.Size;
                    rightW += sz.Width + c.Margin.Horizontal;
                    maxItemH = Math.Max(maxItemH, sz.Height + c.Margin.Vertical);
                }

                int availLeft = Math.Max(120, bar.ClientSize.Width - bar.Padding.Horizontal - rightW - 8);
                int rows = 1;
                int curX = 0;
                foreach (Control c in leftFlow.Controls)
                {
                    int cw, ch;
                    if (c is FlowLayoutPanel sub)
                    {
                        cw = MeasureFlowInlineWidth(sub) + c.Margin.Horizontal;
                        ch = MeasureFlowInlineHeight(sub) + c.Margin.Vertical;
                    }
                    else
                    {
                        var sz = c.AutoSize ? c.GetPreferredSize(GdiSize.Empty) : c.Size;
                        cw = sz.Width + c.Margin.Horizontal;
                        ch = sz.Height + c.Margin.Vertical;
                    }
                    maxItemH = Math.Max(maxItemH, ch);
                    if (curX > 0 && curX + cw > availLeft)
                    {
                        rows++;
                        curX = cw;
                    }
                    else
                    {
                        curX += cw;
                    }
                }
                float dpiScale = bar.DeviceDpi > 0 ? bar.DeviceDpi / 96f : 1f;
                int scaledSingle = Math.Max((int)Math.Round(singleHeight * dpiScale), maxItemH + bar.Padding.Vertical);
                int scaledStep = Math.Max((int)Math.Round(rowStep * dpiScale), maxItemH);
                int targetH = rows <= 1 ? scaledSingle : scaledSingle + (rows - 1) * scaledStep;
                if (bar.Height != targetH) bar.Height = targetH;
            }
            bar.SizeChanged += (_, _) => Reflow();
            bar.FontChanged += (_, _) => Reflow();
            rightFlow.SizeChanged += (_, _) => Reflow();
            foreach (Control c in leftFlow.Controls) c.SizeChanged += (_, _) => Reflow();
        }

        private static int MeasureFlowInlineWidth(FlowLayoutPanel fl)
        {
            int sum = fl.Padding.Horizontal;
            foreach (Control c in fl.Controls)
                sum += (c.AutoSize ? c.GetPreferredSize(GdiSize.Empty).Width : c.Width) + c.Margin.Horizontal;
            return sum;
        }

        private static int MeasureFlowInlineHeight(FlowLayoutPanel fl)
        {
            int maxH = 0;
            foreach (Control c in fl.Controls)
            {
                int ch = (c.AutoSize ? c.GetPreferredSize(GdiSize.Empty).Height : c.Height) + c.Margin.Vertical;
                if (ch > maxH) maxH = ch;
            }
            return fl.Padding.Vertical + maxH;
        }

        private void BuildCenterCanvasArea(Panel centerInner)
        {
            // Viewport Top Bar: 视图切换、标注风格、缩放控制
            var bar1 = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = UiTheme.CanvasBarBg, Padding = new Padding(8, 5, 8, 5) };
            var bar1Grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.CanvasBarBg };
            bar1Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar1Grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var bar1Left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = UiTheme.CanvasBarBg, Margin = new Padding(0) };
            var bar1Right = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = UiTheme.CanvasBarBg, Margin = new Padding(0) };

            var segGroup = new RoundedFlowPanel
            {
                AutoSize = true,
                WrapContents = false,
                BackColor = UiTheme.CanvasPillGroup,
                BorderColor = UiTheme.CanvasBtnBorder,
                CornerRadius = UiTheme.RadiusSubCard,
                Padding = new Padding(2),
                Margin = new Padding(0, 1, 3, 1)
            };
            foreach (var (txt, mode) in new[] { ("标注图", "result"), ("原图", "orig"), ("掩码", "mask"), ("对比", "compare") })
            {
                var b = MakeButton(txt, IndustrialIconKind.None, 6, 25, UiTheme.RadiusSmall, 8.1f);
                b.Margin = new Padding(1, 0, 1, 0);
                b.Click += (_, _) => { _viewMode = mode; SyncViewButtons(); FitToWindow(); };
                _viewBtns[mode] = b;
                segGroup.Controls.Add(b);
            }
            SyncViewButtons();
            bar1Left.Controls.Add(segGroup);
            bar1Left.Controls.Add(MakeVDivider(UiTheme.CanvasBtnBorder, 20, 3, 5));

            _cmbLabelStyle = new DarkSelectButton { IconKind = IndustrialIconKind.LabelTag, MinimumSize = new GdiSize(0, 29), Height = 29, Margin = new Padding(0, 1, 3, 1) };
            _cmbLabelStyle.SetItems(new[] { "精简 (#1)", "完整参数", "无文字" }, 0);
            _cmbLabelStyle.FitWidth();
            _cmbLabelStyle.SelectedIndexChanged += (_, _) => _canvas.Invalidate();
            _toolTip.SetToolTip(_cmbLabelStyle, "切换画布及导出图像上的标签显示样式（精简编号 / 完整测量参数 / 纯净无文字）");
            bar1Left.Controls.Add(_cmbLabelStyle);

            _chkShowWmax = new DarkCheckPill { Text = "Wmax靶标", IconKind = IndustrialIconKind.TargetWmax, MinimumSize = new GdiSize(0, 29), Checked = true, Margin = new Padding(1, 1, 0, 1) };
            _chkShowWmax.Size = _chkShowWmax.GetPreferredSize(GdiSize.Empty);
            _chkShowWmax.CheckedChanged += (_, _) => RefreshOverlayWmax();
            _toolTip.SetToolTip(_chkShowWmax, "显示或隐藏每条裂缝的最大宽度截面靶标");
            bar1Left.Controls.Add(_chkShowWmax);

            var zoomFont = OwnFont(8.8f, FontStyle.Bold, "Consolas");
            int zoomW = Math.Max(48, TextRenderer.MeasureText("3000%", zoomFont, GdiSize.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 10);
            _lblZoomInfo = new DarkPillLabel
            {
                Text = "--",
                Width = zoomW,
                Height = 29,
                CornerRadius = UiTheme.RadiusControl,
                BackColor = UiTheme.CanvasPillGroup,
                BorderColor = UiTheme.CanvasBtnBorder,
                ForeColor = UiTheme.Cyan,
                Font = zoomFont,
                Margin = new Padding(0, 1, 2, 1)
            };
            var btnFit = MakeButton("适应", IndustrialIconKind.FitWindow, 5, 29, UiTheme.RadiusControl, 8.1f);
            btnFit.IconSize = 13;
            btnFit.Margin = new Padding(2, 1, 2, 1);
            btnFit.ApplyDarkTone(false);
            btnFit.Click += (_, _) => FitToWindow();
            _toolTip.SetToolTip(btnFit, "自适应窗口居中显示");

            var btn100 = MakeButton("1:1", IndustrialIconKind.ZoomOneToOne, 5, 29, UiTheme.RadiusControl, 8.1f);
            btn100.IconSize = 13;
            btn100.Margin = new Padding(2, 1, 0, 1);
            btn100.ApplyDarkTone(false);
            btn100.Click += (_, _) => Zoom100();
            _toolTip.SetToolTip(btn100, "按原始 100% 像素比例显示");

            bar1Right.Controls.Add(_lblZoomInfo);
            bar1Right.Controls.Add(btnFit);
            bar1Right.Controls.Add(btn100);
            bar1Grid.Controls.Add(bar1Left, 0, 0);
            bar1Grid.Controls.Add(bar1Right, 1, 0);
            bar1.Controls.Add(bar1Grid);
            AttachAdaptiveToolbarHeight(bar1, bar1Left, bar1Right, 42, 34);

            // 左侧纵向工具轨 (64px)
            var toolRail = new Panel
            {
                Dock = DockStyle.Left,
                Width = 64,
                BackColor = UiTheme.CanvasBarAlt,
                Padding = new Padding(5, 8, 5, 8)
            };

            var railFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = UiTheme.CanvasBarAlt,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            foreach (var (key, txt, icon, tip) in new[]
            {
                ("pan", "漫游", IndustrialIconKind.PanProbe, "漫游与探针：平移画布，悬停裂缝显示亚像素正交测宽探针，单击选中裂缝"),
                ("calib", "标定", IndustrialIconKind.CalibrateRuler, "比例尺标定：在已知真实长度的参照物两端拉线标定 mm/px"),
                ("ruler", "卡尺", IndustrialIconKind.FreeCaliper, "自由游标卡尺：任意两点拉线测量像素距离与物理长度（不改比例尺）"),
                ("brush", "补画", IndustrialIconKind.BrushDraw, "补画裂缝：涂抹补连断缝或漏检细缝，松手后自动重算参数"),
                ("eraser", "擦除", IndustrialIconKind.Eraser, "擦除伪影：涂抹擦除背景污渍或误检区域，松手后自动重算参数")
            })
            {
                var b = new RoundedButton
                {
                    Text = txt,
                    IconKind = icon,
                    IconPlacement = ButtonIconPlacement.Top,
                    IconSize = 16,
                    IconGap = 4,
                    AutoSize = false,
                    Size = new GdiSize(52, 48),
                    CornerRadius = UiTheme.RadiusSubCard,
                    Font = OwnFont(8f, FontStyle.Bold),
                    Padding = new Padding(2, 4, 2, 3),
                    Margin = new Padding(0, 0, 0, 5)
                };
                b.Click += (_, _) => SetActiveTool(key);
                _toolBtns[key] = b;
                _toolTip.SetToolTip(b, tip);
                railFlow.Controls.Add(b);
            }
            SyncToolButtons();

            railFlow.Controls.Add(new Panel { Width = 44, Height = 1, BackColor = UiTheme.CanvasBtnBorder, Margin = new Padding(4, 3, 4, 7) });

            _btnUndo = new RoundedButton
            {
                Text = "撤销",
                IconKind = IndustrialIconKind.Undo,
                IconPlacement = ButtonIconPlacement.Top,
                IconSize = 15,
                IconGap = 4,
                AutoSize = false,
                Size = new GdiSize(52, 46),
                CornerRadius = UiTheme.RadiusSubCard,
                Font = OwnFont(8f, FontStyle.Bold),
                Padding = new Padding(2, 4, 2, 3),
                Margin = new Padding(0)
            };
            _btnUndo.ApplyRailTool(false, UiTheme.TextSecondary);
            _btnUndo.Click += (_, _) => UndoMaskEdit();
            _toolTip.SetToolTip(_btnUndo, "撤销上一步画笔或橡皮擦掩码修改 (Ctrl+Z)");
            railFlow.Controls.Add(_btnUndo);
            toolRail.Controls.Add(railFlow);

            // 视口底部遥测与笔刷条 (32px)
            var viewportBotBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = UiTheme.CanvasBarAlt,
                Padding = new Padding(10, 3, 10, 3)
            };
            var botGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.CanvasBarAlt
            };
            botGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            botGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblCursorTelemetry = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.CanvasMuted,
                Font = OwnFont(8.2f, FontStyle.Regular, "Consolas"),
                AutoEllipsis = true,
                Text = "XY: --   |   图像: --   |   比例尺: 未标定 (px)"
            };

            var brushGroup = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                BackColor = UiTheme.CanvasBarAlt,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _brushPreviewDot = new Panel { Width = 18, Height = 24, Margin = new Padding(0, 0, 4, 0), BackColor = UiTheme.CanvasBarAlt };
            _brushPreviewDot.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float d = Math.Clamp(3f + (_trkBrush?.Value ?? 6) * 0.34f, 4f, 15f);
                float x = (_brushPreviewDot.Width - d) * 0.5f;
                float y = (_brushPreviewDot.Height - d) * 0.5f;
                Color dotCol = _activeTool == "eraser" ? UiTheme.DangerText : (_activeTool == "brush" ? UiTheme.SuccessText : UiTheme.Cyan);
                using var fill = new SolidBrush(Color.FromArgb(90, dotCol));
                using var pen = new GdiPen(dotCol, 1.3f);
                e.Graphics.FillEllipse(fill, x, y, d, d);
                e.Graphics.DrawEllipse(pen, x, y, d, d);
            };
            brushGroup.Controls.Add(_brushPreviewDot);
            brushGroup.Controls.Add(new Label
            {
                Text = "笔刷",
                ForeColor = UiTheme.CanvasMuted,
                Font = OwnFont(8.1f),
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 2)
            });
            var brushFont = OwnFont(8.5f, FontStyle.Bold, "Consolas");
            int brushValW = Math.Max(40, TextRenderer.MeasureText("35 px", brushFont, GdiSize.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 4);
            _lblBrushVal = new Label
            {
                Text = "6 px",
                ForeColor = UiTheme.CanvasText,
                Font = brushFont,
                Width = brushValW,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 2, 0)
            };
            _trkBrush = new ModernSlider { Minimum = 1, Maximum = 35, Value = 6, Width = 86, Height = 24, Margin = new Padding(0) };
            _trkBrush.ApplyDarkTheme();
            _trkBrush.ValueChanged += (_, _) =>
            {
                _lblBrushVal.Text = $"{_trkBrush.Value} px";
                _brushPreviewDot.Invalidate();
                _canvas.Invalidate();
            };
            brushGroup.Controls.Add(_lblBrushVal);
            brushGroup.Controls.Add(_trkBrush);

            botGrid.Controls.Add(_lblCursorTelemetry, 0, 0);
            botGrid.Controls.Add(brushGroup, 1, 0);
            viewportBotBar.Controls.Add(botGrid);

            _canvas = new BufferedCanvas { Dock = DockStyle.Fill, BackColor = UiTheme.CanvasBg, Cursor = Cursors.Default, AllowDrop = true };
            _canvas.DragEnter += OnFilesDragEnter;
            _canvas.DragDrop += OnFilesDragDrop;
            _canvas.Paint += OnCanvasPaint;
            _canvas.Resize += (_, _) => { if (_fitMode) FitToWindow(); else _canvas.Invalidate(); };
            _canvas.MouseDown += OnCanvasMouseDown;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseUp += OnCanvasMouseUp;
            _canvas.MouseLeave += (_, _) =>
            {
                if (_hoverProbe != null || _cursorImgXy != null) { _hoverProbe = null; _cursorImgXy = null; _canvas.Invalidate(); }
                UpdateTelemetryBar(null);
            };
            _canvas.MouseWheel += OnCanvasMouseWheel;
            _canvas.DoubleClick += (_, _) => { if (_activeTool == "pan" && _currentId != null) FitToWindow(); };

            centerInner.Controls.Add(_canvas);
            centerInner.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = UiTheme.Border });
            centerInner.Controls.Add(toolRail);
            centerInner.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });
            centerInner.Controls.Add(viewportBotBar);
            centerInner.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border });
            centerInner.Controls.Add(bar1);
        }

        private void OnFilesDragEnter(object? sender, DragEventArgs e)
        {
            if (_isBusy || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) { if (e != null) e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
        }

        private void OnFilesDragDrop(object? sender, DragEventArgs e)
        {
            if (_isBusy || e.Data?.GetData(DataFormats.FileDrop) is not string[] dropped || dropped.Length == 0) return;
            var collected = new List<string>();
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (string path in dropped)
            {
                try
                {
                    if (File.Exists(path) && ImgCodec.SupportedExts.Contains(Path.GetExtension(path).ToLowerInvariant()))
                        collected.Add(path);
                    else if (Directory.Exists(path))
                        collected.AddRange(Directory.EnumerateFiles(path, "*", opts)
                            .Where(f => ImgCodec.SupportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"读取拖入路径失败：{ex.Message}", "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            if (collected.Count > 0) AddFilePaths(collected);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (ActiveControl is TextBox or NumericUpDown or RoundedFieldBase or ModernStepper) return;
            if (e.KeyCode == Keys.Escape) { SetActiveTool("pan"); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Z) { UndoMaskEdit(); e.Handled = true; }
            else if (e.KeyCode == Keys.OemOpenBrackets) { StepBrush(-2); e.Handled = true; }
            else if (e.KeyCode == Keys.OemCloseBrackets) { StepBrush(2); e.Handled = true; }
        }

        private void StepBrush(int delta)
        {
            _trkBrush.Value = Math.Clamp(_trkBrush.Value + delta, _trkBrush.Minimum, _trkBrush.Maximum);
            _lblBrushVal.Text = $"{_trkBrush.Value} px";
            _brushPreviewDot.Invalidate();
            if (_activeTool is "brush" or "eraser") _canvas.Invalidate();
        }

        private void InitModel()
        {
            string modelPath = ImgCodec.ResolveAsset("crack_seg.onnx");
            if (!File.Exists(modelPath))
            {
                _lblModelBadge.SetState("未找到模型", UiTheme.Warning, UiTheme.WarningSoftBg, UiTheme.WarningSoftBorder, UiTheme.WarningText);
                _toolTip.SetToolTip(_lblModelBadge, $"未找到模型文件：{modelPath}");
                SetStatus($"未找到模型文件: {modelPath}（请将 crack_seg.onnx 放入程序目录）", UiTheme.Warning);
                return;
            }
            try
            {
                _segmenter.LoadModel(modelPath);
                string fmt = _segmenter.IsNhwc ? "NHWC" : "NCHW";
                string shortBadge = _segmenter.DeviceLabel.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)
                    ? "GPU 就绪"
                    : $"CPU {Math.Max(2, Environment.ProcessorCount / 2)}线程";
                if (_segmenter.GpuFallbackReason != null)
                {
                    _lblModelBadge.SetState(shortBadge, UiTheme.Warning, UiTheme.WarningSoftBg, UiTheme.WarningSoftBorder, UiTheme.WarningText);
                    _toolTip.SetToolTip(_lblModelBadge, $"DirectML 回退至 CPU ({fmt})：{_segmenter.GpuFallbackReason}");
                    SetStatus($"GPU 加速不可用（{_segmenter.GpuFallbackReason}），已回退至 {_segmenter.DeviceLabel}", UiTheme.Warning);
                }
                else
                {
                    _lblModelBadge.SetState(shortBadge, UiTheme.Success, UiTheme.SuccessSoftBg, UiTheme.SuccessSoftBorder, UiTheme.SuccessText);
                    _toolTip.SetToolTip(_lblModelBadge, $"模型就绪 · {fmt} · {_segmenter.DeviceLabel}");
                    SetStatus($"模型加载成功 ({fmt})，当前推理后端：{_segmenter.DeviceLabel}", UiTheme.Success);
                }
            }
            catch (Exception ex)
            {
                _lblModelBadge.SetState("模型异常", UiTheme.Danger, UiTheme.DangerSoftBg, UiTheme.DangerSoftBorder, UiTheme.DangerText);
                _toolTip.SetToolTip(_lblModelBadge, $"模型加载失败：{ex.Message}");
                SetStatus($"模型加载失败: {ex.Message}", UiTheme.Danger);
                MessageBox.Show(this, ex.Message, "模型加载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PutCache(string iid, Mat? orig = null, Mat? probU8 = null, string? modeTag = null, Mat? overlay = null, Mat? mask = null, bool? overlayShowWmax = null)
        {
            List<IDisposable> toDispose = new();
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(iid, out var e)) { e = new CacheEntry(); _cache[iid] = e; }
                if (orig != null && !ReferenceEquals(e.OrigImg, orig)) { if (e.OrigImg != null) toDispose.Add(e.OrigImg); e.OrigImg = orig; }
                if (probU8 != null && !ReferenceEquals(e.ProbU8, probU8)) { if (e.ProbU8 != null) toDispose.Add(e.ProbU8); e.ProbU8 = probU8; }
                if (modeTag != null) e.ModeTag = modeTag;
                if (overlay != null && !ReferenceEquals(e.OverlayImg, overlay)) { if (e.OverlayImg != null) toDispose.Add(e.OverlayImg); e.OverlayImg = overlay; }
                if (overlay != null && overlayShowWmax.HasValue) e.OverlayShowWmax = overlayShowWmax.Value;
                if (mask != null && !ReferenceEquals(e.MaskImg, mask)) { if (e.MaskImg != null) toDispose.Add(e.MaskImg); e.MaskImg = mask; }

                _lruList.Remove(iid);
                _lruList.AddLast(iid);

                string? cur = _currentId;
                for (var node = _lruList.First; node != null && _cache.Count > MaxMemoryCache;)
                {
                    var next = node.Next;
                    if (node.Value != cur)
                    {
                        _lruList.Remove(node);
                        if (_cache.Remove(node.Value, out var ev)) toDispose.Add(ev);
                    }
                    node = next;
                }
            }
            DisposeDeferred(toDispose);
        }

        private void DisposeDeferred(List<IDisposable> list)
        {
            if (list.Count == 0) return;
            void Run() { lock (_cacheLock) foreach (var d in list) d.Dispose(); }
            if (IsDisposed || !IsHandleCreated) Run();
            else BeginInvoke((Action)Run);
        }

        private CacheEntry? GetCacheRef(string iid)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(iid, out var entry)) { _lruList.Remove(iid); _lruList.AddLast(iid); return entry; }
                return null;
            }
        }

        private Mat? EnsureOrigLoaded(string iid)
        {
            var entry = GetCacheRef(iid);
            if (entry?.OrigImg != null) return entry.OrigImg;
            if (!_items.TryGetValue(iid, out var item)) return null;
            Mat? m = ImgCodec.ReadBgr(item.FilePath);
            if (m != null) PutCache(iid, orig: m);
            return m;
        }

        private (Mat? Orig, Mat? Overlay, Mat? Mask) EnsureResultCached(string iid)
        {
            if (!_items.TryGetValue(iid, out var item)) return (null, null, null);
            Mat? orig = EnsureOrigLoaded(iid);
            if (orig == null) return (null, null, null);
            var cache = GetCacheRef(iid);
            Mat? ov = cache?.OverlayImg;
            Mat? msk = cache?.MaskImg;
            bool wantWmax = _chkShowWmax.Checked;
            bool wmaxMismatch = cache != null && ov != null && cache.OverlayShowWmax != wantWmax;
            if (item.Summary != null && item.MaskPng != null && (msk == null || ov == null || wmaxMismatch))
            {
                Mat? newMsk = null;
                Mat? newOv = null;
                try
                {
                    if (msk == null)
                    {
                        newMsk = ImgCodec.DecompressMask(item.MaskPng);
                        msk = newMsk;
                    }
                    if (msk != null && (ov == null || wmaxMismatch))
                    {
                        var wpts = wantWmax ? item.Cracks.Select(c => c.WmaxPt).ToList() : null;
                        newOv = PostProcessor.RenderCleanOverlay(orig, msk, item.BranchPts, wpts);
                        ov = newOv;
                    }
                    if (newMsk != null || newOv != null)
                        PutCache(iid, overlay: newOv, mask: newMsk, overlayShowWmax: newOv != null ? wantWmax : null);
                }
                catch
                {
                    newMsk?.Dispose();
                    newOv?.Dispose();
                    ov = cache?.OverlayImg;
                    msk = cache?.MaskImg;
                }
            }
            return (orig, ov, msk);
        }

        private void SyncViewButtons()
        {
            foreach (var (k, b) in _viewBtns)
                b.ApplySegmentedTone(k == _viewMode);
        }

        private void SyncToolButtons()
        {
            var pal = new Dictionary<string, Color>
            {
                ["pan"] = UiTheme.PrimaryHover,
                ["calib"] = UiTheme.SkyFill,
                ["ruler"] = UiTheme.Teal,
                ["brush"] = UiTheme.Success,
                ["eraser"] = UiTheme.DangerHover
            };
            foreach (var (k, b) in _toolBtns)
                b.ApplyRailTool(k == _activeTool, pal[k]);
            _brushPreviewDot?.Invalidate();
        }

        private void SetActiveTool(string tool)
        {
            if (_isBusy) return;
            if (tool != "pan" && _currentId == null) { MessageBox.Show(this, "请先导入并选择一张图片！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (tool is "brush" or "eraser" && (_currentId == null || _items[_currentId].Summary == null))
            {
                MessageBox.Show(this, "请先点击【检测当前】，随后即可用画笔或橡皮擦微调掩码！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_activeTool == tool && tool != "pan") tool = "pan";

            _activeTool = tool;
            _linePt1 = _linePt2 = null;
            _lineDragging = false;
            _strokePts.Clear();
            _hoverProbe = null;
            _canvas.Cursor = tool == "pan" ? Cursors.Hand : Cursors.Cross;
            SyncToolButtons();

            SetStatus(tool switch
            {
                "calib" => "【比例尺标定】在已知长度参照物两端拖拽或点击两点输入真实毫米（右键 / ESC 退出）。",
                "ruler" => "【自由游标卡尺】在任意两点拖拽或点击即可测量像素与物理距离，不改变全局比例尺（右键 / ESC 退出）。",
                "brush" => "【补画裂缝】左键涂抹补连断缝或漏检细缝（[ ] 键调笔刷大小，Ctrl+Z 撤销，右键退出）。",
                "eraser" => "【橡皮擦】左键涂抹擦除污渍、阴影或模板缝等误检区域（Ctrl+Z 撤销，右键退出）。",
                _ => "【漫游与探针模式】滚轮缩放、左键拖拽平移；悬停裂缝实时显示正交截面亚像素缝宽，单击裂缝联动定位表格。"
            }, UiTheme.PrimaryHover);
            _canvas.Invalidate();
        }

        private void PushUndoSnapshot(string iid, Mat? maskImg)
        {
            if (maskImg == null) return;
            byte[]? png = ImgCodec.CompressMask(maskImg);
            if (png == null) return;
            if (!_undoStacks.TryGetValue(iid, out var stack)) { stack = new List<byte[]>(); _undoStacks[iid] = stack; }
            stack.Add(png);
            if (stack.Count > MaxUndoSteps) stack.RemoveAt(0);
        }

        private void UndoMaskEdit()
        {
            if (_isBusy || _currentId == null) return;
            if (!_undoStacks.TryGetValue(_currentId, out var stack) || stack.Count == 0) { SetStatus("当前图像没有可撤销的人工修补记录。", UiTheme.TextMuted); return; }
            byte[] raw = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            using Mat? prev = ImgCodec.DecompressMask(raw);
            if (prev != null) ApplyEditedMask(_currentId, prev, "撤销修补");
        }

        private void CommitPaintStroke()
        {
            if (_currentId == null || _strokePts.Count == 0) return;
            var (orig, _, curMask) = EnsureResultCached(_currentId);
            if (orig == null) return;

            PushUndoSnapshot(_currentId, curMask);
            using Mat edited = curMask != null ? curMask.Clone() : new Mat(orig.Size(), MatType.CV_8UC1, Scalar.All(0));
            Scalar fillVal = _activeTool == "brush" ? new Scalar(255) : new Scalar(0);
            int rad = Math.Max(1, _trkBrush.Value);

            var pts = _strokePts.Select(p => new CvPoint((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToList();
            _strokePts.Clear();
            if (pts.Count == 1) Cv2.Circle(edited, pts[0], rad, fillVal, -1);
            else for (int i = 0; i < pts.Count - 1; i++) { Cv2.Line(edited, pts[i], pts[i + 1], fillVal, rad * 2, LineTypes.Link8); Cv2.Circle(edited, pts[i + 1], rad, fillVal, -1); }
            ApplyEditedMask(_currentId, edited, _activeTool == "brush" ? "人工补画" : "人工擦除");
        }

        private void ApplyEditedMask(string iid, Mat editedMask, string note)
        {
            var sw = Stopwatch.StartNew();
            Mat? orig = EnsureOrigLoaded(iid);
            if (orig == null) return;
            var cache = GetCacheRef(iid);
            var item = _items[iid];
            var p = ReadParams();
            int effArea = note == "人工补画" ? Math.Min(8, p.MinArea) : p.MinArea;
            string baseMode = cache?.ModeTag ?? item.Summary?.Mode.Split('+')[0] ?? "推理";
            bool showWmax = _chkShowWmax.Checked;
            var res = PostProcessor.AnalyzeFromMask(orig, editedMask, cache?.ProbU8, $"{baseMode}+{note}", p.ProbThresh, p.MmPerPx, effArea, p.BridgePx, showWmax);

            item.Status = "已修补";
            item.Summary = res.Summary; item.Cracks = res.Cracks; item.BranchPts = res.BranchPts;
            item.MaskPng = ImgCodec.CompressMask(res.MaskImg);
            PutCache(iid, overlay: res.OverlayImg, mask: res.MaskImg, overlayShowWmax: showWmax);
            UpdateQueueRow(iid, "已修补", res.Summary.CrackCount.ToString());
            UpdateRightPanel(item);
            _canvas.Invalidate();
            SetStatus($"{note}已实时重算 ({sw.Elapsed.TotalMilliseconds:F1} ms)：当前共 {res.Summary.CrackCount} 条裂缝。", UiTheme.Teal);
        }

        private void RefreshOverlayWmax()
        {
            if (_currentId == null) return;
            var item = _items[_currentId];
            var (orig, _, msk) = EnsureResultCached(_currentId);
            if (item.Summary == null || orig == null || msk == null) return;
            bool showWmax = _chkShowWmax.Checked;
            var wpts = showWmax ? item.Cracks.Select(c => c.WmaxPt).ToList() : null;
            PutCache(_currentId, overlay: PostProcessor.RenderCleanOverlay(orig, msk, item.BranchPts, wpts), overlayShowWmax: showWmax);
            _canvas.Invalidate();
        }

        private string GetLabelStyleCode() => _cmbLabelStyle.SelectedIndex switch { 1 => "full", 2 => "none", _ => "compact" };

        private (bool TileMode, double ProbThresh, double MmPerPx, int MinArea, int BridgePx, string LabelStyle) ReadParams()
        {
            double mm = 0.0;
            if (double.TryParse(_txtScale.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) && v > 0) mm = v;
            return (_chkTileMode.Checked, Math.Round(_trkProb.Value / 100.0, 2), mm, (int)_numMinArea.Value, (int)_numBridge.Value, GetLabelStyleCode());
        }

        private void FastReprocessCurrent()
        {
            if (_isBusy || _currentId == null) return;
            var cache = GetCacheRef(_currentId);
            if (cache?.ProbU8 == null) { DetectCurrentAsync(); return; }
            var sw = Stopwatch.StartNew();
            var (orig, _, curMask) = EnsureResultCached(_currentId);
            if (orig == null) return;
            PushUndoSnapshot(_currentId, curMask);
            var p = ReadParams();
            bool showWmax = _chkShowWmax.Checked;

            var res = PostProcessor.ProcessFromProb(orig, cache.ProbU8, cache.ModeTag, p.ProbThresh, p.MmPerPx, p.MinArea, p.BridgePx, showWmax);
            var item = _items[_currentId];
            item.Status = "已完成";
            item.Summary = res.Summary; item.Cracks = res.Cracks; item.BranchPts = res.BranchPts;
            item.MaskPng = ImgCodec.CompressMask(res.MaskImg);
            PutCache(_currentId, overlay: res.OverlayImg, mask: res.MaskImg, overlayShowWmax: showWmax);
            UpdateQueueRow(_currentId, "已完成", res.Summary.CrackCount.ToString());
            UpdateRightPanel(item);
            _canvas.Invalidate();
            SetStatus($"缓存概率图亚像素重算完成 ({sw.Elapsed.TotalMilliseconds:F1} ms)：共检出 {res.Summary.CrackCount} 条裂缝。", UiTheme.Success);
        }

        private void PromptCalibrationDialog(double pxDist)
        {
            using var dlg = new Form
            {
                Text = "比例尺标定",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = OwnFont(9f),
                Padding = new Padding(22)
            };
            UiTheme.EnableDarkTitleBar(dlg);

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 6, Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
            root.Controls.Add(new Label { Text = $"标定线段像素长度：{pxDist:F2} px", Font = OwnFont(10.5f, FontStyle.Bold), ForeColor = UiTheme.Cyan, AutoSize = true, Margin = new Padding(0, 0, 0, 4) }, 0, 0);
            root.Controls.Add(new Label { Text = "请输入该线段对应的真实物理长度（单位：mm）", ForeColor = UiTheme.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, 10) }, 0, 1);

            var valField = new TextBoxField("10.0", OwnFont(10.8f, FontStyle.Regular, "Consolas"), 32) { Width = 360, Margin = new Padding(0, 0, 0, 10) };
            var txtVal = valField.Inner;
            root.Controls.Add(valField, 0, 2);

            var presetFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 10) };
            presetFlow.Controls.Add(new Label { Text = "快捷预设:", AutoSize = true, Margin = new Padding(0, 6, 6, 0), ForeColor = UiTheme.TextMuted });
            foreach (int mm in new[] { 1, 5, 10, 50, 100 })
            {
                int copyMm = mm;
                var pb = MakeButton($"{mm} mm", IndustrialIconKind.None, 8, 26, UiTheme.RadiusControl, 8.2f, FontStyle.Regular);
                pb.ApplyStudioSecondary();
                pb.Click += (_, _) => txtVal.Text = copyMm.ToString("F1", CultureInfo.InvariantCulture);
                presetFlow.Controls.Add(pb);
            }
            root.Controls.Add(presetFlow, 0, 3);

            var chkAll = new ModernCheckBox { Text = "同步换算队列中所有已检测图像的物理尺寸", Checked = true, AutoSize = true, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 0, 0, 14) };
            root.Controls.Add(chkAll, 0, 4);

            var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTheme.Surface, Dock = DockStyle.Fill };
            var btnOk = MakeButton("确定并换算", IndustrialIconKind.Check, 14, 32);
            btnOk.ApplySolidAccent(UiTheme.Primary, UiTheme.PrimaryHover, Color.White);
            var btnCancel = MakeButton("取消", IndustrialIconKind.None, 14, 32, UiTheme.RadiusControl, 9f, FontStyle.Regular);
            btnCancel.ApplyStudioSecondary();
            btnCancel.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel;

            btnOk.Click += (_, _) =>
            {
                if (!double.TryParse(txtVal.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double realMm) || realMm <= 0 || !double.IsFinite(realMm))
                {
                    MessageBox.Show(dlg, "请输入大于 0 的有效毫米数值！", "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                double mmPerPx = realMm / pxDist;
                _txtScale.Text = mmPerPx.ToString("F6", CultureInfo.InvariantCulture);
                var targets = chkAll.Checked ? _itemOrder : (_currentId != null ? new List<string> { _currentId } : new List<string>());
                int cnt = 0;
                foreach (string id in targets)
                {
                    var it = _items[id];
                    if (it.Summary != null)
                    {
                        var s = it.Summary;
                        it.Summary = PostProcessor.BuildSummary(s.Width, s.Height, s.Mode, s.ProbThresh, mmPerPx, s.BridgePx, it.Cracks, it.BranchPts.Count);
                        cnt++;
                    }
                }
                dlg.DialogResult = DialogResult.OK;
                if (_currentId != null) UpdateRightPanel(_items[_currentId]);
                UpdateTelemetryBar();
                SetStatus($"比例尺标定成功：{mmPerPx:F6} mm/px（1 mm ≈ {1.0 / mmPerPx:F1} px），已刷新 {cnt} 张图像。", UiTheme.Success);
            };
            btnRow.Controls.Add(btnOk);
            btnRow.Controls.Add(btnCancel);
            root.Controls.Add(btnRow, 0, 5);

            dlg.Controls.Add(root);
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            SetActiveTool("pan");
        }

        private void ImportImages()
        {
            if (_isBusy) return;
            using var ofd = new OpenFileDialog { Title = "选择裂缝图片 (可多选)", Multiselect = true, Filter = "图像文件|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp" };
            if (ofd.ShowDialog(this) == DialogResult.OK) AddFilePaths(ofd.FileNames);
        }

        private void ImportFolder()
        {
            if (_isBusy) return;
            using var fbd = new FolderBrowserDialog { Description = "选择包含待测图像的文件夹" };
            if (fbd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                var paths = Directory.EnumerateFiles(fbd.SelectedPath, "*", opts)
                    .Where(f => ImgCodec.SupportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                if (paths.Count > 0) AddFilePaths(paths);
                else MessageBox.Show(this, "未找到受支持的图像文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddFilePaths(IEnumerable<string> paths)
        {
            var existing = _items.Values.Select(v => Path.GetFullPath(v.FilePath).ToLowerInvariant()).ToHashSet();
            int added = 0; string? firstId = null;
            _suppressSelectionEvent = true;
            foreach (string raw in paths)
            {
                string full = Path.GetFullPath(raw);
                if (!File.Exists(full) || !existing.Add(full.ToLowerInvariant())) continue;
                string iid = $"img_{DateTime.UtcNow.Ticks}_{_items.Count + 1}";
                var qItem = new QueueItem { Id = iid, FilePath = full, FileName = Path.GetFileName(full) };
                _items[iid] = qItem; _itemOrder.Add(iid);
                int rIdx = _gridQueue.Rows.Add(_itemOrder.Count, qItem.FileName, "待检测", "—");
                _gridQueue.Rows[rIdx].Tag = iid;
                firstId ??= iid; added++;
            }
            _suppressSelectionEvent = false;
            RefreshQueueIndices();
            if (firstId != null && _currentId == null)
            {
                _currentId = firstId;
                _gridQueue.Rows[0].Selected = true;
                _canvas.Cursor = _activeTool == "pan" ? Cursors.Hand : Cursors.Cross;
                UpdateRightPanel(_items[firstId]);
                FitToWindow();
                UpdateTelemetryBar();
            }
            SetStatus($"已导入 {added} 张图片，队列共 {_items.Count} 张。", UiTheme.PrimaryHover);
        }

        private void RemoveSelectedItem()
        {
            if (_isBusy || _gridQueue.SelectedRows.Count == 0) return;
            int idx = _gridQueue.SelectedRows[0].Index;
            string? iid = _gridQueue.SelectedRows[0].Tag as string;
            if (iid == null) return;
            _suppressSelectionEvent = true; _gridQueue.Rows.RemoveAt(idx); _suppressSelectionEvent = false;
            _items.Remove(iid); _itemOrder.Remove(iid); _undoStacks.Remove(iid);
            lock (_cacheLock) { _lruList.Remove(iid); if (_cache.Remove(iid, out var ev)) ev.Dispose(); }
            RefreshQueueIndices();
            if (_gridQueue.Rows.Count > 0)
            {
                int nextIdx = Math.Min(idx, _gridQueue.Rows.Count - 1);
                _gridQueue.Rows[nextIdx].Selected = true;
                _currentId = _gridQueue.Rows[nextIdx].Tag as string;
                UpdateRightPanel(_currentId != null ? _items[_currentId] : null);
                FitToWindow();
            }
            else { _currentId = null; _canvas.Cursor = Cursors.Default; UpdateZoomLabel(false); UpdateRightPanel(null); _canvas.Invalidate(); }
            UpdateTelemetryBar();
        }

        private void ClearQueue()
        {
            if (_isBusy || _items.Count == 0) return;
            if (MessageBox.Show(this, "确定清空所有图像及测量数据吗？", "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _suppressSelectionEvent = true; _gridQueue.Rows.Clear(); _suppressSelectionEvent = false;
            _items.Clear(); _itemOrder.Clear(); _undoStacks.Clear();
            lock (_cacheLock) { foreach (var v in _cache.Values) v.Dispose(); _cache.Clear(); _lruList.Clear(); }
            _currentId = null; _selectedCrackId = null;
            _canvas.Cursor = Cursors.Default;
            UpdateZoomLabel(false);
            RefreshQueueIndices();
            UpdateRightPanel(null);
            UpdateTelemetryBar();
            _canvas.Invalidate();
        }

        private void RefreshQueueIndices()
        {
            for (int i = 0; i < _gridQueue.Rows.Count; i++) _gridQueue.Rows[i].Cells[0].Value = i + 1;
            int done = _items.Values.Count(v => v.Summary != null);
            _cardQueue.BadgeText = done > 0 ? $"共 {_items.Count} · 已检 {done}" : $"{_items.Count} 张";
            if (done > 0)
            {
                _cardQueue.BadgeBackColor = UiTheme.SuccessSoftBg;
                _cardQueue.BadgeForeColor = UiTheme.SuccessText;
            }
            else
            {
                _cardQueue.BadgeBackColor = UiTheme.SurfaceSubtle;
                _cardQueue.BadgeForeColor = UiTheme.TextMuted;
            }
        }

        private void UpdateQueueRow(string iid, string status, string countStr)
        {
            foreach (DataGridViewRow row in _gridQueue.Rows)
                if (row.Tag as string == iid) { row.Cells[2].Value = status; row.Cells[3].Value = countStr; break; }
            RefreshQueueIndices();
        }

        private void UpdateSelectedDetailStrip()
        {
            if (_currentId == null || !_items.TryGetValue(_currentId, out var item) || item.Summary == null)
            {
                _lblSelectedDetail.ForeColor = UiTheme.TextMuted;
                _lblSelectedDetail.Text = "悬停图面裂缝测算局部宽度 · 单击联动定位";
                return;
            }
            if (_selectedCrackId is int sid && item.Cracks.FirstOrDefault(c => c.Id == sid) is { } crack)
            {
                string wFmt = crack.Unit == "mm" ? "F3" : "F2";
                _lblSelectedDetail.ForeColor = UiTheme.Cyan;
                _lblSelectedDetail.Text = $"选中 #{crack.Id} · {crack.Orientation} ({crack.AngleDeg:F1}°) · 长 {crack.LenVal:F1}{crack.Unit} · 最大宽 {crack.MaxWVal.ToString(wFmt)}{crack.Unit} · 置信 {crack.Conf * 100:F0}%";
            }
            else
            {
                _lblSelectedDetail.ForeColor = UiTheme.TextMuted;
                _lblSelectedDetail.Text = $"共 {item.Cracks.Count} 条裂缝 · 悬停测宽，单击表格行高亮定位";
            }
        }

        private void UpdateRightPanel(QueueItem? item)
        {
            _suppressSelectionEvent = true;
            _gridDetail.Rows.Clear();
            if (item?.Summary == null)
            {
                _kpiCount.ValueText = _kpiLen.ValueText = _kpiWmax.ValueText = _kpiWavg.ValueText = "—";
                _cardKpi.BadgeText = item == null ? "未选择" : "待检测";
                _cardKpi.BadgeBackColor = UiTheme.SurfaceSubtle;
                _cardKpi.BadgeForeColor = UiTheme.TextMuted;
                UpdateSelectedDetailStrip();
                _suppressSelectionEvent = false;
                return;
            }
            var s = item.Summary; string u = s.Unit, wFmt = u == "mm" ? "F3" : "F2";
            _cardKpi.BadgeText = $"{s.Width}×{s.Height} · {s.Mode}";
            _cardKpi.BadgeBackColor = UiTheme.PrimarySoftBg;
            _cardKpi.BadgeForeColor = UiTheme.PrimarySoftText;

            _kpiCount.ValueText = $"{s.CrackCount} 条";
            _kpiLen.ValueText = $"{s.TotalLenVal:F1} {u}";
            _kpiWmax.ValueText = $"{s.MaxWVal.ToString(wFmt)} {u}";
            _kpiWavg.ValueText = $"{s.AvgWVal:F2} {u} / {s.TotalBranches}叉";

            foreach (var c in item.Cracks)
            {
                int r = _gridDetail.Rows.Add($"#{c.Id}", c.Orientation, $"{c.LenVal:F1}{c.Unit}", $"{c.AvgWVal.ToString(wFmt)}{c.Unit}", $"{c.MaxWVal.ToString(wFmt)}{c.Unit}", c.Branches, $"{c.AreaVal:F0}{c.Unit}²");
                _gridDetail.Rows[r].Tag = c.Id;
            }
            _gridDetail.ClearSelection();
            _suppressSelectionEvent = false;
            UpdateSelectedDetailStrip();
        }

        private void UpdateTelemetryBar(PointF? imgPt = null)
        {
            if (_lblCursorTelemetry == null) return;
            string xyPart = imgPt is { } p && _currentId != null
                ? $"XY: {(int)Math.Round(p.X)}, {(int)Math.Round(p.Y)} px"
                : "XY: --";

            string sizePart = "图像: --";
            if (_currentId != null && _items.TryGetValue(_currentId, out var it))
            {
                if (it.Summary is { } s) sizePart = $"图像: {s.Width}×{s.Height}";
                else if (GetCacheRef(_currentId)?.OrigImg is { } om) sizePart = $"图像: {om.Cols}×{om.Rows}";
                else sizePart = $"文件: {it.FileName}";
            }

            double mm = ReadParams().MmPerPx;
            string scalePart = mm > 0 ? $"比例尺: {mm:F5} mm/px" : "比例尺: 未标定 (px)";
            string next = $"{xyPart}   |   {sizePart}   |   {scalePart}";
            if (_lblCursorTelemetry.Text != next) _lblCursorTelemetry.Text = next;
        }

        private PointF ScreenToImg(int sx, int sy)
        {
            float ix = (sx - _offX) / _zoom, iy = (sy - _offY) / _zoom;
            if (_viewMode == "compare" && _currentId != null)
            {
                Mat? orig = EnsureOrigLoaded(_currentId);
                if (orig != null && ix >= orig.Cols + 3) ix -= (orig.Cols + 6);
            }
            return new PointF(ix, iy);
        }

        private PointF ImgToScreen(float ix, float iy, bool rightPane = false, int singleW = 0) => new(_offX + (ix + (rightPane ? singleW + 6 : 0f)) * _zoom, _offY + iy * _zoom);

        private HoverHit? FindNearestProfile(float ix, float iy)
        {
            if (_currentId == null || !_items.TryGetValue(_currentId, out var it) || it.Summary == null || it.Cracks.Count == 0) return null;
            float maxDist = Math.Max(4f, 22f / Math.Max(_zoom, 0.1f)), bestD2 = maxDist * maxDist;
            HoverHit? hit = null;
            double factor = it.Summary.MmPerPx > 0 ? it.Summary.MmPerPx : 1.0;
            foreach (var c in it.Cracks)
            {
                if (ix < c.BBox.X - maxDist || ix > c.BBox.Right + maxDist || iy < c.BBox.Y - maxDist || iy > c.BBox.Bottom + maxDist) continue;
                foreach (var pf in c.ProfilePts)
                {
                    float d2 = (pf.Sx - ix) * (pf.Sx - ix) + (pf.Sy - iy) * (pf.Sy - iy);
                    if (d2 < bestD2) { bestD2 = d2; hit = new HoverHit(c.Id, pf.Sx, pf.Sy, new PointF(pf.P1x, pf.P1y), new PointF(pf.P2x, pf.P2y), pf.WidthPx, pf.WidthPx * factor, it.Summary.Unit, c.Orientation); }
                }
            }
            return hit;
        }

        private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
        {
            if (_currentId == null) return;
            if (e.Button == MouseButtons.Right) { SetActiveTool("pan"); return; }
            if (e.Button != MouseButtons.Left) return;
            PointF pt = ScreenToImg(e.X, e.Y);
            if (_activeTool is "calib" or "ruler")
            {
                if (_linePt1 == null || !_lineDragging) { _linePt1 = _linePt2 = pt; _lineDragging = true; }
                else
                {
                    _linePt2 = pt; _lineDragging = false;
                    double d = Math.Sqrt(Math.Pow(pt.X - _linePt1.Value.X, 2) + Math.Pow(pt.Y - _linePt1.Value.Y, 2));
                    _canvas.Invalidate();
                    if (_activeTool == "calib" && d >= 3.0) PromptCalibrationDialog(d);
                    return;
                }
                _canvas.Invalidate();
            }
            else if (_activeTool is "brush" or "eraser") { _cursorImgXy = pt; _strokePts.Clear(); _strokePts.Add(pt); _canvas.Invalidate(); }
            else { _panStart = (e.X, e.Y, _offX, _offY); _panMoved = false; }
        }

        private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
        {
            if (_currentId == null) return;
            PointF pt = ScreenToImg(e.X, e.Y);
            UpdateTelemetryBar(pt);
            if (e.Button == MouseButtons.Left)
            {
                if (_activeTool is "calib" or "ruler" && _linePt1 != null && _lineDragging) { _linePt2 = pt; _canvas.Invalidate(); }
                else if (_activeTool is "brush" or "eraser") { _cursorImgXy = pt; if (_strokePts.Count > 0) _strokePts.Add(pt); _canvas.Invalidate(); }
                else if (_activeTool == "pan" && _panStart is var (sx, sy, ox, oy))
                {
                    if (Math.Abs(e.X - sx) > 3 || Math.Abs(e.Y - sy) > 3) _panMoved = true;
                    _fitMode = false; _offX = ox + (e.X - sx); _offY = oy + (e.Y - sy); _canvas.Invalidate();
                }
            }
            else
            {
                if (_activeTool is "calib" or "ruler" && _linePt1 != null && _lineDragging) { _linePt2 = pt; _canvas.Invalidate(); }
                else if (_activeTool is "brush" or "eraser") { _cursorImgXy = pt; _canvas.Invalidate(); }
                else if (_activeTool == "pan") { var hit = FindNearestProfile(pt.X, pt.Y); if (!Equals(hit, _hoverProbe)) { _hoverProbe = hit; _canvas.Invalidate(); } }
            }
        }

        private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _currentId == null) return;
            PointF pt = ScreenToImg(e.X, e.Y);
            if (_activeTool is "calib" or "ruler" && _linePt1 != null && _lineDragging)
            {
                double d = Math.Sqrt(Math.Pow(pt.X - _linePt1.Value.X, 2) + Math.Pow(pt.Y - _linePt1.Value.Y, 2));
                if (d >= 5.0) { _linePt2 = pt; _lineDragging = false; _canvas.Invalidate(); if (_activeTool == "calib") PromptCalibrationDialog(d); }
            }
            else if (_activeTool is "brush" or "eraser" && _strokePts.Count > 0) CommitPaintStroke();
            else if (_activeTool == "pan")
            {
                if (_panStart != null && !_panMoved)
                {
                    var hit = FindNearestProfile(pt.X, pt.Y);
                    _suppressSelectionEvent = true; _gridDetail.ClearSelection();
                    if (hit != null)
                    {
                        _selectedCrackId = hit.Cid;
                        foreach (DataGridViewRow row in _gridDetail.Rows)
                            if (row.Tag is int id && id == hit.Cid) { row.Selected = true; _gridDetail.FirstDisplayedScrollingRowIndex = row.Index; break; }
                    }
                    else _selectedCrackId = null;
                    _suppressSelectionEvent = false;
                    UpdateSelectedDetailStrip();
                    _canvas.Invalidate();
                }
                _panStart = null;
            }
        }

        private void UpdateZoomLabel(bool hasImage)
        {
            string txt = hasImage ? $"{(int)Math.Round(_zoom * 100)}%" : "--";
            if (_lblZoomInfo.Text != txt) _lblZoomInfo.Text = txt;
        }

        private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
        {
            if (_currentId == null || EnsureOrigLoaded(_currentId) == null) return;
            float factor = e.Delta > 0 ? 1.18f : (1f / 1.18f), nextZoom = Math.Clamp(_zoom * factor, 0.05f, 30f);
            if (Math.Abs(nextZoom - _zoom) < 1e-6f) return;
            float ix = (e.X - _offX) / _zoom, iy = (e.Y - _offY) / _zoom;
            _fitMode = false; _zoom = nextZoom; _offX = e.X - ix * nextZoom; _offY = e.Y - iy * nextZoom;
            UpdateZoomLabel(true);
            _canvas.Invalidate();
        }

        private (int TotalW, int TotalH, int SingleW) GetVirtualDimensions()
        {
            if (_currentId == null) return (0, 0, 0);
            var (orig, ov, _) = EnsureResultCached(_currentId);
            if (orig == null) return (0, 0, 0);
            bool isCmp = _viewMode == "compare" && ov != null;
            return (isCmp ? orig.Cols * 2 + 6 : orig.Cols, orig.Rows, orig.Cols);
        }

        private void FitToWindow()
        {
            var (vw, vh, _) = GetVirtualDimensions();
            if (vw <= 0 || vh <= 0) { UpdateZoomLabel(false); _canvas.Invalidate(); return; }
            _fitMode = true;
            int cw = Math.Max(_canvas.Width, 100), ch = Math.Max(_canvas.Height, 100);
            _zoom = Math.Max(0.05f, Math.Min((cw - 28f) / vw, (ch - 28f) / vh));
            _offX = (cw - vw * _zoom) * 0.5f; _offY = (ch - vh * _zoom) * 0.5f;
            UpdateZoomLabel(true);
            _canvas.Invalidate();
        }

        private void Zoom100()
        {
            var (vw, vh, _) = GetVirtualDimensions();
            if (vw <= 0 || vh <= 0) return;
            _fitMode = false; _zoom = 1.0f;
            _offX = (_canvas.Width - vw) * 0.5f; _offY = (_canvas.Height - vh) * 0.5f;
            UpdateZoomLabel(true);
            _canvas.Invalidate();
        }

        private void DrawViewportMat(Graphics g, Mat srcMat, float originScreenX, float originScreenY)
        {
            int cw = _canvas.Width, ch = _canvas.Height, iw = srcMat.Cols, ih = srcMat.Rows;
            float vx1 = Math.Max(0f, -originScreenX / _zoom), vy1 = Math.Max(0f, -originScreenY / _zoom);
            float vx2 = Math.Min(iw, (cw - originScreenX) / _zoom), vy2 = Math.Min(ih, (ch - originScreenY) / _zoom);
            if (vx2 <= vx1 || vy2 <= vy1) return;

            int ix1 = Math.Max(0, (int)MathF.Floor(vx1)), iy1 = Math.Max(0, (int)MathF.Floor(vy1));
            int ix2 = Math.Min(iw, (int)MathF.Ceiling(vx2)), iy2 = Math.Min(ih, (int)MathF.Ceiling(vy2));
            int ow = Math.Max(1, (int)Math.Round((ix2 - ix1) * _zoom)), oh = Math.Max(1, (int)Math.Round((iy2 - iy1) * _zoom));

            using Mat sub = new(srcMat, new Rect(ix1, iy1, ix2 - ix1, iy2 - iy1));
            using Mat colorSub = new();
            if (sub.Channels() == 1) Cv2.CvtColor(sub, colorSub, ColorConversionCodes.GRAY2BGR); else sub.CopyTo(colorSub);
            var interp = _zoom >= 3.0f ? InterpolationFlags.Nearest : (_zoom < 1.0f ? InterpolationFlags.Area : InterpolationFlags.Linear);
            using Mat scaled = new();
            Cv2.Resize(colorSub, scaled, new CvSize(ow, oh), 0, 0, interp);
            using Bitmap bmp = BitmapConverter.ToBitmap(scaled);
            g.DrawImageUnscaled(bmp, (int)Math.Round(originScreenX + ix1 * _zoom), (int)Math.Round(originScreenY + iy1 * _zoom));
        }

        private void DrawPaneBadge(Graphics g, GdiFont font, string text, float paneLeft, float paneRight, float paneTop)
        {
            SizeF sz = g.MeasureString(text, font);
            float bw = sz.Width + 14f;
            float bh = sz.Height + 6f;
            if (paneRight - paneLeft < bw + 12f || paneRight <= 10f || paneLeft >= _canvas.Width - 10f) return;
            float x = Math.Clamp(Math.Max(10f, paneLeft + 10f), 10f, Math.Max(10f, paneRight - bw - 6f));
            float y = Math.Clamp(Math.Max(10f, paneTop + 10f), 10f, Math.Max(10f, _canvas.Height - bh - 10f));
            RectangleF rect = new(x, y, bw, bh);
            using var path = UiTheme.CreateRoundedRect(rect, UiTheme.RadiusControl);
            using var bg = new SolidBrush(Color.FromArgb(210, 15, 23, 42));
            using var bd = new GdiPen(Color.FromArgb(140, 148, 163, 184), 1f);
            g.FillPath(bg, path);
            g.DrawPath(bd, path);
            g.DrawString(text, font, Brushes.White, rect.X + 7f, rect.Y + 3f);
        }

        private void DrawCanvasMicroGrid(Graphics g)
        {
            const int step = 32;
            int w = _canvas.Width, h = _canvas.Height;
            using var gridPen = new GdiPen(UiTheme.CanvasGrid, 1f);
            for (int x = step; x < w; x += step)
                g.DrawLine(gridPen, x, 0, x, h);
            for (int y = step; y < h; y += step)
                g.DrawLine(gridPen, 0, y, w, y);
        }

        private void OnCanvasPaint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            DrawCanvasMicroGrid(g);

            if (_currentId == null || !_items.TryGetValue(_currentId, out var item)) return;
            var (orig, ov, msk) = EnsureResultCached(_currentId);
            if (orig == null) return;

            int sw = orig.Cols, sh = orig.Rows;
            bool isCmp = _viewMode == "compare" && ov != null;

            if (isCmp)
            {
                DrawViewportMat(g, orig, _offX, _offY);
                using var sepBrush = new SolidBrush(Color.FromArgb(30, 41, 59));
                g.FillRectangle(sepBrush, _offX + sw * _zoom, _offY, 6f * _zoom, sh * _zoom);
                DrawViewportMat(g, ov!, _offX + (sw + 6) * _zoom, _offY);
                DrawPaneBadge(g, _canvasFont, "原始图像", _offX, _offX + sw * _zoom, _offY);
                float rightStart = _offX + (sw + 6) * _zoom;
                DrawPaneBadge(g, _canvasFont, "测量标注", rightStart, rightStart + sw * _zoom, _offY);
            }
            else
            {
                DrawViewportMat(g, _viewMode switch { "orig" => orig, "mask" when msk != null => msk, _ => ov ?? orig }, _offX, _offY);
            }

            string styleCode = GetLabelStyleCode();
            if (item.Summary != null && item.Cracks.Count > 0)
            {
                if (_selectedCrackId is int selId && item.Cracks.FirstOrDefault(c => c.Id == selId) is { } selCrack)
                {
                    using var whitePen = new GdiPen(Color.White, 4f);
                    using var pinkPen = new GdiPen(Color.FromArgb(236, 72, 153), 2f);
                    foreach (bool rp in isCmp ? new[] { false, true } : new[] { false })
                    {
                        PointF p1 = ImgToScreen(selCrack.BBox.X, selCrack.BBox.Y, rp, sw), p2 = ImgToScreen(selCrack.BBox.Right, selCrack.BBox.Bottom, rp, sw);
                        var r = new RectangleF(p1.X - 4f, p1.Y - 4f, (p2.X - p1.X) + 8f, (p2.Y - p1.Y) + 8f);
                        g.DrawRectangle(whitePen, r.X, r.Y, r.Width, r.Height); g.DrawRectangle(pinkPen, r.X, r.Y, r.Width, r.Height);
                    }
                }
                if ((_viewMode is "result" or "compare") && styleCode != "none")
                {
                    float pxOff = _offX + (isCmp ? (sw + 6) * _zoom : 0f);
                    var layouts = PostProcessor.ComputeLabelLayout(item.Cracks, styleCode, s => g.MeasureString(s, _canvasFont), sw * _zoom, sh * _zoom, _zoom, pxOff, _offY);
                    using var fullBoxPen = new GdiPen(Color.FromArgb(253, 224, 71), 1f);
                    using var normFill = new SolidBrush(Color.FromArgb(228, 15, 23, 42));
                    using var selFill = new SolidBrush(Color.FromArgb(238, 131, 24, 67));
                    foreach (var lay in layouts)
                    {
                        bool sel = lay.Id == _selectedCrackId;
                        Color bCol = sel ? Color.FromArgb(236, 72, 153) : Color.FromArgb(250, 204, 21);
                        using var bPen = new GdiPen(bCol, sel ? 2f : 1.25f);
                        using var dotBrush = new SolidBrush(bCol);
                        if (styleCode == "full" && !sel) g.DrawRectangle(fullBoxPen, lay.BBox.X, lay.BBox.Y, lay.BBox.Width, lay.BBox.Height);
                        if (lay.Leader is var (lx1, ly1, lx2, ly2)) { g.DrawLine(bPen, lx1, ly1, lx2, ly2); g.FillEllipse(dotBrush, lx2 - 2.5f, ly2 - 2.5f, 5f, 5f); }
                        using var pill = UiTheme.CreateRoundedRect(lay.Rect, UiTheme.RadiusSmall);
                        g.FillPath(sel ? selFill : normFill, pill);
                        g.DrawPath(bPen, pill);
                        g.DrawString(lay.Text, _canvasFont, Brushes.White, lay.Rect.X + 5f, lay.Rect.Y + 3f);
                    }
                }
            }

            if (_activeTool == "pan" && _hoverProbe is { } hp)
            {
                using var blkPen = new GdiPen(Color.Black, 4f);
                using var cyanPen = new GdiPen(Color.FromArgb(0, 245, 212), 2f);
                using var cyanBrush = new SolidBrush(Color.FromArgb(0, 245, 212));
                using var tipBg = new SolidBrush(Color.FromArgb(238, 15, 23, 42));
                foreach (bool rp in isCmp ? new[] { false, true } : new[] { false })
                {
                    PointF s1 = ImgToScreen(hp.P1.X, hp.P1.Y, rp, sw), s2 = ImgToScreen(hp.P2.X, hp.P2.Y, rp, sw), sc = ImgToScreen(hp.Sx, hp.Sy, rp, sw);
                    g.DrawLine(blkPen, s1, s2); g.DrawLine(cyanPen, s1, s2);
                    g.FillEllipse(cyanBrush, s1.X - 3.5f, s1.Y - 3.5f, 7f, 7f); g.FillEllipse(cyanBrush, s2.X - 3.5f, s2.Y - 3.5f, 7f, 7f);
                    if (rp || !isCmp)
                    {
                        string wStr = hp.Unit == "mm" ? $"{hp.WVal:F3} mm ({hp.WPx:F2} px)" : $"{hp.WPx:F2} px";
                        string tip = $"#{hp.Cid} 局部宽: {wStr} | {hp.Orient}";
                        SizeF tsz = g.MeasureString(tip, _canvasFont);
                        float tw = Math.Min(_canvas.Width - 16f, tsz.Width + 16f);
                        float th = tsz.Height + 8f;
                        float tx = Math.Clamp(sc.X + 14f, 8f, Math.Max(8f, _canvas.Width - tw - 8f));
                        float ty = Math.Clamp(sc.Y - th - 6f, 8f, Math.Max(8f, _canvas.Height - th - 8f));
                        var tipRect = new RectangleF(tx, ty, tw, th);
                        using var tipPath = UiTheme.CreateRoundedRect(tipRect, UiTheme.RadiusControl);
                        g.FillPath(tipBg, tipPath);
                        g.DrawPath(cyanPen, tipPath);
                        g.DrawString(tip, _canvasFont, cyanBrush, new RectangleF(tx + 7f, ty + 4f, Math.Max(10f, tw - 14f), th - 6f));
                    }
                }
            }

            if (_activeTool is "calib" or "ruler")
            {
                Color accent = _activeTool == "calib" ? UiTheme.Cyan : UiTheme.Teal;
                string banTxt = _activeTool == "calib" ? "比例尺标定：拖动或点击参照物两端输入真实毫米 (ESC 取消)" : "自由游标卡尺：拖动或点击任意两点测量距离，不改比例尺 (ESC 退出)";
                SizeF bsz = g.MeasureString(banTxt, _canvasFont);
                float banW = Math.Min(Math.Max(120f, _canvas.Width - 24f), bsz.Width + 36f);
                float banH = bsz.Height + 10f;
                var banRect = new RectangleF(12f, 12f, banW, banH);
                using var banPath = UiTheme.CreateRoundedRect(banRect, UiTheme.RadiusControl);
                using var banBg = new SolidBrush(Color.FromArgb(232, 15, 23, 42));
                using var banBd = new GdiPen(accent, 1.3f);
                using var dotBr = new SolidBrush(accent);
                g.FillPath(banBg, banPath);
                g.DrawPath(banBd, banPath);
                g.FillEllipse(dotBr, 21f, 12f + (banH - 7f) * 0.5f, 7f, 7f);
                using var banSf = new StringFormat { Alignment = StringAlignment.Near };
                g.DrawString(banTxt, _canvasFont, Brushes.White, new RectangleF(33f, 12f + (banH - bsz.Height) * 0.5f, Math.Max(20f, banW - 42f), bsz.Height + 2f), banSf);

                if (_linePt1 is { } lp1 && _linePt2 is { } lp2)
                {
                    PointF s1 = ImgToScreen(lp1.X, lp1.Y, isCmp, sw), s2 = ImgToScreen(lp2.X, lp2.Y, isCmp, sw);
                    double pxD = Math.Sqrt(Math.Pow(lp2.X - lp1.X, 2) + Math.Pow(lp2.Y - lp1.Y, 2)), mmK = ReadParams().MmPerPx;
                    string lbl = mmK > 0 ? $"{pxD * mmK:F3} mm ({pxD:F1} px)" : $"{pxD:F2} px";
                    using var blkPen = new GdiPen(Color.Black, 4f); using var cyanPen = new GdiPen(Color.FromArgb(0, 245, 212), 2f);
                    using var cyanBrush = new SolidBrush(Color.FromArgb(0, 245, 212)); using var darkFill = new SolidBrush(Color.FromArgb(235, 15, 23, 42));
                    g.DrawLine(blkPen, s1, s2); g.DrawLine(cyanPen, s1, s2);
                    g.FillEllipse(darkFill, s1.X - 5f, s1.Y - 5f, 10f, 10f); g.DrawEllipse(cyanPen, s1.X - 5f, s1.Y - 5f, 10f, 10f);
                    g.FillEllipse(darkFill, s2.X - 5f, s2.Y - 5f, 10f, 10f); g.DrawEllipse(cyanPen, s2.X - 5f, s2.Y - 5f, 10f, 10f);
                    SizeF lsz = g.MeasureString(lbl, _canvasFont);
                    float bw = lsz.Width + 16f, bh = lsz.Height + 8f;
                    float mx = Math.Clamp((s1.X + s2.X) * 0.5f - bw * 0.5f, 8f, Math.Max(8f, _canvas.Width - bw - 8f));
                    float my = Math.Clamp((s1.Y + s2.Y) * 0.5f - bh - 8f, 8f, Math.Max(8f, _canvas.Height - bh - 8f));
                    var mRect = new RectangleF(mx, my, bw, bh);
                    using var mPath = UiTheme.CreateRoundedRect(mRect, UiTheme.RadiusControl);
                    g.FillPath(darkFill, mPath);
                    g.DrawPath(cyanPen, mPath);
                    g.DrawString(lbl, _canvasFont, cyanBrush, mx + 7f, my + 4f);
                }
            }

            if (_activeTool is "brush" or "eraser")
            {
                float rScr = Math.Max(2f, _trkBrush.Value * _zoom);
                Color cCol = _activeTool == "brush" ? Color.FromArgb(34, 197, 94) : Color.FromArgb(244, 63, 94);
                using var strokePen = new GdiPen(Color.FromArgb(190, cCol), rScr * 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                using var cursorPen = new GdiPen(cCol, 2f); using var dotFill = new SolidBrush(Color.FromArgb(190, cCol));
                if (_strokePts.Count > 0)
                {
                    foreach (bool rp in isCmp ? new[] { false, true } : new[] { false })
                    {
                        var sPts = _strokePts.Select(p => ImgToScreen(p.X, p.Y, rp, sw)).ToArray();
                        if (sPts.Length == 1) g.FillEllipse(dotFill, sPts[0].X - rScr, sPts[0].Y - rScr, rScr * 2f, rScr * 2f);
                        else g.DrawLines(strokePen, sPts);
                    }
                }
                if (_cursorImgXy is { } curPt)
                {
                    foreach (bool rp in isCmp ? new[] { false, true } : new[] { false })
                    {
                        PointF cp = ImgToScreen(curPt.X, curPt.Y, rp, sw);
                        g.DrawEllipse(cursorPen, cp.X - rScr, cp.Y - rScr, rScr * 2f, rScr * 2f);
                    }
                }
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            foreach (var b in new[] { _btnOpenFiles, _btnOpenFolder, _btnDetectCur, _btnDetectBatch, _btnSaveCur, _btnSaveDxf, _btnReport, _btnExportBatch, _btnFastReprocess, _btnQueueRem, _btnQueueClear, _btnUndo })
                b.Enabled = !busy;
            _btnStop.Enabled = busy;
            if (!busy && _closePending && !IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed) Close(); }));
        }

        private void DetectCurrentAsync()
        {
            if (_isBusy) return;
            if (!_segmenter.IsReady) { MessageBox.Show(this, "模型未加载！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (_currentId == null) { MessageBox.Show(this, "请先选择一张图片！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            RunInferenceWorker(new List<string> { _currentId });
        }

        private void DetectBatchAsync()
        {
            if (_isBusy) return;
            if (!_segmenter.IsReady) { MessageBox.Show(this, "模型未加载！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (_itemOrder.Count == 0) { MessageBox.Show(this, "队列为空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            RunInferenceWorker(_itemOrder.ToList());
        }

        private void RunInferenceWorker(List<string> targetIds, Action? onFinishedCb = null)
        {
            SetActiveTool("pan");
            var p = ReadParams();
            bool showWmax = _chkShowWmax.Checked;
            _cancelFlag = false; SetBusy(true); _progressBar.Value = 0;

            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                int total = targetIds.Count, okCnt = 0;
                string? lastErr = null;

                for (int idx = 0; idx < total; idx++)
                {
                    if (_cancelFlag) break;
                    string iid = targetIds[idx];
                    if (!_items.TryGetValue(iid, out var item)) continue;
                    int step = idx + 1;
                    BeginInvoke(new Action(() => { if (!IsDisposed) { UpdateQueueRow(iid, "检测中", "—"); SetStatus($"[{step}/{total}] 正在推理与法向测量: {item.FileName} ...", UiTheme.Cyan); } }));

                    Mat? img = ImgCodec.ReadBgr(item.FilePath);
                    if (img == null) { lastErr = $"无法读取图像 {item.FileName}"; BeginInvoke(new Action(() => { if (!IsDisposed) UpdateQueueRow(iid, "读取失败", "—"); })); continue; }

                    Mat? probU8 = null;
                    AnalysisOutput? outRes = null;
                    bool handedOff = false;
                    try
                    {
                        (probU8, string modeTag) = _segmenter.InferProbU8(img, p.TileMode);
                        outRes = PostProcessor.ProcessFromProb(img, probU8, modeTag, p.ProbThresh, p.MmPerPx, p.MinArea, p.BridgePx, showWmax);
                        byte[]? maskPng = ImgCodec.CompressMask(outRes.MaskImg);
                        Mat imgRef = img, probRef = probU8;
                        AnalysisOutput resRef = outRes;
                        int prog = (int)Math.Round((step / (double)total) * 1000), cnt = outRes.Summary.CrackCount;
                        BeginInvoke(new Action(() =>
                        {
                            if (IsDisposed) { imgRef.Dispose(); probRef.Dispose(); resRef.OverlayImg.Dispose(); resRef.MaskImg.Dispose(); return; }
                            item.Status = "已完成"; item.Summary = resRef.Summary; item.Cracks = resRef.Cracks;
                            item.BranchPts = resRef.BranchPts; item.MaskPng = maskPng;
                            _undoStacks.Remove(iid);
                            PutCache(iid, orig: imgRef, probU8: probRef, modeTag: modeTag, overlay: resRef.OverlayImg, mask: resRef.MaskImg, overlayShowWmax: showWmax);
                            UpdateQueueRow(iid, "已完成", cnt.ToString());
                            _progressBar.Value = Math.Clamp(prog, 0, 1000);
                            if (iid == _currentId)
                            {
                                if (_viewMode == "orig") { _viewMode = "result"; SyncViewButtons(); }
                                _selectedCrackId = null; UpdateRightPanel(item); UpdateTelemetryBar(); _canvas.Invalidate();
                            }
                        }));
                        handedOff = true;
                        okCnt++;
                    }
                    catch (Exception ex)
                    {
                        if (!handedOff) { img.Dispose(); probU8?.Dispose(); outRes?.OverlayImg.Dispose(); outRes?.MaskImg.Dispose(); }
                        lastErr = ex.Message;
                        string err = ex.Message;
                        BeginInvoke(new Action(() => { if (!IsDisposed) { UpdateQueueRow(iid, "出错", "—"); SetStatus($"推理出错: {err}", UiTheme.Danger); } }));
                    }
                }

                double sec = sw.Elapsed.TotalSeconds; bool canceled = _cancelFlag;
                BeginInvoke(new Action(() =>
                {
                    SetBusy(false);
                    if (_closePending) return;
                    if (canceled) { SetStatus($"任务已停止：完成 {okCnt}/{total} 张 ({sec:F1}s)", UiTheme.Warning); return; }
                    _progressBar.Value = 1000;
                    if (lastErr != null && okCnt == 0) SetStatus($"检测失败：{lastErr}", UiTheme.Danger);
                    else SetStatus($"处理完成！共完成 {okCnt}/{total} 张图像，总耗时 {sec:F2}s。", UiTheme.Success);
                    onFinishedCb?.Invoke();
                }));
            });
        }

        private (Mat Orig, Mat ResBgr, Mat? Mask) BuildExportTripleCloned(string iid, string styleCode, bool showWmax)
        {
            var it = _items[iid];
            Mat? orig = null, overlay = null, mask = null;
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(iid, out var e))
                {
                    orig = e.OrigImg?.Clone();
                    if (e.OverlayShowWmax == showWmax) overlay = e.OverlayImg?.Clone();
                    mask = e.MaskImg?.Clone();
                }
            }
            try
            {
                orig ??= ImgCodec.ReadBgr(it.FilePath) ?? throw new IOException($"无法读取源图像: {it.FilePath}");
                mask ??= ImgCodec.DecompressMask(it.MaskPng) ?? throw new InvalidOperationException($"{it.FileName} 缺少掩码，请重新检测");
                overlay ??= PostProcessor.RenderCleanOverlay(orig, mask, it.BranchPts, showWmax ? it.Cracks.Select(c => c.WmaxPt).ToList() : null);
                return (orig, PostProcessor.RenderHighDpiExport(overlay, it.Cracks, styleCode), mask);
            }
            catch { orig?.Dispose(); mask?.Dispose(); throw; }
            finally { overlay?.Dispose(); }
        }

        private void SaveCurrentImage()
        {
            if (_isBusy) { MessageBox.Show(this, "后台任务正在运行，请稍候或停止后再操作！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (_currentId == null || _items[_currentId].Summary == null) { MessageBox.Show(this, "请先对当前图片执行检测！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var it = _items[_currentId];
            using var sfd = new SaveFileDialog { Title = "保存高清标注图", FileName = $"{Path.GetFileNameWithoutExtension(it.FileName)}_result.jpg", Filter = "JPEG 图像|*.jpg|PNG 无损图像|*.png" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var p = ReadParams();
                var (orig, res, msk) = BuildExportTripleCloned(_currentId, p.LabelStyle, _chkShowWmax.Checked);
                using (orig) using (res) using (msk) ImgCodec.WriteImg(sfd.FileName, res);
                SetStatus($"高清标注图已保存至：{sfd.FileName}", UiTheme.Success);
                MessageBox.Show(this, $"高清标注图已保存至：\n{sfd.FileName}", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveCurrentDxf()
        {
            if (_isBusy) { MessageBox.Show(this, "后台任务正在运行，请稍候或停止后再操作！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (_currentId == null || _items[_currentId].Summary == null) { MessageBox.Show(this, "请先对当前图片执行检测！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var it = _items[_currentId];
            using var sfd = new SaveFileDialog { Title = "导出 AutoCAD 裂缝矢量图 (.dxf)", FileName = $"{Path.GetFileNameWithoutExtension(it.FileName)}_cracks.dxf", Filter = "AutoCAD DXF 矢量图|*.dxf" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var p = ReadParams();
                var (orig, res, msk) = BuildExportTripleCloned(_currentId, p.LabelStyle, _chkShowWmax.Checked);
                using (orig) using (res) using (msk) ReportExporter.ExportDxf(sfd.FileName, it.Summary!, it.Cracks, msk);
                SetStatus($"CAD 矢量图已导出至：{sfd.FileName}", UiTheme.Success);
                MessageBox.Show(this, $"CAD 矢量图已导出至：\n{sfd.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportReportDialog()
        {
            if (_isBusy) return;
            var doneIds = _itemOrder.Where(id => _items[id].Summary != null).ToList();
            if (doneIds.Count == 0) { MessageBox.Show(this, "暂无已检测完成的图像！请先执行检测后再导出报告。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            using var dlg = new Form
            {
                Text = "生成裂缝检测报告",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                Font = OwnFont(9f),
                Padding = new Padding(22)
            };
            UiTheme.EnableDarkTitleBar(dlg);

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 7, Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 276f));

            var hdr = new Label { Text = "填写检测报告信息并选择导出范围", Font = OwnFont(10.5f, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            root.Controls.Add(hdr, 0, 0);
            root.SetColumnSpan(hdr, 2);

            (Label Lbl, TextBox Box) AddRow(int rowIdx, string label, string defVal)
            {
                var lbl = new Label { Text = label, ForeColor = UiTheme.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) };
                root.Controls.Add(lbl, 0, rowIdx);
                var field = new TextBoxField(defVal, OwnFont(9f), 28) { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
                root.Controls.Add(field, 1, rowIdx);
                return (lbl, field.Inner);
            }
            bool hasMmAll = doneIds.Any(id => _items[id].Summary!.Unit == "mm");
            string defUnitAll = hasMmAll ? "mm" : "px";
            string defLim = hasMmAll ? "0.30" : "15.0";
            var (_, tbProj) = AddRow(1, "工程 / 项目名称", "结构表面裂缝专项检测");
            var (_, tbLoc) = AddRow(2, "检测部位 / 构件", "待测结构表面");
            var (_, tbInsp) = AddRow(3, "检测 / 复核人员", "检测工程师");
            var (lblWarn, tbWarn) = AddRow(4, $"缝宽超限预警值 ({defUnitAll})", defLim);

            root.Controls.Add(new Label { Text = "报告范围", ForeColor = UiTheme.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 12, 10) }, 0, 5);
            var scopeFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = UiTheme.Surface, Margin = new Padding(0, 8, 0, 8) };
            var rbAll = new ModernRadioButton { Text = $"全部已检 ({doneIds.Count} 张)", Checked = true, AutoSize = true, Margin = new Padding(0, 0, 14, 0) };
            var rbCur = new ModernRadioButton { Text = "仅当前图像", Enabled = _currentId != null && doneIds.Contains(_currentId), AutoSize = true };
            scopeFlow.Controls.Add(rbAll);
            scopeFlow.Controls.Add(rbCur);
            root.Controls.Add(scopeFlow, 1, 5);

            string lastAutoLim = defLim;
            void SyncWarnUnit()
            {
                bool useMm = rbCur.Checked && _currentId != null ? _items[_currentId].Summary!.Unit == "mm" : hasMmAll;
                string u = useMm ? "mm" : "px";
                string nextLim = useMm ? "0.30" : "15.0";
                lblWarn.Text = $"缝宽超限预警值 ({u})";
                if (tbWarn.Text.Trim() == lastAutoLim) tbWarn.Text = nextLim;
                lastAutoLim = nextLim;
            }
            rbAll.CheckedChanged += (_, _) => SyncWarnUnit();
            rbCur.CheckedChanged += (_, _) => SyncWarnUnit();

            var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTheme.Surface, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            var btnGen = MakeButton("选择路径并生成报告", IndustrialIconKind.ReportDoc, 14, 32);
            btnGen.ApplySolidAccent(UiTheme.Primary, UiTheme.PrimaryHover, Color.White);
            var btnCan = MakeButton("取消", IndustrialIconKind.None, 14, 32, UiTheme.RadiusControl, 9f, FontStyle.Regular);
            btnCan.ApplyStudioSecondary();
            btnCan.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel;
            btnGen.Click += (_, _) =>
            {
                if (!double.TryParse(tbWarn.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v) || v <= 0)
                {
                    MessageBox.Show(dlg, "请输入大于 0 的有效预警阈值！", "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    tbWarn.Focus();
                    return;
                }
                dlg.DialogResult = DialogResult.OK;
            };
            btnRow.Controls.Add(btnGen);
            btnRow.Controls.Add(btnCan);
            root.Controls.Add(btnRow, 0, 6);
            root.SetColumnSpan(btnRow, 2);

            dlg.Controls.Add(root);
            dlg.AcceptButton = btnGen;
            dlg.CancelButton = btnCan;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            if (!double.TryParse(tbWarn.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double warnLim) || !double.IsFinite(warnLim) || warnLim <= 0)
            {
                MessageBox.Show(this, "请输入大于 0 的有效预警阈值！", "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string proj = tbProj.Text.Trim(), loc = tbLoc.Text.Trim(), insp = tbInsp.Text.Trim();
            var targetIds = rbCur.Checked && _currentId != null ? new List<string> { _currentId } : doneIds;
            using var sfd = new SaveFileDialog { Title = "导出图文检测报告", FileName = $"裂缝检测报告_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML 打印级报告 (可一键转存 PDF)|*.html|Word 可编辑图文报告 (.doc)|*.doc" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            string savePath = sfd.FileName; var p = ReadParams(); bool showWmax = _chkShowWmax.Checked;
            _cancelFlag = false; SetBusy(true); _progressBar.Value = 0;
            Task.Run(() =>
            {
                try
                {
                    var bundle = new List<(QueueItem, string, string)>(targetIds.Count);
                    for (int i = 0; i < targetIds.Count; i++)
                    {
                        if (_cancelFlag) throw new OperationCanceledException();
                        string iid = targetIds[i]; var it = _items[iid]; int step = i + 1, tot = targetIds.Count;
                        BeginInvoke(new Action(() => { if (!IsDisposed) { SetStatus($"[{step}/{tot}] 正在排版报告配图: {it.FileName} ...", UiTheme.Cyan); _progressBar.Value = (int)Math.Round((step / (double)(tot + 1)) * 1000); } }));
                        var (orig, res, msk) = BuildExportTripleCloned(iid, p.LabelStyle, showWmax);
                        using (orig) using (res) using (msk) bundle.Add((it, ImgCodec.ToJpegBase64(orig), ImgCodec.ToJpegBase64(res)));
                    }
                    if (_cancelFlag) throw new OperationCanceledException();
                    ReportExporter.ExportInspectionReport(savePath, proj, loc, insp, warnLim, bundle);
                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        if (_closePending) return;
                        _progressBar.Value = 1000; SetStatus($"检测报告已生成: {savePath}", UiTheme.Success);
                        if (MessageBox.Show(this, $"检测报告已导出至：\n{savePath}\n\n是否立即打开查看？", "报告生成成功", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo(savePath) { UseShellExecute = true });
                    }));
                }
                catch (OperationCanceledException)
                {
                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        if (!_closePending) SetStatus("报告生成已取消", UiTheme.Warning);
                    }));
                }
                catch (Exception ex)
                {
                    string err = ex.Message;
                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        if (_closePending) return;
                        SetStatus($"报告生成失败: {err}", UiTheme.Danger);
                        MessageBox.Show(this, err, "报告生成失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void BatchExportDialog()
        {
            if (_isBusy || _itemOrder.Count == 0) { MessageBox.Show(this, "队列为空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var undone = _itemOrder.Where(id => _items[id].Summary == null).ToList();
            if (undone.Count > 0)
            {
                var ans = MessageBox.Show(this, $"有 {undone.Count} 张图像尚未检测。\n【是】自动检测后导出全部；【否】仅导出已检图像；【取消】返回。", "未检测图像提示", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return;
                if (ans == DialogResult.Yes)
                {
                    using var fbd1 = new FolderBrowserDialog { Description = "选择批量导出目录" };
                    if (fbd1.ShowDialog(this) == DialogResult.OK) { string dir = fbd1.SelectedPath; RunInferenceWorker(undone, () => ExecBatchExport(dir)); }
                    return;
                }
            }
            using var fbd = new FolderBrowserDialog { Description = "选择批量导出目录" };
            if (fbd.ShowDialog(this) == DialogResult.OK) ExecBatchExport(fbd.SelectedPath);
        }

        private void ExecBatchExport(string outDir)
        {
            var doneIds = _itemOrder.Where(id => _items[id].Summary != null).ToList();
            if (doneIds.Count == 0) return;
            bool saveMask = _chkExportMask.Checked, saveDxf = _chkExportDxf.Checked, showWmax = _chkShowWmax.Checked;
            var p = ReadParams(); _cancelFlag = false; SetBusy(true); _progressBar.Value = 0;

            Task.Run(() =>
            {
                try
                {
                    string imgDir = Path.Combine(outDir, "标注结果图"), mskDir = Path.Combine(outDir, "二值掩码图"), dxfDir = Path.Combine(outDir, "CAD矢量图_DXF");
                    Directory.CreateDirectory(imgDir);
                    if (saveMask) Directory.CreateDirectory(mskDir);
                    if (saveDxf) Directory.CreateDirectory(dxfDir);

                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var records = new List<QueueItem>(doneIds.Count);
                    var repBundle = new List<(QueueItem, string, string)>(doneIds.Count);

                    for (int i = 0; i < doneIds.Count; i++)
                    {
                        if (_cancelFlag) throw new OperationCanceledException();
                        string iid = doneIds[i]; var it = _items[iid]; records.Add(it);
                        int step = i + 1, tot = doneIds.Count;
                        BeginInvoke(new Action(() => { if (!IsDisposed) { SetStatus($"[{step}/{tot}] 正在导出图像/矢量/报表: {it.FileName} ...", UiTheme.Cyan); _progressBar.Value = (int)Math.Round((step / (double)(tot + 1)) * 1000); } }));

                        var (orig, res, msk) = BuildExportTripleCloned(iid, p.LabelStyle, showWmax);
                        using (orig) using (res) using (msk)
                        {
                            repBundle.Add((it, ImgCodec.ToJpegBase64(orig), ImgCodec.ToJpegBase64(res)));
                            string stem = Path.GetFileNameWithoutExtension(it.FileName), ext = Path.GetExtension(it.FileName).ToLowerInvariant();
                            if (ext is not (".jpg" or ".jpeg" or ".png" or ".bmp")) ext = ".jpg";
                            string cand = stem; int dup = 1;
                            while (!usedNames.Add(cand)) cand = $"{stem}_{++dup}";
                            ImgCodec.WriteImg(Path.Combine(imgDir, $"{cand}_result{ext}"), res);
                            if (saveMask && msk != null) ImgCodec.WriteImg(Path.Combine(mskDir, $"{cand}_mask.png"), msk);
                            if (saveDxf) ReportExporter.ExportDxf(Path.Combine(dxfDir, $"{cand}_cracks.dxf"), it.Summary!, it.Cracks, msk);
                        }
                    }

                    if (_cancelFlag) throw new OperationCanceledException();
                    string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    ReportExporter.ExportExcel(Path.Combine(outDir, $"裂缝测量数据表_{ts}.xlsx"), records);
                    double defLim = records.Any(r => r.Summary!.Unit == "mm") ? 0.30 : 15.0;
                    ReportExporter.ExportInspectionReport(Path.Combine(outDir, $"裂缝图文检测报告_{ts}.html"), "批量裂缝检测项目", "详见各图像样本", "系统自动汇总", defLim, repBundle);

                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        if (_closePending) return;
                        _progressBar.Value = 1000; SetStatus($"批量导出全部完成：{outDir}", UiTheme.Success);
                        string extra = saveDxf ? "\n• AutoCAD (.dxf) 矢量图" : "";
                        if (MessageBox.Show(this, $"已成功导出至：\n{outDir}\n\n• 标注结果图 ({records.Count}张)\n• Excel 汇总与明细表\n• HTML 图文检测报告{extra}\n\n是否立即打开文件夹？", "批量导出完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo("explorer.exe", outDir) { UseShellExecute = true });
                    }));
                }
                catch (OperationCanceledException)
                {
                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        if (!_closePending) SetStatus("批量导出已取消", UiTheme.Warning);
                    }));
                }
                catch (Exception ex)
                {
                    string err = ex.Message;
                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        if (_closePending) return;
                        SetStatus($"批量导出失败: {err}", UiTheme.Danger);
                        MessageBox.Show(this, err, "批量导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }
    }
}
