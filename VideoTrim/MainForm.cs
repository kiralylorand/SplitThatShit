using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VideoTrim;

public sealed class MainForm : Form
{
    private readonly MenuStrip _menu = new();

    private readonly TextBox _inputBox = new();
    private readonly TextBox _outputBox = new();
    private readonly TextBox _processedBox = new();
    private readonly TextBox _minBox = new();
    private readonly TextBox _maxBox = new();
    private readonly TextBox _segmentsPerOutputBox = new();
    private readonly TextBox _outputsPerInputBox = new();
    private readonly CheckBox _autoDetectBox = new();
    private readonly TrackBar _similaritySlider = new();
    private readonly Label _similarityValueLabel = new();
    private readonly Label _estimatedDurationLabel = new();
    private readonly CheckBox _pauseBox = new();
    private readonly RadioButton _fastSplitRadio = new();
    private readonly RadioButton _crossfadeRadio = new();
    private readonly Button _startButton = new();
    private readonly Button _pauseButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _continueButton = new();
    private readonly Button _openSegmentsButton = new();
    private readonly Label _licenseStatusLabel = new();
    private readonly Label _progressStatusLabel = new();
    private readonly TextBox _logBox = new();
    private readonly ToolTip _toolTip = new();

    private readonly ComboBox _modeComboBox = new();

    private CancellationTokenSource? _cts;
    private ManualResetEventSlim? _continueEvent;
    private string? _lastSegmentsFolder;
    private bool _isWaitingForContinue;
    private bool _isPaused;
    private AppSettings _settings = new();

    // GitHub update checker configuration
    // Repository is public but contains only releases (no source code)
    private const string GitHubOwner = "kiralylorand";
    private const string GitHubRepo = "SplitThatShit";

    public MainForm()
    {
        Text = "Split That Sh!t";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 750); // Increased to ensure progress bar is visible
        // Use default MaximumSize (no upper bound) so the form can grow if needed on high DPI
        MaximumSize = new Size(0, 0);
        Size = new Size(900, 750);

        // Menu
        var licenseMenu = new ToolStripMenuItem("License");
        licenseMenu.Click += (_, _) => ActivateLicense();

        var helpMenu = new ToolStripMenuItem("Help");
        var aboutItem = new ToolStripMenuItem("About");
        aboutItem.Click += (_, _) => ShowAbout();
        helpMenu.DropDownItems.Add(aboutItem);
        _menu.Items.Add(licenseMenu);
        _menu.Items.Add(helpMenu);
        _menu.Dock = DockStyle.Fill;

