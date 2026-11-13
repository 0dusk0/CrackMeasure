using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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

namespace CrackMeasurePro
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

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

        private volatile bool _isBusy, _cancelFlag;
        private bool _suppressSelectionEvent;

        private Label _lblModelBadge = null!, _lblStatus = null!, _lblQueueCount = null!;
        private Label _lblProbVal = null!, _lblBrushVal = null!, _lblZoomInfo = null!;
        private Label _kpiCount = null!, _kpiLen = null!, _kpiWmax = null!, _kpiWavg = null!;
        private ProgressBar _progressBar = null!;
        private Button _btnOpenFiles = null!, _btnOpenFolder = null!, _btnDetectCur = null!, _btnDetectBatch = null!, _btnStop = null!;
        private Button _btnSaveCur = null!, _btnSaveDxf = null!, _btnReport = null!, _btnExportBatch = null!;
        private DataGridView _gridQueue = null!, _gridDetail = null!;
        private CheckBox _chkTileMode = null!, _chkExportMask = null!, _chkExportDxf = null!, _chkShowWmax = null!;
        private TrackBar _trkProb = null!, _trkBrush = null!;
        private TextBox _txtScale = null!;
        private NumericUpDown _numMinArea = null!, _numBridge = null!;
        private ComboBox _cmbLabelStyle = null!;
        private readonly Dictionary<string, Button> _viewBtns = new();
        private readonly Dictionary<string, Button> _toolBtns = new();
        private BufferedCanvas _canvas = null!;
        private readonly GdiFont _canvasFont = new("Microsoft YaHei UI", 9f, FontStyle.Bold);

        private sealed record HoverHit(int Cid, float Sx, float Sy, PointF P1, PointF P2, float WPx, double WVal, string Unit, string Orient);

        public MainForm()
        {
            Text = "表面裂缝分割与测量系统 (C#)";
            Size = new GdiSize(1580, 920);
            MinimumSize = new GdiSize(1280, 750);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new GdiFont("Microsoft YaHei UI", 9f);
            KeyPreview = true;

            BuildUi();
            Shown += (_, _) => InitModel();
            FormClosing += (_, _) =>
            {
                _segmenter.Dispose();
                _canvasFont.Dispose();
                lock (_cacheLock)
                {
                    foreach (var kv in _cache.Values) kv.Dispose();
                    _cache.Clear();
                }
            };
        }

        private static Button MakeFlatBtn(string text, Color bg, Color fg, int padX = 10, int height = 34)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, MinimumSize = new GdiSize(0, height),
                FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg, Cursor = Cursors.Hand,
                Font = new GdiFont("Microsoft YaHei UI", 9f, FontStyle.Bold),
                Padding = new Padding(padX, 2, padX, 2), Margin = new Padding(2, 2, 2, 2)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void BuildUi()
        {
            var topBar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.White, Padding = new Padding(10, 6, 10, 6) };
            var leftFlow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Color.White };
            var rightFlow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };

            var titlePanel = new Panel { Width = 230, Height = 42, BackColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
            titlePanel.Controls.Add(new Label { Text = "裂缝测量", Font = new GdiFont("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), AutoSize = true, Location = new GdiPoint(0, 0) });
            _lblModelBadge = new Label { Text = "正在加载模型...", Font = new GdiFont("Microsoft YaHei UI", 8f), ForeColor = Color.FromArgb(100, 116, 139), AutoSize = true, Location = new GdiPoint(0, 24) };
            titlePanel.Controls.Add(_lblModelBadge);
            leftFlow.Controls.Add(titlePanel);

            _btnOpenFiles = MakeFlatBtn("+ 导入图片", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59));
            _btnOpenFiles.Click += (_, _) => ImportImages();
            _btnOpenFolder = MakeFlatBtn("+ 导入文件夹", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59));
            _btnOpenFolder.Click += (_, _) => ImportFolder();
            _btnDetectCur = MakeFlatBtn("▶ 检测当前图", Color.FromArgb(37, 99, 235), Color.White);
            _btnDetectCur.Click += (_, _) => DetectCurrentAsync();
            _btnDetectBatch = MakeFlatBtn("⚡ 批量检测", Color.FromArgb(220, 38, 38), Color.White);
            _btnDetectBatch.Click += (_, _) => DetectBatchAsync();
            _btnStop = MakeFlatBtn("■ 停止", Color.FromArgb(217, 119, 6), Color.White);
            _btnStop.Enabled = false;
            _btnStop.Click += (_, _) => { if (_isBusy) { _cancelFlag = true; _lblStatus.Text = "正在等待当前图片完成后安全停止..."; } };
            leftFlow.Controls.AddRange(new Control[] { _btnOpenFiles, _btnOpenFolder, _btnDetectCur, _btnDetectBatch, _btnStop });

            _btnExportBatch = MakeFlatBtn("批量导出(图+表)", Color.FromArgb(22, 163, 74), Color.White);
            _btnExportBatch.Click += (_, _) => BatchExportDialog();
            _btnReport = MakeFlatBtn("导出报告", Color.FromArgb(124, 58, 237), Color.White);
            _btnReport.Click += (_, _) => ExportReportDialog();
            _btnSaveDxf = MakeFlatBtn("导出CAD(.dxf)", Color.FromArgb(13, 148, 136), Color.White);
            _btnSaveDxf.Click += (_, _) => SaveCurrentDxf();
            _btnSaveCur = MakeFlatBtn("保存当前图", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59));
            _btnSaveCur.Click += (_, _) => SaveCurrentImage();
            rightFlow.Controls.AddRange(new Control[] { _btnExportBatch, _btnReport, _btnSaveDxf, _btnSaveCur });

            topBar.Controls.Add(leftFlow);
            topBar.Controls.Add(rightFlow);

            var botBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.White, Padding = new Padding(12, 4, 12, 4) };
            _lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(51, 65, 85), Text = "就绪：支持亚像素法向测宽、悬停局部缝宽探针、画笔/橡皮擦修补掩码 (Ctrl+Z撤销) 及 CAD(.dxf) 导出。" };
            _progressBar = new ProgressBar { Dock = DockStyle.Right, Width = 220, Maximum = 1000 };
            botBar.Controls.Add(_lblStatus);
            botBar.Controls.Add(_progressBar);

            var workArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            // 左栏 (295px)
            var leftCol = new Panel { Dock = DockStyle.Left, Width = 295, Padding = new Padding(0, 0, 8, 0) };
            var grpParam = new GroupBox { Text = " 测量参数与批量导出项 ", Dock = DockStyle.Bottom, Height = 315, BackColor = Color.White, Font = new GdiFont("Microsoft YaHei UI", 9f, FontStyle.Bold) };
            BuildLeftParamControls(grpParam);
            var grpQueue = new GroupBox { Text = " 图像队列 (点击切换) ", Dock = DockStyle.Fill, BackColor = Color.White, Font = new GdiFont("Microsoft YaHei UI", 9f, FontStyle.Bold), Padding = new Padding(8, 20, 8, 8) };
            BuildLeftQueueControls(grpQueue);
            leftCol.Controls.Add(grpQueue);
            leftCol.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 6 });
            leftCol.Controls.Add(grpParam);

            // 右栏 (435px)
            var rightCol = new Panel { Dock = DockStyle.Right, Width = 435, Padding = new Padding(8, 0, 0, 0) };
            var grpKpi = new GroupBox { Text = " 当前图像测量概览 ", Dock = DockStyle.Top, Height = 148, BackColor = Color.White, Font = new GdiFont("Microsoft YaHei UI", 9f, FontStyle.Bold) };
            BuildRightKpiCards(grpKpi);
            var grpDetail = new GroupBox { Text = " 裂缝明细表 (点击表格或图中裂缝联动) ", Dock = DockStyle.Fill, BackColor = Color.White, Font = new GdiFont("Microsoft YaHei UI", 9f, FontStyle.Bold), Padding = new Padding(8, 22, 8, 8) };
            BuildRightDetailGrid(grpDetail);
            rightCol.Controls.Add(grpDetail);
            rightCol.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6 });
            rightCol.Controls.Add(grpKpi);

            var centerCol = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42) };
            BuildCenterCanvasArea(centerCol);

            workArea.Controls.Add(centerCol);
            workArea.Controls.Add(leftCol);
            workArea.Controls.Add(rightCol);

            Controls.Add(workArea);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(226, 232, 240) });
            Controls.Add(topBar);
            Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(226, 232, 240) });
            Controls.Add(botBar);
        }

        private void BuildLeftQueueControls(GroupBox parent)
        {
            var botRow = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Color.White };
            _lblQueueCount = new Label { Text = "共 0 张", AutoSize = true, Location = new GdiPoint(2, 8), Font = new GdiFont("Microsoft YaHei UI", 9f), ForeColor = Color.FromArgb(51, 65, 85) };
            var btnClear = MakeFlatBtn("清空", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 8, 26);
            btnClear.Dock = DockStyle.Right;
            btnClear.Click += (_, _) => ClearQueue();
            var btnRem = MakeFlatBtn("移除", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 8, 26);
            btnRem.Dock = DockStyle.Right;
            btnRem.Click += (_, _) => RemoveSelectedItem();
            botRow.Controls.Add(_lblQueueCount);
            botRow.Controls.Add(btnRem);
            botRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 4 });
            botRow.Controls.Add(btnClear);

            _gridQueue = CreateStyledGrid();
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 34, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "文件名", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Width = 56, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "条数", Width = 44, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
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
                }
            };

            parent.Controls.Add(_gridQueue);
            parent.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 6 });
            parent.Controls.Add(botRow);
        }

        private void BuildLeftParamControls(GroupBox parent)
        {
            var regular = new GdiFont("Microsoft YaHei UI", 9f);
            int y = 22;

            _chkTileMode = new CheckBox { Text = "高清重叠切片推理 (大图保留细裂缝)", Checked = true, Font = regular, AutoSize = true, Location = new GdiPoint(10, y) };
            parent.Controls.Add(_chkTileMode);
            y += 26;

            parent.Controls.Add(new Label { Text = "分割概率阈值:", Font = regular, AutoSize = true, Location = new GdiPoint(10, y) });
            _lblProbVal = new Label { Text = "0.45", Font = new GdiFont("Consolas", 9.5f, FontStyle.Bold), AutoSize = true, Location = new GdiPoint(235, y) };
            parent.Controls.Add(_lblProbVal);
            y += 18;

            _trkProb = new TrackBar { Minimum = 15, Maximum = 90, Value = 45, TickStyle = TickStyle.None, AutoSize = false, Height = 24, Width = 266, Location = new GdiPoint(8, y) };
            _trkProb.Scroll += (_, _) => _lblProbVal.Text = (_trkProb.Value / 100.0).ToString("F2");
            parent.Controls.Add(_trkProb);
            y += 28;

            parent.Controls.Add(new Label { Text = "比例尺 (mm/px):", Font = regular, AutoSize = true, Location = new GdiPoint(10, y + 4) });
            _txtScale = new TextBox { Text = "0", Font = regular, Width = 76, Location = new GdiPoint(118, y + 1) };
            var btnCalib = MakeFlatBtn("⌖ 图上标定", Color.FromArgb(2, 132, 199), Color.White, 5, 25);
            btnCalib.Font = new GdiFont("Microsoft YaHei UI", 8f, FontStyle.Bold);
            btnCalib.Location = new GdiPoint(198, y);
            btnCalib.Click += (_, _) => SetActiveTool("calib");
            parent.Controls.Add(_txtScale);
            parent.Controls.Add(btnCalib);
            y += 30;

            parent.Controls.Add(new Label { Text = "最小面积 (px²):", Font = regular, AutoSize = true, Location = new GdiPoint(10, y + 3) });
            _numMinArea = new NumericUpDown { Minimum = 5, Maximum = 5000, Increment = 5, Value = 35, Font = regular, Width = 156, Location = new GdiPoint(118, y) };
            parent.Controls.Add(_numMinArea);
            y += 29;

            parent.Controls.Add(new Label { Text = "断裂桥接核 (px):", Font = regular, AutoSize = true, Location = new GdiPoint(10, y + 3) });
            _numBridge = new NumericUpDown { Minimum = 1, Maximum = 25, Increment = 2, Value = 5, Font = regular, Width = 156, Location = new GdiPoint(118, y) };
            parent.Controls.Add(_numBridge);
            y += 31;

            var btnFast = MakeFlatBtn("⚡ 应用新阈值/比例尺重算 (免重推)", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 6, 30);
            btnFast.AutoSize = false;
            btnFast.Width = 266;
            btnFast.Location = new GdiPoint(10, y);
            btnFast.Click += (_, _) => FastReprocessCurrent();
            parent.Controls.Add(btnFast);
            y += 36;

            _chkExportMask = new CheckBox { Text = "批量导出附带二值掩码 (_mask.png)", Font = regular, AutoSize = true, Location = new GdiPoint(10, y) };
            parent.Controls.Add(_chkExportMask);
            y += 22;

            _chkExportDxf = new CheckBox { Text = "批量导出附带 CAD 矢量图 (.dxf)", Checked = true, Font = regular, AutoSize = true, Location = new GdiPoint(10, y) };
            parent.Controls.Add(_chkExportDxf);
        }

        private void BuildRightKpiCards(GroupBox parent)
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, Padding = new Padding(6, 4, 6, 6) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _kpiCount = AddKpiCard(grid, 0, 0, "检出裂缝数", Color.FromArgb(37, 99, 235));
            _kpiLen = AddKpiCard(grid, 0, 1, "裂缝总长 (∑L)", Color.FromArgb(8, 145, 178));
            _kpiWmax = AddKpiCard(grid, 1, 0, "全局最大宽 (Wmax)", Color.FromArgb(220, 38, 38));
            _kpiWavg = AddKpiCard(grid, 1, 1, "加权均宽 / 分叉数", Color.FromArgb(124, 58, 237));
            parent.Controls.Add(grid);
        }

        private static Label AddKpiCard(TableLayoutPanel grid, int r, int c, string title, Color valColor)
        {
            var box = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252), Margin = new Padding(3), BorderStyle = BorderStyle.FixedSingle };
            box.Controls.Add(new Label { Text = title, Font = new GdiFont("Microsoft YaHei UI", 8f), ForeColor = Color.FromArgb(100, 116, 139), AutoSize = true, Location = new GdiPoint(8, 5) });
            var valLbl = new Label { Text = "-", Font = new GdiFont("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = valColor, AutoSize = true, Location = new GdiPoint(8, 25) };
            box.Controls.Add(valLbl);
            grid.Controls.Add(box, c, r);
            return valLbl;
        }

        private void BuildRightDetailGrid(GroupBox parent)
        {
            _gridDetail = CreateStyledGrid();
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "编号", Width = 44, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "走向", Width = 68, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "长度 L", Width = 64, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "均宽 W", Width = 64, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "最大宽", Width = 68, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分叉", Width = 40, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _gridDetail.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "面积", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _gridDetail.SelectionChanged += (_, _) =>
            {
                if (_suppressSelectionEvent || _gridDetail.SelectedRows.Count == 0) return;
                if (_gridDetail.SelectedRows[0].Tag is int cid) { _selectedCrackId = cid; _canvas.Invalidate(); }
            };
            parent.Controls.Add(_gridDetail);
        }

        private static DataGridView CreateStyledGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
                RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, ReadOnly = true, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(241, 245, 249), Font = new GdiFont("Microsoft YaHei UI", 8.8f)
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(51, 65, 85);
            g.ColumnHeadersDefaultCellStyle.Font = new GdiFont("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
            g.ColumnHeadersHeight = 28;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            g.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 64, 175);
            g.RowTemplate.Height = 25;
            return g;
        }

        private void BuildCenterCanvasArea(Panel centerCol)
        {
            var bar1 = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.FromArgb(30, 41, 59), Padding = new Padding(6, 4, 6, 4) };
            var bar1Left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            var bar1Right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.Transparent };

            foreach (var (txt, mode) in new[] { ("标注图", "result"), ("原图", "orig"), ("掩码", "mask"), ("对比", "compare") })
            {
                var b = MakeFlatBtn(txt, Color.FromArgb(51, 65, 85), Color.White, 6, 27);
                b.Font = new GdiFont("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
                b.Click += (_, _) => { _viewMode = mode; SyncViewButtons(); FitToWindow(); };
                _viewBtns[mode] = b;
                bar1Left.Controls.Add(b);
            }
            SyncViewButtons();

            bar1Left.Controls.Add(new Label { Text = " | 标签:", ForeColor = Color.FromArgb(148, 163, 184), AutoSize = true, Margin = new Padding(4, 7, 2, 0) });
            _cmbLabelStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 98, Margin = new Padding(2, 4, 4, 0) };
            _cmbLabelStyle.Items.AddRange(new object[] { "精简编号 (#1)", "完整测量参数", "纯净无文字" });
            _cmbLabelStyle.SelectedIndex = 0;
            _cmbLabelStyle.SelectedIndexChanged += (_, _) => _canvas.Invalidate();
            bar1Left.Controls.Add(_cmbLabelStyle);

            _chkShowWmax = new CheckBox { Text = "⊕ Wmax靶标", Checked = true, ForeColor = Color.White, AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            _chkShowWmax.CheckedChanged += (_, _) => RefreshOverlayWmax();
            bar1Left.Controls.Add(_chkShowWmax);

            var btn100 = MakeFlatBtn("1:1", Color.FromArgb(51, 65, 85), Color.White, 8, 26);
            btn100.Click += (_, _) => Zoom100();
            var btnFit = MakeFlatBtn("适应窗口", Color.FromArgb(51, 65, 85), Color.White, 8, 26);
            btnFit.Click += (_, _) => FitToWindow();
            _lblZoomInfo = new Label { Text = "100%", ForeColor = Color.FromArgb(56, 189, 248), Font = new GdiFont("Consolas", 10f, FontStyle.Bold), AutoSize = true, Margin = new Padding(6, 6, 6, 0) };
            bar1Right.Controls.AddRange(new Control[] { btn100, btnFit, _lblZoomInfo });

            // 先加 Fill 再加 Right，保证右侧按钮永不被左侧遮挡
            bar1.Controls.Add(bar1Left);
            bar1.Controls.Add(bar1Right);

            var bar2 = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(23, 32, 51), Padding = new Padding(6, 3, 6, 3) };
            var bar2Left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            var bar2Right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };

            foreach (var (key, txt) in new[] { ("pan", "漫游/探针"), ("calib", "⌖ 标定比例尺"), ("ruler", "自由卡尺"), ("brush", "✎ 补画裂缝"), ("eraser", "⌫ 擦除伪影") })
            {
                var b = MakeFlatBtn(txt, Color.FromArgb(51, 65, 85), Color.White, 7, 26);
                b.Font = new GdiFont("Microsoft YaHei UI", 8.2f, FontStyle.Bold);
                b.Click += (_, _) => SetActiveTool(key);
                _toolBtns[key] = b;
                bar2Left.Controls.Add(b);
            }
            SyncToolButtons();

            bar2Left.Controls.Add(new Label { Text = " | 笔刷:", ForeColor = Color.FromArgb(148, 163, 184), AutoSize = true, Margin = new Padding(6, 6, 2, 0) });
            _lblBrushVal = new Label { Text = "6 px", ForeColor = Color.White, Font = new GdiFont("Consolas", 9f), Width = 42, Margin = new Padding(2, 6, 0, 0) };
            _trkBrush = new TrackBar { Minimum = 1, Maximum = 35, Value = 6, TickStyle = TickStyle.None, AutoSize = false, Width = 78, Height = 24, BackColor = Color.FromArgb(23, 32, 51), Margin = new Padding(0, 3, 0, 0) };
            _trkBrush.Scroll += (_, _) => { _lblBrushVal.Text = $"{_trkBrush.Value} px"; _canvas.Invalidate(); };
            bar2Left.Controls.Add(_lblBrushVal);
            bar2Left.Controls.Add(_trkBrush);

            var btnUndo = MakeFlatBtn("↩ 撤销 (Ctrl+Z)", Color.FromArgb(51, 65, 85), Color.White, 8, 26);
            btnUndo.Font = new GdiFont("Microsoft YaHei UI", 8.2f);
            btnUndo.Click += (_, _) => UndoMaskEdit();
            bar2Right.Controls.Add(btnUndo);
            bar2.Controls.Add(bar2Left);
            bar2.Controls.Add(bar2Right);

            _canvas = new BufferedCanvas { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), Cursor = Cursors.Hand };
            _canvas.Paint += OnCanvasPaint;
            _canvas.Resize += (_, _) => { if (_fitMode) FitToWindow(); else _canvas.Invalidate(); };
            _canvas.MouseDown += OnCanvasMouseDown;
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseUp += OnCanvasMouseUp;
            _canvas.MouseLeave += (_, _) => { if (_hoverProbe != null || _cursorImgXy != null) { _hoverProbe = null; _cursorImgXy = null; _canvas.Invalidate(); } };
            _canvas.MouseWheel += OnCanvasMouseWheel;
            _canvas.DoubleClick += (_, _) => { if (_activeTool == "pan") FitToWindow(); };

            centerCol.Controls.Add(_canvas);
            centerCol.Controls.Add(bar2);
            centerCol.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(15, 23, 42) });
            centerCol.Controls.Add(bar1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (ActiveControl is TextBox or NumericUpDown) return;
            if (e.KeyCode == Keys.Escape) { SetActiveTool("pan"); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Z) { UndoMaskEdit(); e.Handled = true; }
            else if (e.KeyCode == Keys.OemOpenBrackets) { StepBrush(-2); e.Handled = true; }
            else if (e.KeyCode == Keys.OemCloseBrackets) { StepBrush(2); e.Handled = true; }
        }

        private void StepBrush(int delta)
        {
            _trkBrush.Value = Math.Clamp(_trkBrush.Value + delta, _trkBrush.Minimum, _trkBrush.Maximum);
            _lblBrushVal.Text = $"{_trkBrush.Value} px";
            if (_activeTool is "brush" or "eraser") _canvas.Invalidate();
        }

        private void InitModel()
        {
            string modelPath = ImgCodec.ResolveAsset("crack_seg.onnx");
            if (!File.Exists(modelPath))
            {
                _lblModelBadge.Text = "⚠ 未找到 crack_seg.onnx";
                _lblStatus.Text = $"未找到模型文件: {modelPath}（请将 crack_seg.onnx 放入程序目录）";
                return;
            }
            try
            {
                _segmenter.LoadModel(modelPath);
                string fmt = _segmenter.IsNhwc ? "NHWC" : "NCHW";
                _lblModelBadge.Text = $"✔ 模型就绪 | {fmt} | {_segmenter.DeviceLabel}";
                _lblStatus.Text = $"模型加载成功，当前推理后端：{_segmenter.DeviceLabel}";
            }
            catch (Exception ex)
            {
                _lblModelBadge.Text = "✖ 模型加载失败";
                MessageBox.Show(this, ex.Message, "模型加载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PutCache(string iid, Mat? orig = null, Mat? probU8 = null, string? modeTag = null, Mat? overlay = null, Mat? mask = null)
        {
            List<CacheEntry> toDispose = new();
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(iid, out var entry)) { entry = new CacheEntry(); _cache[iid] = entry; }
                if (orig != null && !ReferenceEquals(entry.OrigImg, orig)) { entry.OrigImg?.Dispose(); entry.OrigImg = orig; }
                if (probU8 != null && !ReferenceEquals(entry.ProbU8, probU8)) { entry.ProbU8?.Dispose(); entry.ProbU8 = probU8; }
                if (modeTag != null) entry.ModeTag = modeTag;
                if (overlay != null && !ReferenceEquals(entry.OverlayImg, overlay)) { entry.OverlayImg?.Dispose(); entry.OverlayImg = overlay; }
                if (mask != null && !ReferenceEquals(entry.MaskImg, mask)) { entry.MaskImg?.Dispose(); entry.MaskImg = mask; }

                _lruList.Remove(iid);
                _lruList.AddLast(iid);

                while (_cache.Count > MaxMemoryCache && _lruList.First != null)
                {
                    string oldest = _lruList.First.Value;
                    _lruList.RemoveFirst();
                    if (oldest == _currentId)
                    {
                        _lruList.AddLast(oldest);
                        if (_cache.Count > MaxMemoryCache && _lruList.First != null && _lruList.First.Value != _currentId)
                        {
                            string nextOld = _lruList.First.Value;
                            _lruList.RemoveFirst();
                            if (_cache.Remove(nextOld, out var ev2)) toDispose.Add(ev2);
                        }
                        break;
                    }
                    if (_cache.Remove(oldest, out var ev)) toDispose.Add(ev);
                }
            }
            if (toDispose.Count > 0)
                BeginInvoke(new Action(() => { foreach (var d in toDispose) d.Dispose(); }));
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

        private void SyncViewButtons()
        {
            foreach (var (k, b) in _viewBtns)
                b.BackColor = k == _viewMode ? Color.FromArgb(37, 99, 235) : Color.FromArgb(51, 65, 85);
        }

        private void SyncToolButtons()
        {
            var pal = new Dictionary<string, Color>
            {
                ["pan"] = Color.FromArgb(37, 99, 235), ["calib"] = Color.FromArgb(2, 132, 199),
                ["ruler"] = Color.FromArgb(13, 148, 136), ["brush"] = Color.FromArgb(22, 163, 74), ["eraser"] = Color.FromArgb(220, 38, 38)
            };
            foreach (var (k, b) in _toolBtns)
                b.BackColor = k == _activeTool ? pal[k] : Color.FromArgb(51, 65, 85);
        }

        private void SetActiveTool(string tool)
        {
            if (_isBusy) return;
            if (tool != "pan" && _currentId == null) { MessageBox.Show(this, "请先导入并选择一张图片！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (tool is "brush" or "eraser" && (_currentId == null || _items[_currentId].Summary == null))
            {
                MessageBox.Show(this, "请先点击【▶ 检测当前图】，随后即可用画笔或橡皮擦微调掩码！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            _lblStatus.Text = tool switch
            {
                "calib" => "【比例尺标定】在已知长度参照物两端拖拽或点击两点输入真实毫米（右键 / ESC 退出）。",
                "ruler" => "【自由游标卡尺】在任意两点拖拽或点击即可测量像素与物理距离，不改变全局比例尺（右键 / ESC 退出）。",
                "brush" => "【补画裂缝】左键涂抹补连断缝或漏检细缝（[ ] 键调笔刷大小，Ctrl+Z 撤销，右键退出）。",
                "eraser" => "【橡皮擦】左键涂抹擦除污渍、阴影或模板缝等误检区域（Ctrl+Z 撤销，右键退出）。",
                _ => "【漫游与探针模式】滚轮缩放、左键拖拽平移；悬停裂缝实时显示正交截面亚像素缝宽，单击裂缝联动定位表格。"
            };
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
            if (!_undoStacks.TryGetValue(_currentId, out var stack) || stack.Count == 0) { _lblStatus.Text = "当前图像没有可撤销的人工修补记录。"; return; }
            byte[] raw = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            using Mat? prev = ImgCodec.DecompressMask(raw);
            if (prev != null) ApplyEditedMask(_currentId, prev, "撤销修补");
        }

        private void CommitPaintStroke()
        {
            if (_currentId == null || _strokePts.Count == 0) return;
            Mat? orig = EnsureOrigLoaded(_currentId);
            if (orig == null) return;
            var cache = GetCacheRef(_currentId);

            PushUndoSnapshot(_currentId, cache?.MaskImg);
            using Mat edited = cache?.MaskImg != null ? cache.MaskImg.Clone() : new Mat(orig.Size(), MatType.CV_8UC1, Scalar.All(0));
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
            var p = ReadParams();
            int effArea = note == "人工补画" ? Math.Min(8, p.MinArea) : p.MinArea;
            var res = PostProcessor.AnalyzeFromMask(orig, editedMask, cache?.ProbU8, $"{cache?.ModeTag ?? "推理"}+{note}", p.ProbThresh, p.MmPerPx, effArea, p.BridgePx, _chkShowWmax.Checked);

            var item = _items[iid];
            item.Summary = res.Summary; item.Cracks = res.Cracks; item.BranchPts = res.BranchPts;
            PutCache(iid, overlay: res.OverlayImg, mask: res.MaskImg);
            UpdateQueueRow(iid, "已修补", res.Summary.CrackCount.ToString());
            UpdateRightPanel(item);
            _canvas.Invalidate();
            _lblStatus.Text = $"✔ {note}已实时重算 ({sw.Elapsed.TotalMilliseconds:F1} ms)：当前共 {res.Summary.CrackCount} 条裂缝。";
        }

        private void RefreshOverlayWmax()
        {
            if (_currentId == null) return;
            var item = _items[_currentId];
            var cache = GetCacheRef(_currentId);
            if (item.Summary == null || cache?.OrigImg == null || cache.MaskImg == null) return;
            var wpts = _chkShowWmax.Checked ? item.Cracks.Select(c => c.WmaxPt).ToList() : null;
            PutCache(_currentId, overlay: PostProcessor.RenderCleanOverlay(cache.OrigImg, cache.MaskImg, item.BranchPts, wpts));
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
            Mat? orig = EnsureOrigLoaded(_currentId);
            if (orig == null) return;
            PushUndoSnapshot(_currentId, cache.MaskImg);
            var p = ReadParams();

            var res = PostProcessor.ProcessFromProb(orig, cache.ProbU8, cache.ModeTag, p.ProbThresh, p.MmPerPx, p.MinArea, p.BridgePx, _chkShowWmax.Checked);
            var item = _items[_currentId];
            item.Summary = res.Summary; item.Cracks = res.Cracks; item.BranchPts = res.BranchPts;
            PutCache(_currentId, overlay: res.OverlayImg, mask: res.MaskImg);
            UpdateQueueRow(_currentId, "已完成", res.Summary.CrackCount.ToString());
            UpdateRightPanel(item);
            _canvas.Invalidate();
            _lblStatus.Text = $"⚡ 缓存概率图亚像素重算完成 ({sw.Elapsed.TotalMilliseconds:F1} ms)：共检出 {res.Summary.CrackCount} 条裂缝。";
        }

        private void PromptCalibrationDialog(double pxDist)
        {
            using var dlg = new Form { Text = "输入参照物真实物理长度", Size = new GdiSize(415, 255), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, BackColor = Color.White, Font = new GdiFont("Microsoft YaHei UI", 9f) };
            dlg.Controls.Add(new Label { Text = $"标定线段像素长度：{pxDist:F2} px", Font = new GdiFont("Microsoft YaHei UI", 10f, FontStyle.Bold), AutoSize = true, Location = new GdiPoint(18, 16) });
            dlg.Controls.Add(new Label { Text = "请输入该线段对应的真实物理长度 (单位 mm)：", AutoSize = true, Location = new GdiPoint(18, 44) });
            var txtVal = new TextBox { Text = "10.0", Font = new GdiFont("Consolas", 11f), Width = 360, Location = new GdiPoint(18, 68) };
            dlg.Controls.Add(txtVal);

            var presetFlow = new FlowLayoutPanel { Location = new GdiPoint(16, 100), Width = 365, Height = 30 };
            presetFlow.Controls.Add(new Label { Text = "快捷预设:", AutoSize = true, Margin = new Padding(2, 6, 4, 0), ForeColor = Color.Gray });
            foreach (int mm in new[] { 1, 5, 10, 50, 100 })
            {
                int copyMm = mm;
                var pb = MakeFlatBtn($"{mm}mm", Color.FromArgb(241, 245, 249), Color.FromArgb(51, 65, 85), 6, 24);
                pb.Font = new GdiFont("Microsoft YaHei UI", 8f);
                pb.Click += (_, _) => txtVal.Text = copyMm.ToString("F1", CultureInfo.InvariantCulture);
                presetFlow.Controls.Add(pb);
            }
            dlg.Controls.Add(presetFlow);

            var chkAll = new CheckBox { Text = "同步换算队列中所有已检测图像的物理尺寸", Checked = true, AutoSize = true, Location = new GdiPoint(18, 136) };
            dlg.Controls.Add(chkAll);
            var btnOk = MakeFlatBtn("确定并换算", Color.FromArgb(37, 99, 235), Color.White, 14, 32);
            btnOk.Location = new GdiPoint(272, 170);
            var btnCancel = MakeFlatBtn("取消", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 14, 32);
            btnCancel.Location = new GdiPoint(198, 170);
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
                _lblStatus.Text = $"比例尺标定成功：{mmPerPx:F6} mm/px（1 mm ≈ {1.0 / mmPerPx:F1} px），已刷新 {cnt} 张图像！";
            };
            dlg.Controls.AddRange(new Control[] { btnOk, btnCancel });
            dlg.AcceptButton = btnOk;
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
            var paths = Directory.EnumerateFiles(fbd.SelectedPath, "*.*", SearchOption.AllDirectories)
                .Where(f => ImgCodec.SupportedExts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToList();
            if (paths.Count > 0) AddFilePaths(paths);
            else MessageBox.Show(this, "未找到受支持的图像文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                int rIdx = _gridQueue.Rows.Add(_itemOrder.Count, qItem.FileName, "待检测", "-");
                _gridQueue.Rows[rIdx].Tag = iid;
                firstId ??= iid; added++;
            }
            _suppressSelectionEvent = false;
            RefreshQueueIndices();
            if (firstId != null && _currentId == null) { _currentId = firstId; _gridQueue.Rows[0].Selected = true; UpdateRightPanel(_items[firstId]); FitToWindow(); }
            _lblStatus.Text = $"已导入 {added} 张图片，队列共 {_items.Count} 张。";
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
            else { _currentId = null; UpdateRightPanel(null); _canvas.Invalidate(); }
        }

        private void ClearQueue()
        {
            if (_isBusy || _items.Count == 0) return;
            if (MessageBox.Show(this, "确定清空所有图像及测量数据吗？", "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _suppressSelectionEvent = true; _gridQueue.Rows.Clear(); _suppressSelectionEvent = false;
            _items.Clear(); _itemOrder.Clear(); _undoStacks.Clear();
            lock (_cacheLock) { foreach (var v in _cache.Values) v.Dispose(); _cache.Clear(); _lruList.Clear(); }
            _currentId = null; RefreshQueueIndices(); UpdateRightPanel(null); _canvas.Invalidate();
        }

        private void RefreshQueueIndices()
        {
            for (int i = 0; i < _gridQueue.Rows.Count; i++) _gridQueue.Rows[i].Cells[0].Value = i + 1;
            _lblQueueCount.Text = $"共 {_items.Count} 张 (已检 {_items.Values.Count(v => v.Summary != null)})";
        }

        private void UpdateQueueRow(string iid, string status, string countStr)
        {
            foreach (DataGridViewRow row in _gridQueue.Rows)
                if (row.Tag as string == iid) { row.Cells[2].Value = status; row.Cells[3].Value = countStr; break; }
            RefreshQueueIndices();
        }

        private void UpdateRightPanel(QueueItem? item)
        {
            _suppressSelectionEvent = true;
            _gridDetail.Rows.Clear();
            if (item?.Summary == null) { _kpiCount.Text = _kpiLen.Text = _kpiWmax.Text = _kpiWavg.Text = "-"; _suppressSelectionEvent = false; return; }
            var s = item.Summary; string u = s.Unit, wFmt = u == "mm" ? "F3" : "F2";
            _kpiCount.Text = $"{s.CrackCount} 条";
            _kpiLen.Text = $"{s.TotalLenVal:F1} {u}";
            _kpiWmax.Text = $"{s.MaxWVal.ToString(wFmt)} {u}";
            _kpiWavg.Text = $"{s.AvgWVal:F2} {u} / {s.TotalBranches}叉";
            foreach (var c in item.Cracks)
            {
                int r = _gridDetail.Rows.Add($"#{c.Id}", c.Orientation, $"{c.LenVal:F1}{c.Unit}", $"{c.AvgWVal.ToString(wFmt)}{c.Unit}", $"{c.MaxWVal.ToString(wFmt)}{c.Unit}", c.Branches, $"{c.AreaVal:F0}{c.Unit}²");
                _gridDetail.Rows[r].Tag = c.Id;
            }
            _gridDetail.ClearSelection();
            _suppressSelectionEvent = false;
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
            if (e.Button == MouseButtons.Right) { SetActiveTool("pan"); return; }
            if (e.Button != MouseButtons.Left || _currentId == null) return;
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
                    _suppressSelectionEvent = false; _canvas.Invalidate();
                }
                _panStart = null;
            }
        }

        private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
        {
            if (_currentId == null || EnsureOrigLoaded(_currentId) == null) return;
            float factor = e.Delta > 0 ? 1.18f : (1f / 1.18f), nextZoom = Math.Clamp(_zoom * factor, 0.05f, 30f);
            if (Math.Abs(nextZoom - _zoom) < 1e-6f) return;
            float ix = (e.X - _offX) / _zoom, iy = (e.Y - _offY) / _zoom;
            _fitMode = false; _zoom = nextZoom; _offX = e.X - ix * nextZoom; _offY = e.Y - iy * nextZoom;
            _canvas.Invalidate();
        }

        private (int TotalW, int TotalH, int SingleW) GetVirtualDimensions()
        {
            if (_currentId == null) return (0, 0, 0);
            Mat? orig = EnsureOrigLoaded(_currentId);
            if (orig == null) return (0, 0, 0);
            bool isCmp = _viewMode == "compare" && GetCacheRef(_currentId)?.OverlayImg != null;
            return (isCmp ? orig.Cols * 2 + 6 : orig.Cols, orig.Rows, orig.Cols);
        }

        private void FitToWindow()
        {
            var (vw, vh, _) = GetVirtualDimensions();
            if (vw <= 0 || vh <= 0) { _canvas.Invalidate(); return; }
            _fitMode = true;
            int cw = Math.Max(_canvas.Width, 100), ch = Math.Max(_canvas.Height, 100);
            _zoom = Math.Max(0.05f, Math.Min((cw - 24f) / vw, (ch - 24f) / vh));
            _offX = (cw - vw * _zoom) * 0.5f; _offY = (ch - vh * _zoom) * 0.5f;
            _canvas.Invalidate();
        }

        private void Zoom100()
        {
            var (vw, vh, _) = GetVirtualDimensions();
            if (vw <= 0 || vh <= 0) return;
            _fitMode = false; _zoom = 1.0f;
            _offX = (_canvas.Width - vw) * 0.5f; _offY = (_canvas.Height - vh) * 0.5f;
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

        private void OnCanvasPaint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (_currentId == null || !_items.TryGetValue(_currentId, out var item)) { DrawEmptyHint(g); return; }
            Mat? orig = EnsureOrigLoaded(_currentId);
            if (orig == null) { DrawEmptyHint(g); return; }

            _lblZoomInfo.Text = $"{(int)Math.Round(_zoom * 100)}%";
            var cache = GetCacheRef(_currentId);
            Mat? ov = cache?.OverlayImg, msk = cache?.MaskImg;
            int sw = orig.Cols, sh = orig.Rows;
            bool isCmp = _viewMode == "compare" && ov != null;

            if (isCmp)
            {
                DrawViewportMat(g, orig, _offX, _offY);
                using var sepBrush = new SolidBrush(Color.FromArgb(30, 41, 59));
                g.FillRectangle(sepBrush, _offX + sw * _zoom, _offY, 6f * _zoom, sh * _zoom);
                DrawViewportMat(g, ov!, _offX + (sw + 6) * _zoom, _offY);
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
                    using var normFill = new SolidBrush(Color.FromArgb(225, 15, 23, 42));
                    using var selFill = new SolidBrush(Color.FromArgb(235, 131, 24, 67));
                    foreach (var lay in layouts)
                    {
                        bool sel = lay.Id == _selectedCrackId;
                        Color bCol = sel ? Color.FromArgb(236, 72, 153) : Color.FromArgb(250, 204, 21);
                        using var bPen = new GdiPen(bCol, sel ? 2f : 1.2f);
                        using var dotBrush = new SolidBrush(bCol);
                        if (styleCode == "full" && !sel) g.DrawRectangle(fullBoxPen, lay.BBox.X, lay.BBox.Y, lay.BBox.Width, lay.BBox.Height);
                        if (lay.Leader is var (lx1, ly1, lx2, ly2)) { g.DrawLine(bPen, lx1, ly1, lx2, ly2); g.FillEllipse(dotBrush, lx2 - 2.5f, ly2 - 2.5f, 5f, 5f); }
                        g.FillRectangle(sel ? selFill : normFill, lay.Rect);
                        g.DrawRectangle(bPen, lay.Rect.X, lay.Rect.Y, lay.Rect.Width, lay.Rect.Height);
                        g.DrawString(lay.Text, _canvasFont, Brushes.White, lay.Rect.X + 5f, lay.Rect.Y + 3f);
                    }
                }
            }

            if (_activeTool == "pan" && _hoverProbe is { } hp)
            {
                using var blkPen = new GdiPen(Color.Black, 4f);
                using var cyanPen = new GdiPen(Color.FromArgb(0, 245, 212), 2f);
                using var cyanBrush = new SolidBrush(Color.FromArgb(0, 245, 212));
                using var tipBg = new SolidBrush(Color.FromArgb(235, 15, 23, 42));
                foreach (bool rp in isCmp ? new[] { false, true } : new[] { false })
                {
                    PointF s1 = ImgToScreen(hp.P1.X, hp.P1.Y, rp, sw), s2 = ImgToScreen(hp.P2.X, hp.P2.Y, rp, sw), sc = ImgToScreen(hp.Sx, hp.Sy, rp, sw);
                    g.DrawLine(blkPen, s1, s2); g.DrawLine(cyanPen, s1, s2);
                    g.FillEllipse(cyanBrush, s1.X - 3.5f, s1.Y - 3.5f, 7f, 7f); g.FillEllipse(cyanBrush, s2.X - 3.5f, s2.Y - 3.5f, 7f, 7f);
                    if (rp || !isCmp)
                    {
                        string wStr = hp.Unit == "mm" ? $"{hp.WVal:F3} mm ({hp.WPx:F2} px)" : $"{hp.WPx:F2} px";
                        string tip = $"#{hp.Cid} 局部宽: {wStr} | {hp.Orient}";
                        float tw = g.MeasureString(tip, _canvasFont).Width + 14f, tx = Math.Min(sc.X + 14f, _canvas.Width - tw - 6f), ty = Math.Max(sc.Y - 28f, 8f);
                        g.FillRectangle(tipBg, tx, ty, tw, 24f); g.DrawRectangle(cyanPen, tx, ty, tw, 24f);
                        g.DrawString(tip, _canvasFont, cyanBrush, tx + 6f, ty + 3f);
                    }
                }
            }

            if (_activeTool is "calib" or "ruler")
            {
                using var banBrush = new SolidBrush(_activeTool == "calib" ? Color.FromArgb(2, 132, 199) : Color.FromArgb(13, 148, 136));
                string banTxt = _activeTool == "calib" ? "比例尺标定：拖动或点击参照物两端输入真实毫米 (ESC取消)" : "自由游标卡尺：拖动或点击任意两点测量距离，不改比例尺 (ESC退出)";
                g.FillRectangle(banBrush, 12, 10, 450, 26); g.DrawRectangle(Pens.White, 12, 10, 450, 26); g.DrawString(banTxt, _canvasFont, Brushes.White, 18, 14);
                if (_linePt1 is { } lp1 && _linePt2 is { } lp2)
                {
                    PointF s1 = ImgToScreen(lp1.X, lp1.Y, isCmp, sw), s2 = ImgToScreen(lp2.X, lp2.Y, isCmp, sw);
                    double pxD = Math.Sqrt(Math.Pow(lp2.X - lp1.X, 2) + Math.Pow(lp2.Y - lp1.Y, 2)), mmK = ReadParams().MmPerPx;
                    string lbl = mmK > 0 ? $"{pxD * mmK:F3} mm ({pxD:F1} px)" : $"{pxD:F2} px";
                    using var blkPen = new GdiPen(Color.Black, 4f); using var cyanPen = new GdiPen(Color.FromArgb(0, 245, 212), 2f);
                    using var cyanBrush = new SolidBrush(Color.FromArgb(0, 245, 212)); using var darkFill = new SolidBrush(Color.FromArgb(230, 15, 23, 42));
                    g.DrawLine(blkPen, s1, s2); g.DrawLine(cyanPen, s1, s2);
                    g.FillEllipse(darkFill, s1.X - 5f, s1.Y - 5f, 10f, 10f); g.DrawEllipse(cyanPen, s1.X - 5f, s1.Y - 5f, 10f, 10f);
                    g.FillEllipse(darkFill, s2.X - 5f, s2.Y - 5f, 10f, 10f); g.DrawEllipse(cyanPen, s2.X - 5f, s2.Y - 5f, 10f, 10f);
                    float bw = g.MeasureString(lbl, _canvasFont).Width + 14f, mx = (s1.X + s2.X) * 0.5f, my = (s1.Y + s2.Y) * 0.5f - 18f;
                    g.FillRectangle(darkFill, mx - bw * 0.5f, my - 11f, bw, 22f); g.DrawRectangle(cyanPen, mx - bw * 0.5f, my - 11f, bw, 22f);
                    g.DrawString(lbl, _canvasFont, cyanBrush, mx - bw * 0.5f + 6f, my - 8f);
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

        private void DrawEmptyHint(Graphics g)
        {
            string msg = "请点击上方【+ 导入图片】或【+ 导入文件夹】加载待测表面图像\n\n"
                       + "• 【漫游/探针】：鼠标移至裂缝任意位置，实时贴边绘制正交卡尺并显示局部亚像素缝宽\n"
                       + "• 【补画 / 擦除】：直接涂抹补连断缝或擦除污渍伪影，松手即刻重算长度与缝宽 (Ctrl+Z 撤销)\n"
                       + "• 【导出CAD(.dxf) / 导出报告】：支持 AutoCAD 矢量裂缝图、Excel 与 Word/PDF 报告";
            using var f = new GdiFont("Microsoft YaHei UI", 10f);
            using var br = new SolidBrush(Color.FromArgb(148, 163, 184));
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(msg, f, br, new RectangleF(0, 0, _canvas.Width, _canvas.Height), sf);
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            foreach (var b in new[] { _btnOpenFiles, _btnOpenFolder, _btnDetectCur, _btnDetectBatch, _btnSaveCur, _btnSaveDxf, _btnReport, _btnExportBatch }) b.Enabled = !busy;
            _btnStop.Enabled = busy;
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
                    BeginInvoke(new Action(() => { UpdateQueueRow(iid, "检测中", "-"); _lblStatus.Text = $"[{step}/{total}] 正在推理与亚像素法向测量: {item.FileName} ..."; }));

                    Mat? img = ImgCodec.ReadBgr(item.FilePath);
                    if (img == null) { lastErr = $"无法读取图像 {item.FileName}"; BeginInvoke(new Action(() => UpdateQueueRow(iid, "读取失败", "-"))); continue; }

                    try
                    {
                        var (probU8, modeTag) = _segmenter.InferProbU8(img, p.TileMode);
                        var outRes = PostProcessor.ProcessFromProb(img, probU8, modeTag, p.ProbThresh, p.MmPerPx, p.MinArea, p.BridgePx, showWmax);
                        item.Status = "已完成"; item.Summary = outRes.Summary; item.Cracks = outRes.Cracks; item.BranchPts = outRes.BranchPts;
                        _undoStacks.Remove(iid);
                        PutCache(iid, orig: img, probU8: probU8, modeTag: modeTag, overlay: outRes.OverlayImg, mask: outRes.MaskImg);
                        okCnt++;

                        int prog = (int)Math.Round((step / (double)total) * 1000), cnt = outRes.Summary.CrackCount;
                        BeginInvoke(new Action(() =>
                        {
                            UpdateQueueRow(iid, "已完成", cnt.ToString());
                            _progressBar.Value = Math.Clamp(prog, 0, 1000);
                            if (iid == _currentId)
                            {
                                if (_viewMode == "orig") { _viewMode = "result"; SyncViewButtons(); }
                                _selectedCrackId = null; UpdateRightPanel(item); _canvas.Invalidate();
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        img.Dispose();
                        lastErr = ex.Message;
                        string err = ex.Message;
                        BeginInvoke(new Action(() => { UpdateQueueRow(iid, "出错", "-"); _lblStatus.Text = $"推理出错: {err}"; }));
                    }
                }

                double sec = sw.Elapsed.TotalSeconds; bool canceled = _cancelFlag;
                BeginInvoke(new Action(() =>
                {
                    SetBusy(false);
                    if (canceled) { _lblStatus.Text = $"任务已停止：完成 {okCnt}/{total} 张 ({sec:F1}s)"; return; }
                    _progressBar.Value = 1000;
                    _lblStatus.Text = (lastErr != null && okCnt == 0) ? $"检测失败：{lastErr}" : $"处理完成！共完成 {okCnt}/{total} 张图像，总耗时 {sec:F2}s。";
                    onFinishedCb?.Invoke();
                }));
            });
        }

        private (Mat Orig, Mat ResBgr, Mat? Mask) BuildExportTripleCloned(string iid, string styleCode, int minArea)
        {
            var it = _items[iid]; var cache = GetCacheRef(iid);
            Mat? orig = cache?.OrigImg != null ? cache.OrigImg.Clone() : ImgCodec.ReadBgr(it.FilePath);
            if (orig == null) throw new IOException($"无法读取源图像: {it.FilePath}");
            if (cache?.OverlayImg != null) return (orig, PostProcessor.RenderHighDpiExport(cache.OverlayImg, it.Cracks, styleCode), cache.MaskImg?.Clone());

            var s = it.Summary!;
            using var infer = _segmenter.InferProbU8(orig, s.Mode.Contains("切片")).ProbU8;
            var outRes = PostProcessor.ProcessFromProb(orig, infer, s.Mode, s.ProbThresh, s.MmPerPx, minArea, s.BridgePx, _chkShowWmax.Checked);
            try { return (orig, PostProcessor.RenderHighDpiExport(outRes.OverlayImg, it.Cracks, styleCode), outRes.MaskImg); }
            finally { outRes.OverlayImg.Dispose(); }
        }

        private void SaveCurrentImage()
        {
            if (_isBusy || _currentId == null || _items[_currentId].Summary == null) { MessageBox.Show(this, "请先对当前图片执行检测！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var it = _items[_currentId];
            using var sfd = new SaveFileDialog { Title = "保存高清标注图", FileName = $"{Path.GetFileNameWithoutExtension(it.FileName)}_result.jpg", Filter = "JPEG 图像|*.jpg|PNG 无损图像|*.png" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            var p = ReadParams();
            var (orig, res, msk) = BuildExportTripleCloned(_currentId, p.LabelStyle, p.MinArea);
            using (orig) using (res) using (msk) ImgCodec.WriteImg(sfd.FileName, res);
            MessageBox.Show(this, $"高清标注图已保存至：\n{sfd.FileName}", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveCurrentDxf()
        {
            if (_isBusy || _currentId == null || _items[_currentId].Summary == null) { MessageBox.Show(this, "请先对当前图片执行检测！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var it = _items[_currentId];
            using var sfd = new SaveFileDialog { Title = "导出 AutoCAD 裂缝矢量图 (.dxf)", FileName = $"{Path.GetFileNameWithoutExtension(it.FileName)}_cracks.dxf", Filter = "AutoCAD DXF 矢量图|*.dxf" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            var p = ReadParams();
            var (orig, res, msk) = BuildExportTripleCloned(_currentId, p.LabelStyle, p.MinArea);
            using (orig) using (res) using (msk) ReportExporter.ExportDxf(sfd.FileName, it.Summary!, it.Cracks, msk);
            MessageBox.Show(this, $"CAD 矢量图已导出至：\n{sfd.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportReportDialog()
        {
            if (_isBusy) return;
            var doneIds = _itemOrder.Where(id => _items[id].Summary != null).ToList();
            if (doneIds.Count == 0) { MessageBox.Show(this, "暂无已检测完成的图像！请先执行检测后再导出报告。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            using var dlg = new Form { Text = "生成裂缝检测报告", Size = new GdiSize(445, 315), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, BackColor = Color.White, Font = new GdiFont("Microsoft YaHei UI", 9f) };
            TextBox AddRow(int idx, string label, string defVal)
            {
                int ry = 18 + idx * 36;
                dlg.Controls.Add(new Label { Text = label, AutoSize = true, Location = new GdiPoint(18, ry + 3) });
                var tb = new TextBox { Text = defVal, Width = 260, Location = new GdiPoint(145, ry) };
                dlg.Controls.Add(tb); return tb;
            }
            string defLim = _items[doneIds[0]].Summary!.Unit == "mm" ? "0.30" : "15.0";
            var tbProj = AddRow(0, "工程 / 项目名称:", "结构表面裂缝专项检测");
            var tbLoc = AddRow(1, "检测部位 / 构件:", "待测结构表面");
            var tbInsp = AddRow(2, "检测 / 复核人员:", "检测工程师");
            var tbWarn = AddRow(3, "缝宽超限预警值:", defLim);

            var rbAll = new RadioButton { Text = $"全部已检图像 ({doneIds.Count}张)", Checked = true, AutoSize = true, Location = new GdiPoint(100, 168) };
            var rbCur = new RadioButton { Text = "仅当前图像", Enabled = _currentId != null && doneIds.Contains(_currentId), AutoSize = true, Location = new GdiPoint(270, 168) };
            dlg.Controls.Add(new Label { Text = "报告范围:", AutoSize = true, Location = new GdiPoint(18, 170) });
            dlg.Controls.AddRange(new Control[] { rbAll, rbCur });

            var btnGen = MakeFlatBtn("选择路径并生成报告", Color.FromArgb(124, 58, 237), Color.White, 12, 32); btnGen.Location = new GdiPoint(235, 215);
            var btnCan = MakeFlatBtn("取消", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 12, 32); btnCan.Location = new GdiPoint(160, 215);
            btnCan.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel; btnGen.Click += (_, _) => dlg.DialogResult = DialogResult.OK;
            dlg.Controls.AddRange(new Control[] { btnGen, btnCan });
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            if (!double.TryParse(tbWarn.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double warnLim)) warnLim = 0.3;
            string proj = tbProj.Text.Trim(), loc = tbLoc.Text.Trim(), insp = tbInsp.Text.Trim();
            var targetIds = rbCur.Checked && _currentId != null ? new List<string> { _currentId } : doneIds;
            using var sfd = new SaveFileDialog { Title = "导出图文检测报告", FileName = $"裂缝检测报告_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML 打印级报告 (可一键转存 PDF)|*.html|Word 可编辑图文报告 (.doc)|*.doc" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            string savePath = sfd.FileName; var p = ReadParams(); SetBusy(true); _progressBar.Value = 0;
            Task.Run(() =>
            {
                try
                {
                    var bundle = new List<(QueueItem, string, string)>(targetIds.Count);
                    for (int i = 0; i < targetIds.Count; i++)
                    {
                        string iid = targetIds[i]; var it = _items[iid]; int step = i + 1, tot = targetIds.Count;
                        BeginInvoke(new Action(() => { _lblStatus.Text = $"[{step}/{tot}] 正在排版报告配图: {it.FileName} ..."; _progressBar.Value = (int)Math.Round((step / (double)(tot + 1)) * 1000); }));
                        var (orig, res, msk) = BuildExportTripleCloned(iid, p.LabelStyle, p.MinArea);
                        using (orig) using (res) using (msk) bundle.Add((it, ImgCodec.ToJpegBase64(orig), ImgCodec.ToJpegBase64(res)));
                    }
                    ReportExporter.ExportInspectionReport(savePath, proj, loc, insp, warnLim, bundle);
                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false); _progressBar.Value = 1000; _lblStatus.Text = $"检测报告已生成: {savePath}";
                        if (MessageBox.Show(this, $"检测报告已导出至：\n{savePath}\n\n是否立即打开查看？", "报告生成成功", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo(savePath) { UseShellExecute = true });
                    }));
                }
                catch (Exception ex) { string err = ex.Message; BeginInvoke(new Action(() => { SetBusy(false); MessageBox.Show(this, err, "报告生成失败", MessageBoxButtons.OK, MessageBoxIcon.Error); })); }
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
            bool saveMask = _chkExportMask.Checked, saveDxf = _chkExportDxf.Checked;
            var p = ReadParams(); SetBusy(true); _progressBar.Value = 0;

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
                        string iid = doneIds[i]; var it = _items[iid]; records.Add(it);
                        int step = i + 1, tot = doneIds.Count;
                        BeginInvoke(new Action(() => { _lblStatus.Text = $"[{step}/{tot}] 正在导出图像/矢量/报表: {it.FileName} ..."; _progressBar.Value = (int)Math.Round((step / (double)(tot + 1)) * 1000); }));

                        var (orig, res, msk) = BuildExportTripleCloned(iid, p.LabelStyle, p.MinArea);
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

                    string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    ReportExporter.ExportExcel(Path.Combine(outDir, $"裂缝测量数据表_{ts}.xlsx"), records);
                    double defLim = records[0].Summary!.Unit == "mm" ? 0.30 : 15.0;
                    ReportExporter.ExportInspectionReport(Path.Combine(outDir, $"裂缝图文检测报告_{ts}.html"), "批量裂缝检测项目", "详见各图像样本", "系统自动汇总", defLim, repBundle);

                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false); _progressBar.Value = 1000; _lblStatus.Text = $"批量导出全部完成：{outDir}";
                        string extra = saveDxf ? "\n• AutoCAD (.dxf) 矢量图" : "";
                        if (MessageBox.Show(this, $"已成功导出至：\n{outDir}\n\n• 标注结果图 ({records.Count}张)\n• Excel 汇总与明细表\n• HTML 图文检测报告{extra}\n\n是否立即打开文件夹？", "批量导出完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo("explorer.exe", outDir) { UseShellExecute = true });
                    }));
                }
                catch (Exception ex) { string err = ex.Message; BeginInvoke(new Action(() => { SetBusy(false); MessageBox.Show(this, err, "批量导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); })); }
            });
        }
    }
}