        // Top bar: menu (left) + trial status (right) on the same line
        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            RowCount = 1,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));            // Menu (left)
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));        // License text (right)

        _licenseStatusLabel.AutoSize = false;
        _licenseStatusLabel.Dock = DockStyle.Fill;
        _licenseStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _licenseStatusLabel.Padding = new Padding(10, 5, 10, 5);
        _licenseStatusLabel.ForeColor = Color.DarkRed;

        topBar.Controls.Add(_menu, 0, 0);
        topBar.Controls.Add(_licenseStatusLabel, 1, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            RowCount = 11,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10)
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        AddRow(grid, 0, "Input folder:", _inputBox, BrowseInput);
        AddRow(grid, 1, "Output folder:", _outputBox, BrowseOutput);
        AddRow(grid, 2, "Processed folder:", _processedBox, BrowseProcessed);

        // Min / Max seconds on the same row
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var segmentLengthLabel = new Label 
        { 
            Text = "Segment length (seconds):", 
            AutoSize = false,
            Height = 23,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = new Padding(0, 7, 0, 7)
        };
        grid.Controls.Add(segmentLengthLabel, 0, 3);

        var lengthPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 0),
            Margin = new Padding(0, 7, 0, 7)
        };
        var minLabel = new Label 
        { 
            Text = "Min:", 
            AutoSize = false,
            Height = 23,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 7, 0, 7)
        };
        lengthPanel.Controls.Add(minLabel);
        _minBox.Width = 45;
        _minBox.Height = 23; // Standard textbox height
        _minBox.Margin = new Padding(4, 7, 0, 7);
        lengthPanel.Controls.Add(_minBox);
        var maxLabel = new Label 
        { 
            Text = "Max:", 
            AutoSize = false,
            Height = 23,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(10, 7, 0, 7)
        };
        lengthPanel.Controls.Add(maxLabel);
        _maxBox.Width = 45;
        _maxBox.Height = 23; // Standard textbox height
        _maxBox.Margin = new Padding(4, 7, 0, 7);
        lengthPanel.Controls.Add(_maxBox);

        _minBox.TextChanged += (_, _) => UpdateEstimatedDuration();
        _maxBox.TextChanged += (_, _) => UpdateEstimatedDuration();

        grid.Controls.Add(lengthPanel, 1, 3);

        // Mode dropdown
        _modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeComboBox.Items.AddRange(new object[]
        {
            "Split only (segments only)",
            "Split + Review (auto/manual delete)",
            "Direct Mix (cut only needed segments, faster)"
        });
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.Width = 400;
        _modeComboBox.Height = 23;
        _modeComboBox.SelectedIndexChanged += (_, _) =>
        {
            var mode = _modeComboBox.SelectedIndex switch
            {
                0 => ProcessingMode.SplitOnly,
                1 => ProcessingMode.SplitReview,
                2 => ProcessingMode.DirectMix,
                _ => ProcessingMode.SplitOnly
            };
            ApplyModeDefaults(mode);
            UpdateMode();
        };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var modeLabel = new Label 
        { 
            Text = "Mode:", 
            AutoSize = false,
            Height = 23,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = new Padding(0, 7, 0, 7)
        };
        grid.Controls.Add(modeLabel, 0, 4);
        _modeComboBox.Margin = new Padding(0, 7, 0, 7);
        grid.Controls.Add(_modeComboBox, 1, 4);

        // Generate videos using segments - combined on one line
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var generateLabel = new Label 
        { 
            Text = "Generate:", 
            AutoSize = false,
            Height = 23,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = new Padding(0, 7, 0, 7)
        };
        grid.Controls.Add(generateLabel, 0, 5);

        var generatePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 7, 0, 7)
        };
        
        // "Generate [INPUT] videos using [INPUT] segments/video."
        generatePanel.Controls.Add(new Label { Text = "Generate", AutoSize = true, Height = 23, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 7, 0, 7) });
        
        _outputsPerInputBox.Width = 60;
        _outputsPerInputBox.Height = 23;
        _outputsPerInputBox.Margin = new Padding(0, 7, 0, 7); // Ensure bottom border is visible
        generatePanel.Controls.Add(_outputsPerInputBox);
        
        generatePanel.Controls.Add(new Label { Text = "videos using", AutoSize = true, Height = 23, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(4, 7, 0, 7) });
        
        _segmentsPerOutputBox.Width = 60;
        _segmentsPerOutputBox.Height = 23;
        _segmentsPerOutputBox.Margin = new Padding(0, 7, 0, 7); // Ensure bottom border is visible
        generatePanel.Controls.Add(_segmentsPerOutputBox);
        
        generatePanel.Controls.Add(new Label { Text = "segments/video.", AutoSize = true, Height = 23, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(4, 7, 0, 7) });

        _estimatedDurationLabel.AutoSize = true;
        _estimatedDurationLabel.ForeColor = Color.Gray;
        _estimatedDurationLabel.Margin = new Padding(8, 7, 0, 7);
        generatePanel.Controls.Add(_estimatedDurationLabel);

        _segmentsPerOutputBox.TextChanged += (_, _) => UpdateEstimatedDuration();
        _outputsPerInputBox.TextChanged += (_, _) => UpdateEstimatedDuration();

        grid.Controls.Add(generatePanel, 1, 5);

        // Auto-detect and Pause for manual delete on the same row
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var autoDetectPausePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 7, 0, 7)
        };
        _autoDetectBox.Text = "Auto-detect similar segments";
        _autoDetectBox.AutoSize = true;
        _autoDetectBox.Margin = new Padding(0, 7, 0, 7);
        _autoDetectBox.CheckedChanged += (_, _) => UpdateMode();
        autoDetectPausePanel.Controls.Add(_autoDetectBox);
        
        _pauseBox.Text = "Pause for manual delete";
        _pauseBox.AutoSize = true;
        _pauseBox.Margin = new Padding(15, 7, 0, 7); // Space between checkboxes
        autoDetectPausePanel.Controls.Add(_pauseBox);
        
        grid.Controls.Add(autoDetectPausePanel, 1, 6);

        _similaritySlider.Minimum = 90;
        _similaritySlider.Maximum = 99;
        _similaritySlider.TickFrequency = 1;
        _similaritySlider.ValueChanged += (_, _) => UpdateSimilarityLabel();
        _similaritySlider.Margin = new Padding(0, 8, 5, 8);
        _similarityValueLabel.AutoSize = true;
        _similarityValueLabel.Margin = new Padding(0, 8, 0, 8);
        var simPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        simPanel.Controls.Add(_similaritySlider);
        simPanel.Controls.Add(_similarityValueLabel);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var similarityLabel = new Label 
        { 
            Text = "Similarity threshold:", 
            AutoSize = false,
            Height = 23,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = new Padding(0, 12, 0, 12)
        };
        grid.Controls.Add(similarityLabel, 0, 7);
        grid.Controls.Add(simPanel, 1, 7);

        _fastSplitRadio.Text = "Fast split (faster, less precise)";
        _fastSplitRadio.AutoSize = true;
        _fastSplitRadio.CheckedChanged += (_, _) => UpdateMode();

        _crossfadeRadio.Text = "Crossfade transitions (slower)";
        _crossfadeRadio.AutoSize = true;
        _crossfadeRadio.CheckedChanged += (_, _) => UpdateMode();

        var splitModePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 7, 0, 7)
        };
        _fastSplitRadio.Margin = new Padding(0, 7, 0, 7);
        _crossfadeRadio.Margin = new Padding(15, 7, 0, 7);
        splitModePanel.Controls.Add(_fastSplitRadio);
        splitModePanel.Controls.Add(_crossfadeRadio);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var splitModeLabel = new Label 
        { 
            Text = "Split mode:", 
            AutoSize = false,
            Height = 23,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = new Padding(0, 7, 0, 7)
        };
        grid.Controls.Add(splitModeLabel, 0, 8);
        grid.Controls.Add(splitModePanel, 1, 8);

        _startButton.Text = "Start";
        _startButton.AutoSize = true;
        _startButton.Click += StartClicked;

        _pauseButton.Text = "Pause";
        _pauseButton.AutoSize = true;
        _pauseButton.Enabled = false;
        _pauseButton.Click += (_, _) =>
        {
            if (_cts == null)
            {
                return;
            }
            _isPaused = true;
            VideoProcessor.GlobalPauseEvent.Reset();
            AppendLog("Paused. Click Resume to continue.");
            _pauseButton.Enabled = false;
            _continueButton.Enabled = true;
        };

        _continueButton.Text = "Resume";
        _continueButton.AutoSize = true;
        _continueButton.Enabled = false;
        _continueButton.Click += (_, _) =>
        {
            _isPaused = false;
            VideoProcessor.GlobalPauseEvent.Set();
            _continueEvent?.Set();
            _pauseButton.Enabled = _cts != null;
            _continueButton.Enabled = _isWaitingForContinue;
        };

        _stopButton.Text = "Stop";
        _stopButton.AutoSize = true;
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) =>
        {
            _cts?.Cancel();
            _continueEvent?.Set();
        };

        _openSegmentsButton.Text = "Open segments folder";
        _openSegmentsButton.AutoSize = true;
        _openSegmentsButton.Enabled = false;
        _openSegmentsButton.Click += (_, _) => OpenSegmentsFolderClicked();

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 7, 0, 7)
        };
        _startButton.Margin = new Padding(0, 7, 5, 7);
        _pauseButton.Margin = new Padding(0, 7, 5, 7);
        _continueButton.Margin = new Padding(0, 7, 5, 7);
        _openSegmentsButton.Margin = new Padding(0, 7, 5, 7);
        _stopButton.Margin = new Padding(0, 7, 0, 7);
        buttonPanel.Controls.Add(_startButton);
        buttonPanel.Controls.Add(_pauseButton);
        buttonPanel.Controls.Add(_continueButton);
        buttonPanel.Controls.Add(_openSegmentsButton);
        buttonPanel.Controls.Add(_stopButton);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        grid.Controls.Add(buttonPanel, 1, 9);

        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Dock = DockStyle.Fill;
        _logBox.BackColor = SystemColors.Window;
        _logBox.MinimumSize = new Size(0, 200);

        _progressStatusLabel.AutoSize = false;
        _progressStatusLabel.Height = 40;
        _progressStatusLabel.Dock = DockStyle.Fill;
        _progressStatusLabel.BackColor = Color.LightGray;
        _progressStatusLabel.ForeColor = Color.Black;
        _progressStatusLabel.Padding = new Padding(5);
        _progressStatusLabel.Text = "Ready to start processing.";
        _progressStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _progressStatusLabel.Font = new Font(_progressStatusLabel.Font.FontFamily, 9, FontStyle.Bold);

        // Use SplitContainer to ensure progress bar is ALWAYS visible
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal, // Horizontal = top/bottom split
            SplitterWidth = 5,
            FixedPanel = FixedPanel.Panel2, // Fix bottom panel
            IsSplitterFixed = false
        };
        
        // Top panel: settings grid (no scrollbars)
        grid.Dock = DockStyle.Fill;
        splitContainer.Panel1.Controls.Add(grid);
        splitContainer.Panel1MinSize = 320;
        
        // Bottom panel: log box and progress bar - ALWAYS VISIBLE
        var bottomPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // Fixed height for progress bar
        bottomPanel.Controls.Add(_logBox, 0, 0);
        bottomPanel.Controls.Add(_progressStatusLabel, 0, 1);
        
        _logBox.Dock = DockStyle.Fill;
        _progressStatusLabel.Dock = DockStyle.Fill;
        
        splitContainer.Panel2.Controls.Add(bottomPanel);
        // Don't set Panel2MinSize here - it causes initialization errors
        // We'll set it in Load event after form is fully initialized
        
        var layout = splitContainer;

        // Add controls: top bar (menu + trial status) then main layout
        Controls.Add(layout);
        Controls.Add(topBar);
        MainMenuStrip = _menu;

        // Set Panel2MinSize and SplitterDistance after form is fully loaded
        Load += (_, _) =>
        {
            try
            {
                splitContainer.Panel2MinSize = 200; // Minimum height: 160 log + 40 progress
                // Set splitter distance to leave ~250px for bottom panel (log + progress)
                var availableHeight = ClientSize.Height - _menu.Height;
                splitContainer.SplitterDistance = Math.Max(300, availableHeight - 250);
            }
            catch (Exception ex)
            {
                // If calculation fails, use safe defaults
                splitContainer.Panel2MinSize = 200;
                splitContainer.SplitterDistance = 450;
                AppendLog($"Warning: Could not calculate splitter position: {ex.Message}");
            }

            // Check for updates in background (non-blocking)
            _ = Task.Run(async () =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[MainForm] Checking for updates...");
                    var updateInfo = await UpdateChecker.CheckForUpdatesAsync(GitHubOwner, GitHubRepo);
                    if (updateInfo != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainForm] Update found, showing notification");
                        Invoke(new Action(() => ShowUpdateNotification(updateInfo)));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainForm] No update found");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainForm] Error in update check: {ex.Message}");
                }
            });
        };

        _settings = AppSettings.Load();
        _inputBox.Text = _settings.InputFolder;
        _outputBox.Text = _settings.OutputFolder;
        _processedBox.Text = _settings.ProcessedFolder;
        _minBox.Text = _settings.MinSeconds.ToString();
        _maxBox.Text = _settings.MaxSeconds.ToString();
        _segmentsPerOutputBox.Text = _settings.SegmentsPerVideo.ToString();
        _outputsPerInputBox.Text = _settings.VideosPerInput.ToString();
        _autoDetectBox.Checked = _settings.AutoDetectSimilar;
        _pauseBox.Checked = _settings.PauseForManualDelete;
        _fastSplitRadio.Checked = _settings.FastSplit;
        _crossfadeRadio.Checked = _settings.Crossfade;
        _similaritySlider.Value = Math.Max(90, Math.Min(99, (int)Math.Round(_settings.Similarity * 100)));
        SetMode(_settings.Mode);
        UpdateSimilarityLabel();
        UpdateMode();
        UpdateEstimatedDuration();
        UpdateLicenseStatusLabel();
        InitTooltips();
        FormClosing += (_, _) => SaveSettings();
    }

    private ProcessingMode SelectedMode =>
        _modeComboBox.SelectedIndex switch
        {
            0 => ProcessingMode.SplitOnly,
            1 => ProcessingMode.SplitReview,
            2 => ProcessingMode.DirectMix,
            _ => ProcessingMode.SplitOnly
        };

    private void SetMode(ProcessingMode mode)
    {
        _modeComboBox.SelectedIndex = mode switch
        {
            ProcessingMode.SplitOnly => 0,
            ProcessingMode.SplitReview => 1,
            ProcessingMode.DirectMix => 2,
            _ => 0
        };
    }

    private void AddRow(TableLayoutPanel grid, int row, string label, TextBox box, Action browseAction)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var lbl = new Label 
        { 
            Text = label, 
            AutoSize = false,
            Height = 23,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = new Padding(0, 7, 0, 7)
        };
        grid.Controls.Add(lbl, 0, row);
        box.Dock = DockStyle.Fill;
        box.Height = 23;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        box.Margin = new Padding(0, 7, 0, 7);
        grid.Controls.Add(box, 1, row);
        var btn = new Button { Text = "Browse", AutoSize = true, Margin = new Padding(0, 7, 0, 7) };
        btn.Click += (_, _) => browseAction();
        grid.Controls.Add(btn, 2, row);
    }

    private void AddSimpleRow(TableLayoutPanel grid, int row, string label, TextBox box)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Let row height follow controls
        var lbl = new Label 
        { 
            Text = label, 
            AutoSize = false,
            Height = 23,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = new Padding(0, 3, 0, 3)
        };
        grid.Controls.Add(lbl, 0, row);
        box.Width = 100;
        box.Height = 23;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
        box.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(box, 1, row);
    }

    private void BrowseInput()
    {
        var path = PickFolder(_inputBox.Text);
        if (!string.IsNullOrWhiteSpace(path))
        {
            _inputBox.Text = path;
        }
    }

    private void BrowseOutput()
    {
        var path = PickFolder(_outputBox.Text);
        if (!string.IsNullOrWhiteSpace(path))
        {
            _outputBox.Text = path;
        }
    }

    private void BrowseProcessed()
    {
        var path = PickFolder(_processedBox.Text);
        if (!string.IsNullOrWhiteSpace(path))
        {
            _processedBox.Text = path;
        }
    }

    private static string PickFolder(string initial)
    {
        using var dialog = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(initial))
        {
            dialog.SelectedPath = initial;
        }

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : string.Empty;
    }

    private async void StartClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(_minBox.Text.Trim(), out var minSec) ||
            !int.TryParse(_maxBox.Text.Trim(), out var maxSec))
        {
            MessageBox.Show("Min/Max seconds must be numbers.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (minSec < 1 || maxSec < 1 || minSec > maxSec)
        {
            MessageBox.Show("Time range is invalid.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (SelectedMode != ProcessingMode.SplitOnly)
        {
            if (!int.TryParse(_segmentsPerOutputBox.Text.Trim(), out var segmentsPerOutput) ||
                !int.TryParse(_outputsPerInputBox.Text.Trim(), out var outputsPerInput))
            {
                MessageBox.Show("Mix values must be numbers.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (segmentsPerOutput < 1 || outputsPerInput < 1)
            {
                MessageBox.Show("Mix values are invalid.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        SaveSettings();
        _isPaused = false;
        VideoProcessor.GlobalPauseEvent.Set();
        ToggleUi(false);
        AppendLog("=== Start ===");

        try
        {
            if (!ToolDownloader.ToolsExist(AppContext.BaseDirectory))
            {
                var result = MessageBox.Show(
                    "ffmpeg/ffprobe are missing. Download now? (~100-200MB)",
                    "Download ffmpeg",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                {
                    AppendLog("Stopped: ffmpeg/ffprobe missing.");
                    return;
                }

                AppendLog("Downloading ffmpeg...");
                await ToolDownloader.DownloadAsync(AppContext.BaseDirectory, AppendLog);
            }

            _cts = new CancellationTokenSource();
            _continueEvent = new ManualResetEventSlim(false);
            var similarity = _similaritySlider.Value / 100.0;

            await VideoProcessor.RunAsync(
                _inputBox.Text.Trim(),
                _outputBox.Text.Trim(),
                _processedBox.Text.Trim(),
                minSec,
                maxSec,
                SelectedMode,
                int.TryParse(_segmentsPerOutputBox.Text.Trim(), out var segs) ? segs : 0,
                int.TryParse(_outputsPerInputBox.Text.Trim(), out var outs) ? outs : 0,
                _autoDetectBox.Checked,
                similarity,
                _pauseBox.Checked,
                _fastSplitRadio.Checked,
                _crossfadeRadio.Checked,
                _cts.Token,
                ManualPauseAsync,
                CreateCanProcessDelegate(),
                CreateRegisterProcessedDelegate(),
                AppendLog,
                UpdateProgressStatus);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Stopped by user.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _continueEvent?.Dispose();
            _continueEvent = null;
            _isWaitingForContinue = false;
            AppendLog("=== Done ===");
            UpdateProgressStatus(0, 0, 0, 0, null);
            _progressStatusLabel.Text = "Ready to start processing.";
            ToggleUi(true);
        }
    }

    private void UpdateProgressStatus(int totalInputVideos, int processedInputVideos, int totalOutputFiles, int generatedOutputFiles, TimeSpan? estimatedRemaining)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<int, int, int, int, TimeSpan?>(UpdateProgressStatus), totalInputVideos, processedInputVideos, totalOutputFiles, generatedOutputFiles, estimatedRemaining);
            return;
        }

        if (totalInputVideos == 0)
        {
            _progressStatusLabel.Text = "Ready to start processing.";
            return;
        }

        var parts = new List<string>();
        parts.Add($"Input videos: {processedInputVideos}/{totalInputVideos}");
        
        if (totalOutputFiles > 0)
        {
            parts.Add($"Output files: {generatedOutputFiles}/{totalOutputFiles}");
        }
        else if (generatedOutputFiles > 0)
        {
            parts.Add($"Output files: {generatedOutputFiles} generated");
        }

        if (estimatedRemaining.HasValue)
        {
            var rem = estimatedRemaining.Value;
            var minutes = (int)Math.Floor(rem.TotalMinutes);
            var seconds = rem.Seconds;
            parts.Add($"Est. remaining: ~{minutes}m {seconds:D2}s");
        }

        _progressStatusLabel.Text = string.Join(" | ", parts);
    }

    private void UpdateEstimatedDuration()
    {
        if (SelectedMode == ProcessingMode.SplitOnly)
        {
            _estimatedDurationLabel.Text = "";
            return;
        }

        if (!int.TryParse(_minBox.Text.Trim(), out var minSec) ||
            !int.TryParse(_maxBox.Text.Trim(), out var maxSec) ||
            !int.TryParse(_segmentsPerOutputBox.Text.Trim(), out var segmentsPerVideo))
        {
            _estimatedDurationLabel.Text = "";
            return;
        }

        if (minSec < 1 || maxSec < 1 || minSec > maxSec || segmentsPerVideo < 1)
        {
            _estimatedDurationLabel.Text = "";
            return;
        }

        var minDuration = segmentsPerVideo * minSec;
        var maxDuration = segmentsPerVideo * maxSec;

        if (minDuration == maxDuration)
        {
            _estimatedDurationLabel.Text = $"{minDuration}s/video";
        }
        else
        {
            _estimatedDurationLabel.Text = $"{minDuration}-{maxDuration}s/video";
        }
    }

    private void ToggleUi(bool enabled)
    {
        _startButton.Enabled = enabled;
        _pauseButton.Enabled = !enabled && !_isPaused;
        _stopButton.Enabled = !enabled;
        _continueButton.Enabled = (!enabled && _isWaitingForContinue) || _isPaused;
        _openSegmentsButton.Enabled = SelectedMode == ProcessingMode.SplitReview && _pauseBox.Checked && !string.IsNullOrEmpty(_lastSegmentsFolder);
        _inputBox.Enabled = enabled;
        _outputBox.Enabled = enabled;
        _processedBox.Enabled = enabled;
        _minBox.Enabled = enabled;
        _maxBox.Enabled = enabled;
        _modeComboBox.Enabled = enabled;
        _segmentsPerOutputBox.Enabled = enabled && SelectedMode != ProcessingMode.SplitOnly;
        _outputsPerInputBox.Enabled = enabled && SelectedMode != ProcessingMode.SplitOnly;
        _autoDetectBox.Enabled = enabled && SelectedMode == ProcessingMode.SplitReview;
        _similaritySlider.Enabled = enabled && SelectedMode == ProcessingMode.SplitReview && _autoDetectBox.Checked;
        _pauseBox.Enabled = enabled && SelectedMode == ProcessingMode.SplitReview;
        _fastSplitRadio.Enabled = enabled;
        _crossfadeRadio.Enabled = enabled && SelectedMode != ProcessingMode.SplitOnly;
    }

    private void UpdateMode()
    {
        _segmentsPerOutputBox.Enabled = SelectedMode != ProcessingMode.SplitOnly;
        _outputsPerInputBox.Enabled = SelectedMode != ProcessingMode.SplitOnly;
        _autoDetectBox.Enabled = SelectedMode == ProcessingMode.SplitReview;
        _similaritySlider.Enabled = SelectedMode == ProcessingMode.SplitReview && _autoDetectBox.Checked;
        _pauseBox.Enabled = SelectedMode == ProcessingMode.SplitReview;
        _fastSplitRadio.Enabled = true;
        _crossfadeRadio.Enabled = SelectedMode != ProcessingMode.SplitOnly;
        _openSegmentsButton.Enabled = SelectedMode == ProcessingMode.SplitReview && _pauseBox.Checked && !string.IsNullOrEmpty(_lastSegmentsFolder);
        UpdateEstimatedDuration();
    }

    private void ApplyModeDefaults(ProcessingMode mode)
    {
        switch (mode)
        {
            case ProcessingMode.SplitOnly:
                _autoDetectBox.Checked = false;
                _pauseBox.Checked = false;
                _fastSplitRadio.Checked = true;
                _crossfadeRadio.Checked = false;
                break;
            case ProcessingMode.SplitReview:
                _autoDetectBox.Checked = true;
                _pauseBox.Checked = true;
                _fastSplitRadio.Checked = true;
                _crossfadeRadio.Checked = false;
                break;
            case ProcessingMode.DirectMix:
                _autoDetectBox.Checked = false;
                _pauseBox.Checked = false;
                _fastSplitRadio.Checked = true;
                _crossfadeRadio.Checked = false;
                break;
        }
    }

    private void UpdateSimilarityLabel()
    {
        _similarityValueLabel.Text = (_similaritySlider.Value / 100.0).ToString("0.00");
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(AppendLog), message);
            return;
        }

        _logBox.AppendText(message + Environment.NewLine);
    }

    private void SaveSettings()
    {
        _settings.InputFolder = _inputBox.Text.Trim();
        _settings.OutputFolder = _outputBox.Text.Trim();
        _settings.ProcessedFolder = _processedBox.Text.Trim();
        _settings.MinSeconds = int.TryParse(_minBox.Text.Trim(), out var min) ? min : _settings.MinSeconds;
        _settings.MaxSeconds = int.TryParse(_maxBox.Text.Trim(), out var max) ? max : _settings.MaxSeconds;
        _settings.SegmentsPerVideo = int.TryParse(_segmentsPerOutputBox.Text.Trim(), out var segs) ? segs : _settings.SegmentsPerVideo;
        _settings.VideosPerInput = int.TryParse(_outputsPerInputBox.Text.Trim(), out var outs) ? outs : _settings.VideosPerInput;
        _settings.AutoDetectSimilar = _autoDetectBox.Checked;
        _settings.PauseForManualDelete = _pauseBox.Checked;
        _settings.FastSplit = _fastSplitRadio.Checked;
        _settings.Similarity = _similaritySlider.Value / 100.0;
        _settings.Mode = SelectedMode;
        _settings.Crossfade = _crossfadeRadio.Checked;
        _settings.Save();
    }

    private Func<string, bool> CreateCanProcessDelegate()
    {
        var trial = new TrialManager(_settings, AppendLog);
        return path => trial.CanProcess(path);
    }

    private Action<string> CreateRegisterProcessedDelegate()
    {
        var trial = new TrialManager(_settings, AppendLog);
        return path => trial.RegisterProcessed(path);
    }

    private Task ManualPauseAsync(string folderPath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastSegmentsFolder = folderPath;
            _continueEvent?.Reset();
            Invoke(new Action(() =>
            {
                SystemSounds.Exclamation.Play();
                TryOpenFolder(folderPath);
                _isWaitingForContinue = true;
                _continueButton.Enabled = true;
                _openSegmentsButton.Enabled = true;
                AppendLog("Paused for manual delete. Click Resume to continue.");
            }));

            WaitHandle.WaitAny(new[] { _continueEvent!.WaitHandle, cancellationToken.WaitHandle });
            cancellationToken.ThrowIfCancellationRequested();

            Invoke(new Action(() =>
            {
                _isWaitingForContinue = false;
                _continueButton.Enabled = false;
            }));
        }, cancellationToken);
    }

    private void TryOpenFolder(string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    private void OpenSegmentsFolderClicked()
    {
        if (string.IsNullOrEmpty(_lastSegmentsFolder))
        {
            AppendLog("No temporary segments folder is available yet.");
            return;
        }

        TryOpenFolder(_lastSegmentsFolder);
    }

    private void InitTooltips()
    {
        _toolTip.SetToolTip(_autoDetectBox, "Remove near-duplicate segments automatically.");
        _toolTip.SetToolTip(_similaritySlider, "Higher = stricter (0.90–0.99).");
        _toolTip.SetToolTip(_pauseBox, "Pause after splitting so you can delete segments.");
        _toolTip.SetToolTip(_fastSplitRadio, "Faster processing, but cut points may be slightly off.");
        _toolTip.SetToolTip(_crossfadeRadio, "Smooth transitions between segments (slower).");
        _toolTip.SetToolTip(_pauseButton, "Temporarily pause processing.");
        _toolTip.SetToolTip(_continueButton, "Resume after pause/manual delete.");
        _toolTip.SetToolTip(_openSegmentsButton, "Open the temporary folder with segments (Split + Review + Pause).");
    }

    private void ShowAbout()
    {
        var text =
            "Split That Sh!t\n\n" +
            "A desktop tool for fast video segmenting and mixing.\n\n" +
            "Third‑party components:\n" +
            "- FFmpeg (https://ffmpeg.org) — licensed under GPL/LGPL.\n" +
            "- Other helper code and ideas inspired by open‑source projects under MIT/permissive licenses.\n\n" +
            "Use this tool at your own risk. Always respect the copyrights of the original videos.";

        MessageBox.Show(text, "About Split That Sh!t",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ActivateLicense()
    {
        using var form = new Form
        {
            Text = "Activate License",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 130),
            MinimizeBox = false,
            MaximizeBox = false
        };

        var label = new Label
        {
            Text = "Enter license key (VT-XXXX-XXXX-XXXX):",
            AutoSize = true,
            Left = 10,
            Top = 10
        };

        var box = new TextBox
        {
            Left = 10,
            Top = 35,
            Width = 390,
            Text = _settings.LicenseKey ?? string.Empty
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Left = 230,
            Top = 75,
            Width = 80
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Left = 320,
            Top = 75,
            Width = 80
        };

        form.Controls.Add(label);
        form.Controls.Add(box);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var key = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("Please enter a license key.", "License",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!LicenseManager.Validate(key))
        {
            MessageBox.Show("License key is not valid.", "License",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _settings.LicenseKey = key;
        _settings.IsLicensed = true;
        _settings.Save();
        UpdateLicenseStatusLabel();
        MessageBox.Show("License activated. Thank you for supporting Split That Sh!t.", "License",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UpdateLicenseStatusLabel()
    {
        var licensed = LicenseManager.IsLicensed(_settings);
        if (licensed)
        {
            // Hide trial/licensing hint once the app is activated.
            _licenseStatusLabel.Text = string.Empty;
            _licenseStatusLabel.Visible = false;
        }
        else
        {
            _licenseStatusLabel.Visible = true;
            var used = _settings.TrialUsedVideos;
            var max = 10;
            _licenseStatusLabel.Text = $"Trial mode – {used} of {max} videos used.";
            _licenseStatusLabel.ForeColor = Color.DarkRed;
        }
    }

    private void ShowUpdateNotification(UpdateChecker.UpdateInfo updateInfo)
    {
        var message = $"O versiune nouă ({updateInfo.TagName}) este disponibilă pentru Split That Sh!t!\n\n" +
                     $"Versiunea curentă: {UpdateChecker.GetCurrentVersion()}\n" +
                     $"Versiunea nouă: {updateInfo.Version}\n\n" +
                     $"Dorești să descarci update-ul acum?";

        var result = MessageBox.Show(
            message,
            "Update disponibil",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateInfo.ReleaseUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show(
                    $"Nu s-a putut deschide link-ul.\n\nTe rugăm să accesezi manual:\n{updateInfo.ReleaseUrl}",
                    "Eroare",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}
