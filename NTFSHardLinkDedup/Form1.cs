using NTFSHardLinkDedup.Src;
using System.Collections.Frozen;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Policy;

namespace NTFSHardLinkDedup
{
    public partial class Form1 : Form
    {
        private Panel? _loadingPanel;
        private Label? _loadingLabel;
        private ProgressBar? _loadingProgressBar;

        private TabPage? _currentLoadingTabPage;
        #region Loading
        private void ShowTabLoading(TabPage tabPage)
        {
            if (tabPage == null)
            {
                return;
            }

            _currentLoadingTabPage = tabPage;

            if (_loadingPanel == null || _loadingPanel.IsDisposed)
            {
                CreateLoadingPanel();
            }

            if (_loadingPanel!.Parent != tabPage)
            {
                tabPage.Controls.Add(_loadingPanel);
            }

            CenterLoadingPanel(tabPage);

            _loadingPanel.Visible = true;
            _loadingPanel.Enabled = true;
            _loadingPanel.BringToFront();

            _loadingProgressBar!.Style = ProgressBarStyle.Marquee;
            _loadingProgressBar.MarqueeAnimationSpeed = 30;

            tabPage.SizeChanged -= TabPage_SizeChanged!;
            tabPage.SizeChanged += TabPage_SizeChanged!;
        }

        private void HideTabLoading()
        {
            if (_currentLoadingTabPage == null)
            {
                return;
            }

            TabPage tabPage = _currentLoadingTabPage;

            if (_loadingPanel != null && !_loadingPanel.IsDisposed)
            {
                _loadingProgressBar!.MarqueeAnimationSpeed = 0;
                _loadingPanel.Visible = false;
            }

            tabPage.SizeChanged -= TabPage_SizeChanged!;

            _currentLoadingTabPage = null;
        }

        private void CreateLoadingPanel()
        {
            _loadingPanel = new Panel
            {
                Width = 240,
                Height = 90,
                Visible = false,
                Enabled = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _loadingLabel = new Label
            {
                Text = "Loading...",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 220,
                Height = 30,
                Left = 10,
                Top = 10,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular)
            };

            _loadingProgressBar = new ProgressBar
            {
                Width = 200,
                Height = 22,
                Left = 20,
                Top = 50,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };

            _loadingPanel.Controls.Add(_loadingLabel);
            _loadingPanel.Controls.Add(_loadingProgressBar);
        }
        private void CenterLoadingPanel(TabPage tabPage)
        {
            if (_loadingPanel == null || _loadingPanel.IsDisposed)
            {
                return;
            }

            int x = (tabPage.ClientSize.Width - _loadingPanel.Width) / 2;
            int y = (tabPage.ClientSize.Height - _loadingPanel.Height) / 2;

            if (x < 0)
            {
                x = 0;
            }

            if (y < 0)
            {
                y = 0;
            }

            _loadingPanel.Location = new Point(x, y);
        }

        private void TabPage_SizeChanged(object sender, EventArgs e)
        {
            if (sender is TabPage tabPage)
            {
                CenterLoadingPanel(tabPage);
            }
        }
        #endregion
        public Form1()
        {
            InitializeComponent();
            InitListView();
            CheckForIllegalCrossThreadCalls = false;
            FileAssociationHelper.RegisterFileAssociation();
            using Icon? appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon != null)
            {
                this.Icon = (Icon)appIcon.Clone();
            }
            if (Environment.GetCommandLineArgs().Length >= 2)
            {
                string fil = Environment.GetCommandLineArgs()[1];
                if (File.Exists(fil))
                {
                    if (HashListSearcher.IsValidFile(fil))
                    {
                        TabCtrl.SelectedIndex = 1;
                        HLFPath = V_S_HLFPath.Text = fil;
                    }
                }
                else if (Directory.Exists(fil) || fil.EndsWith(":\\$MFT", StringComparison.OrdinalIgnoreCase))
                {
                    TabCtrl.SelectedIndex = 0;
                    M_P_RootPath.Text = fil;
                }
            }
        }
        #region VirtualList+HashRow+ResultList
        private List<HashRow> _rows = new List<HashRow>();
        private int _sortColumn = -1;
        private bool _sortAscending = true;
        private void InitListView()
        {
            V_S_Result.View = View.Details;
            V_S_Result.FullRowSelect = true;
            V_S_Result.GridLines = true;
            V_S_Result.HideSelection = false;

            V_S_Result.VirtualMode = true;
            V_S_Result.VirtualListSize = 0;

            V_S_Result.RetrieveVirtualItem += V_S_Result_RetrieveVirtualItem;
            V_S_Result.ColumnClick += V_S_Result_ColumnClick;
        }

        private void V_S_Result_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= _rows.Count)
            {
                e.Item = new ListViewItem();
                return;
            }

            var row = _rows[e.ItemIndex];

            var item = new ListViewItem(row.Hash);
            item.SubItems.Add(row.FilePath);
            item.SubItems.Add(row.GetSizeDisplay(UI_ViewKBOnly.Checked, UI_IsIEC.Checked));

            e.Item = item;
        }
        private void V_S_Result_ColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (_sortColumn == e.Column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = e.Column;
                _sortAscending = true;
            }

            SortRows(_sortColumn, _sortAscending);
            UpdateColumnHeaderText();
            V_S_Result.Invalidate();
        }

        private void SortRows(int column, bool ascending)
        {
            Comparison<HashRow> comparison = column switch
            {
                0 => (a, b) => string.Compare(a.Hash, b.Hash, StringComparison.Ordinal),
                1 => (a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase),
                2 => (a, b) => a.SizeBytes.CompareTo(b.SizeBytes),
                _ => (a, b) => 0
            };

            if (ascending)
            {
                _rows.Sort(comparison);
            }
            else
            {
                _rows.Sort((a, b) => comparison(b, a));
            }
        }
        private void UpdateColumnHeaderText()
        {
            V_S_Result.Columns[0].Text = "SHA-256";
            V_S_Result.Columns[1].Text = "Path";
            V_S_Result.Columns[2].Text = "Size";

            if (_sortColumn >= 0)
            {
                V_S_Result.Columns[_sortColumn].Text += _sortAscending ? " ▲" : " ▼";
            }
        }
        public class HashRow
        {
            public string Hash { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;

            // 真实字节数，用于排序
            public ulong SizeBytes { get; set; }

            // UI显示值，用于第三列显示
            public string GetSizeDisplay(bool isKB, bool isIEC) => isKB ? Util.FormatBytesKB(SizeBytes) : Util.FormatBytes(SizeBytes, isIEC);

        }
        #endregion
        private bool IsRunning = false;

        private bool IsGlobalStop = false;
        private void M_P_SelectRootPath_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.ShowNewFolderButton = true;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                M_P_RootPath.Text = dialog.SelectedPath;
            }
        }
        private void OnStats(ScanStats stats)
        {
            M_S_Counter.Text = $"{stats.ReturnedCount} ({stats.FilteredCount} Filtered)\r\n({stats.ReturnedFileCount} Files, {stats.ReturnedDirectoryCount} Folders)";
            M_S_Stage.Text = $"Scanning disk...\r\nElapsed {stats.Elapsed}\r\nRAW T{stats.RawNodeCount}/F{stats.FileCount}/D{stats.DirectoryCount}\r\nMissing Parent {stats.ParentMissingCount}/No Path {stats.NoPathCount}";
        }
        private async void M_T_Run_Click(object sender, EventArgs e)
        {
            string M_Path = M_P_RootPath.Text;
            if ((!Directory.Exists(M_Path) && !M_Path.EndsWith(":\\$MFT", StringComparison.OrdinalIgnoreCase)) || M_Path == string.Empty)
            {
                //invalid path
                MessageBox.Show("Invalid root path, please check again.", "Warn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (File.Exists(M_P_SavePath.Text))
            {
                DialogResult dr = MessageBox.Show("Save file already exists. Append?\r\nYes = Append, No = Overwrite", "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr == DialogResult.No)
                {
                    File.Delete(M_P_SavePath.Text);
                }
                else if(dr == DialogResult.Cancel)
                {
                    Log($"Task canceled because HLF file exists.");
                    return;
                }
            }

            //run
            M_P.Enabled = M_T_Run.Enabled = false;
            IsRunning = true;
            DateTime start_t = DateTime.Now;

            IoCounter ioc = new();
            Log("Task start.");
            try
            {
                //check ntfs
                if (CheckNTFS.TryGetDriveLetter(M_Path, out char driveLetter))
                {
                    var isNtfs = CheckNTFS.IsNtfs(driveLetter);
                    if (!isNtfs.IsNTFS)
                    {
                        Log(isNtfs.Message);
                        return;
                    }
                }
                else
                {
                    Log($"Cannot get driveletter from '{M_Path}'");
                    return;
                }

                #region 扫描文件
                M_S_Stage.Text = "Scanning disk...";
                bool ismftmthd = false;
                //default
                DiskScanList R_norm = new();
                ScanResult R_mft = new([],new());

                DiskScan ds = new(M_Path);
                if (M_Path.EndsWith(":\\$MFT",StringComparison.OrdinalIgnoreCase))
                {
                    //check admin
                    if (!Util.IsRunAsAdministrator())
                    {
                        DialogResult dr = MessageBox.Show("MFT scan requires run as administrator.\r\nYes = Restart as admin", "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (dr == DialogResult.Yes)
                        {
                            Util.RestartAsAdministrator();
                        }
                        else
                        {
                            return;
                        }
                    }
                    ismftmthd = true;
                    Log("Using scan mode = MFT");
                    //mftscan
                    char drvletter = M_Path[0];
                    M_Path = $"{drvletter}:\\";
                    MFTScan mftscan = new(drvletter);
                    mftscan.StatsUpdated += OnStats;
                    R_mft = await mftscan.Scan(() => IsGlobalStop);
                    mftscan.StatsUpdated -= OnStats;
                    await Task.Delay(1000);
                    ScanStats stats = R_mft.Stats;
                    //test
                    //await File.WriteAllLinesAsync(M_P_SavePath.Text,((ScanResult)list).Entries.Select(e => e.Path),new UTF8Encoding(false));
                    //
                    M_S_Counter.Text = $"{stats.ReturnedCount} ({stats.FilteredCount} Filtered)\r\n({stats.ReturnedFileCount} Files, {stats.ReturnedDirectoryCount} Folders)";
                    M_S_Stage.Text = $"Scanning disk...\r\nElapsed {stats.Elapsed}\r\nRAW T{stats.RawNodeCount}/F{stats.FileCount}/D{stats.DirectoryCount}\r\nMissing Parent {stats.ParentMissingCount}/No Path {stats.NoPathCount}";

                    Log($"Scan finished:\r\n{stats.ReturnedCount} ({stats.FilteredCount} Filtered)\r\n({stats.ReturnedFileCount} Files, {stats.ReturnedDirectoryCount} Folders)\r\nElapsed {stats.Elapsed}\r\nRAW T{stats.RawNodeCount}/F{stats.FileCount}/D{stats.DirectoryCount}\r\nMissing Parent {stats.ParentMissingCount}/No Path {stats.NoPathCount}");
                    mftscan.Dispose();
                }
                else
                {
                    ismftmthd = false;
                    Log("Using scan mode = Normal");
                    ds.Scan();
                    // UI刷新循环
                    var uiTask = Task.Run(async () =>
                    {
                        while (!ds.IsCompleted && !ds.IsPaused)
                        {
                            M_S_Counter.Text = $"{ds.Count()}\r\n({ds.FileCount()} Files, {ds.DirectoryCount()} Folders)";
                            if (IsGlobalStop) await ds.Pause();

                            await Task.Delay(1000);
                        }
                        M_S_Counter.Text = $"{ds.Count()}\r\n({ds.FileCount()} Files, {ds.DirectoryCount()} Folders)";
                        Log($"Scan finished:\r\n{ds.Count()}\r\n({ds.FileCount()} Files, {ds.DirectoryCount()} Folders)");
                    });

                    // 业务等待扫描完成
                    await ds.Completion;

                    // 这里说明扫描自然完成
                    R_norm = ds.GetList();
                }
                #endregion

                #region 计算SHA256并存储
                M_S_Stage.Text = "Calculating SHA-256...";
                ioc.Start();
                long calced = 0, total, error = 0, exists = 0;
                if (ismftmthd)
                {
                    total = R_mft.Entries.Count;
                }
                else
                {
                    total = R_norm.Count;
                }
                bool finished = false;
                HashStorageBuilder hashStorageBuilder = HashListSearcher.IsValidFile(M_P_SavePath.Text) ? HashStorageBuilder.RestoreFromFile(M_P_SavePath.Text) : new HashStorageBuilder(capacity: (int)total / 2);
                // UI刷新循环
                var uiTask1 = Task.Run(async () =>
                {
                    while (!finished && !IsGlobalStop)
                    {
                        M_S_ShaCounter.Text = $"{calced} / {total} , {error} errors";
                        M_S_Stage.Text = $"Calculating SHA-256...\r\nSkip : {exists}\r\nDiskRead: {Util.ToMBpsString(ioc.ReadSpeed)}";
                        await Task.Delay(1000);
                    }
                    M_S_Stage.Text = $"Calculating SHA-256...\r\nSkip : {exists}\r\nDiskRead: {Util.ToMBpsString(ioc.ReadSpeed)}";
                    M_S_ShaCounter.Text = $"{calced} / {total} , {error} errors";
                    Log($"SHA-256 finished:\r\n{calced} / {total} , {error} errors\r\nSkip exists : {exists}\r\nDiskRead: {Util.ToMBpsString(ioc.ReadSpeed)}");
                    ioc.Dispose();
                });
                HashSet<string> skippedinsha256 = new HashSet<string>();
                if (!ismftmthd)
                {
                    foreach (var entry in R_norm)
                    {
                        if (hashStorageBuilder.ContainsPath(entry.Path))
                        {
                            //exists
                            skippedinsha256.Add(entry.PathString);
                            exists++;
                            continue;
                        }
                        if (!entry.IsDirectory)
                        {
                            //file
                            SHA256Calc? calc = null;
                            try
                            {
                                calc = new SHA256Calc(M_Path, entry.Path, () => IsGlobalStop);
                                hashStorageBuilder.AddFile(await calc.CalcSha256(), entry.Path, calc.GetFileLen());
                            }
                            catch (TaskCanceledException)
                            {
                                finished = true;
                                throw;//by user
                            }
                            catch (Exception ex)
                            {
                                Log($"SHA256 Error: {ex.GetType()} '{ex.Message}'");
                                error++;
                            }
                            finally
                            {
                                calc?.Dispose();
                            }
                        }
                        else
                        {
                            //dir
                            hashStorageBuilder.AddDir(entry.Path);
                        }
                        calced++;
                    }
                }
                else
                {
                    foreach (var entry in R_mft.Entries)
                    {
                        if (hashStorageBuilder.ContainsPath(entry.Path))
                        {
                            //exists
                            skippedinsha256.Add(entry.Path);
                            exists++;
                            continue;
                        }
                        if (!entry.IsDir)
                        {
                            //file
                            SHA256Calc? calc = null;
                            try
                            {
                                calc = new SHA256Calc(M_Path, entry.Path, () => IsGlobalStop);
                                hashStorageBuilder.AddFile(await calc.CalcSha256(), entry.Path, calc.GetFileLen());
                            }
                            catch (TaskCanceledException)
                            {
                                finished = true;
                                throw;//by user
                            }
                            catch (Exception ex)
                            {
                                Log($"SHA256 Error: {ex.GetType()} '{ex.Message}'");
                                error++;
                            }
                            finally
                            {
                                calc?.Dispose();
                            }
                        }
                        else
                        {
                            //dir
                            hashStorageBuilder.AddDir(entry.Path);
                        }
                        calced++;
                    }
                }
                finished = true;

                //完成计算，清理DiskScan
                if (ds != null) ds?.Dispose();
                #endregion

                #region NTFS硬链接创建
                //
                M_S_Stage.Text = "Creating hardlink...";
                NtfsHardLinkBuilder builder = new NtfsHardLinkBuilder(M_Path, hashStorageBuilder, skippedinsha256, () => IsGlobalStop);
                // UI刷新循环
                var uiTask2 = Task.Run(async () =>
                {
                    HardLinkBuilderSnapshot s;
                    while (!builder.IsCompleted && !IsGlobalStop)
                    {
                        s = builder.GetSnapshot();
                        M_S_HardLinkCounter.Text = $"Created {s.LinkCreatedCount} , {s.LinkFailedCount} errors";
                        M_S_Stage.Text = $"Creating hardlink...\r\nCreate {s.LinkCreatedCount}\r\nSHA-256 Entry {s.EntryProcessedCount}/{hashStorageBuilder.EntryCount}\r\nSkip {s.LinkSkippedCount}";
                        foreach (string e in builder.DrainErrors()) Log(e);
                        await Task.Delay(1000);
                    }
                    s = builder.GetSnapshot();
                    M_S_HardLinkCounter.Text = $"Created {s.LinkCreatedCount} , {s.LinkFailedCount} errors";
                    foreach (string e in builder.DrainErrors()) Log(e);

                    Log($"Create hardlink finished:\r\nCreated {s.LinkCreatedCount} , {s.LinkFailedCount} errors\r\nCreate {s.LinkCreatedCount}\r\nSHA-256 Entry {s.EntryProcessedCount}/{hashStorageBuilder.EntryCount}\r\nSkip {s.LinkSkippedCount}");
                });
                await builder.BuildAsync();
                #endregion

                #region 写入HashList
                M_S_Stage.Text = "Writing HashList...";
                M_S_HLF.Text = "Processing...";
                await Task.Run(() => hashStorageBuilder.WriteToFile(M_P_SavePath.Text));
                M_S_HLF.Text = "Done!";
                #endregion

                //end
                skippedinsha256.Clear();
                M_S_Stage.Text = "Done.";
                Log("Done.");
            }
            catch (TaskCanceledException)
            {
                ioc.Dispose();
                Log("Task stopped by user.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[Exception]\r\n{ex.Message}\r\n[InnerEx]\r\n{(ex.InnerException != null ? ex.InnerException.Message : string.Empty)}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                //结束并重置
                IsRunning = IsGlobalStop = false;
                M_P.Enabled = M_T_Run.Enabled = true;
                TimeSpan t = DateTime.Now - start_t;
                Log($"Time elapsed : {t}");

                await Task.Delay(500);
                MessageBox.Show($"Done!\r\nElapsed time: {t}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await Task.Delay(500);

                M_S_Counter.Text = M_S_HardLinkCounter.Text = M_S_ShaCounter.Text = M_S_HLF.Text = M_S_Stage.Text = "Waiting...";
            }
        }
        private readonly Lock _lock = new Lock();
        public void Log(string msg)
        {
            lock (_lock)
            {
                M_S_Log.AppendText($"[{DateTime.Now}] {msg}\r\n");
            }
        }

        private void M_T_Stop_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("Stop task?\r\nCan not resume!", "Info", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr == DialogResult.Yes)
            {
                if (IsRunning) IsGlobalStop = true;
            }
        }

        private void M_P_SelectSavePath_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Save HashList file";
                dialog.Filter = "HashList Format (*.hlf)|*.hlf|All (*.*)|*.*";
                dialog.DefaultExt = "hlf";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;

                // 默认文件名
                dialog.FileName = "New List.hlf";


                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    M_P_SavePath.Text = dialog.FileName;
                }
            }
        }

        #region HashListSearch
        private HashListSearcher? searcher;
        private string HLFPath = string.Empty;

        //状态缓存
        private int _TmpResultCount = 0;
        private ulong _TmpTotalSize = 0;
        private bool _TmpExceedLim = false,_TmpHasResult = false;

        private void V_S_Search_Click(object sender, EventArgs e)
        {
            var u = Task.Run(async () =>
            {
                Search();
            });
        }

        private void V_S_Keywords_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // 可选：阻止“滴”一声
                var u = Task.Run(async () =>
                {
                    Search();
                });
            }
        }
        private async void Search()
        {
            V_S.Enabled = V_S_HLFPath.Enabled = V_S_Result.Enabled = false;
            if (searcher == null)
            {
                MessageBox.Show("No loaded HashList file, please check.", "Warn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                V_S.Enabled = V_S_HLFPath.Enabled = V_S_Result.Enabled = true;
                return;
            }
            searcher.MaxResult = (int)V_S_Max.Value;
            //search
            _rows.Clear();
            V_S_Result.VirtualListSize = 0;


            ShowTabLoading(Page_View);
            if (V_S_Keywords.Text.StartsWith("H|"))
            {
                if (Util.TryParseSha256(V_S_Keywords.Text[2..], out var bytes))
                {
                    ReadOnlySpan<byte> span = bytes;
                    if (searcher.TryFindByHash(span, out var byHash))
                    {
                        var data = new List<HashRow>(byHash!.Count);
                        Span<byte> hash = stackalloc byte[32];
                        ulong total = 0;
                        foreach (var item in byHash!)
                        {
                            //ui
                            item.Hash.CopyTo(hash);
                            data.Add(new HashRow
                            {
                                Hash = Convert.ToHexString(hash),
                                FilePath = item.Path,
                                SizeBytes = item.Size
                            });
                            total += item.Size;
                        }
                        _rows = data;

                        SaveSResultsInfo(true, false, total, byHash!.Count);
                    }
                    else
                    {
                        SaveSResultsInfo(false, false,0, 0);
                    }
                }
                else
                {
                    MessageBox.Show("Invalid SHA256 string, please check again.", "Warn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                //common keywords
                HashListSearcher.SearchResult sr = searcher.FindByKeyword(V_S_Keywords.Text);
                var data = new List<HashRow>(sr.Items.Count);
                Span<byte> hash = stackalloc byte[32];
                ulong total = 0;
                foreach (var item in sr.Items)
                {
                    //ui
                    item.Hash.CopyTo(hash);
                    data.Add(new HashRow
                    {
                        Hash = Convert.ToHexString(hash),
                        FilePath = item.Path,
                        SizeBytes = item.Size
                    });
                    total += item.Size;
                }
                _rows = data;

                SaveSResultsInfo(true, sr.ExceededMaxResults, total, sr.Items.Count);
            }
            V_S_Result.VirtualListSize = _rows.Count;
            V_S_Result.Invalidate();
            ShowSResultsInfo();
            V_S.Enabled = V_S_HLFPath.Enabled = V_S_Result.Enabled = true;
            HideTabLoading();
        }
        private void SaveSResultsInfo(bool hasresult,bool exceedlim,ulong size,int count)
        {
            lock (_lock)
            {
                _TmpHasResult = hasresult;
                _TmpExceedLim = exceedlim;
                _TmpTotalSize = size;
                _TmpResultCount = count;
            }
        }
        private void ShowSResultsInfo() => V_ResultsC.Text = _TmpExceedLim
                    ? $"Results: {_TmpResultCount:N0} ({_TmpTotalSize:N0} B, {Util.FormatBytes(_TmpTotalSize, UI_IsIEC.Checked)}) (Warn:Max results exceeded.)"
                    : $"Results: {_TmpResultCount:N0} ({_TmpTotalSize:N0} B, {Util.FormatBytes(_TmpTotalSize, UI_IsIEC.Checked)})";


        private void V_S_Max_ValueChanged(object sender, EventArgs e)
        {
            searcher?.MaxResult = (int)V_S_Max.Value;
        }

        private async void V_S_HLFPath_TextChanged(object sender, EventArgs e)
        {
            //基础检查
            if (!File.Exists(V_S_HLFPath.Text)) return;
            V_S.Enabled = V_S_HLFPath.Enabled = V_S_Result.Enabled = false;
            if (searcher == null)
            {
                //init
                HLFPath = V_S_HLFPath.Text;
                if (!HashListSearcher.IsValidFile(HLFPath))
                {
                    MessageBox.Show("Invalid HashList file, please check again.", "Warn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    V_S.Enabled = V_S_HLFPath.Enabled = V_S_Result.Enabled = true;
                    return;
                }

                ShowTabLoading(Page_View);
                searcher = await HashListSearcher.OpenAsync(HLFPath);
                HideTabLoading();
            }
            else
            {
                if (HLFPath != V_S_HLFPath.Text)
                {
                    HLFPath = V_S_HLFPath.Text;
                    if (!HashListSearcher.IsValidFile(HLFPath))
                    {
                        MessageBox.Show("Invalid HashList file, please check again.", "Warn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        V_S.Enabled = V_S_HLFPath.Enabled = V_S_Result.Enabled = true;
                        return;
                    }
                    //re-init
                    searcher.Dispose();

                    ShowTabLoading(Page_View);
                    searcher = await HashListSearcher.OpenAsync(HLFPath);
                    HideTabLoading();
                }
            }

            HashListSearcher.HashListInfo i = searcher.GetCounts();
            V_Count.Text = $"Loaded {i.HashCount:N0} hashes and {i.FileCount:N0} items";
            V_S.Enabled = V_S_HLFPath.Enabled = V_S_Result.Enabled = true;
        }

        private void T_Copy_Click(object sender, EventArgs e)
        {
            if (V_S_Result.SelectedIndices.Count == 0) return;

            int i = V_S_Result.SelectedIndices[0];
            try
            {
                Clipboard.SetText($"{_rows[i].Hash}\t{_rows[i].FilePath}\t{_rows[i].GetSizeDisplay(UI_ViewKBOnly.Checked, UI_IsIEC.Checked)}");
            }
            catch (Exception ex)
            {
                Log($"Clipboard error : {ex.Message}");
            }
        }

        private void T_CopyHash_Click(object sender, EventArgs e)
        {
            if (V_S_Result.SelectedIndices.Count == 0) return;

            int i = V_S_Result.SelectedIndices[0];
            try
            {
                Clipboard.SetText(_rows[i].Hash);
            }
            catch (Exception ex)
            {
                Log($"Clipboard error : {ex.Message}");
            }
        }

        private void T_CopyPath_Click(object sender, EventArgs e)
        {
            if (V_S_Result.SelectedIndices.Count == 0) return;

            int i = V_S_Result.SelectedIndices[0];
            try
            {
                Clipboard.SetText(_rows[i].FilePath);
            }
            catch (Exception ex)
            {
                Log($"Clipboard error : {ex.Message}");
            }
        }

        private void T_CopySize_Click(object sender, EventArgs e)
        {
            if (V_S_Result.SelectedIndices.Count == 0) return;

            int i = V_S_Result.SelectedIndices[0];
            try
            {
                Clipboard.SetText(_rows[i].GetSizeDisplay(UI_ViewKBOnly.Checked, UI_IsIEC.Checked));
            }
            catch (Exception ex)
            {
                Log($"Clipboard error : {ex.Message}");
            }
        }

        private void T_CopySizeRaw_Click(object sender, EventArgs e)
        {
            if (V_S_Result.SelectedIndices.Count == 0) return;

            int i = V_S_Result.SelectedIndices[0];
            try
            {
                Clipboard.SetText(_rows[i].SizeBytes.ToString());
            }
            catch (Exception ex)
            {
                Log($"Clipboard error : {ex.Message}");
            }
        }
        private void UI_IsIEC_Click(object sender, EventArgs e)
        {
            V_S_Result.Invalidate();
            ShowSResultsInfo();
        }

        private void UI_ViewKBOnly_Click(object sender, EventArgs e)
        {
            V_S_Result.Invalidate();
            ShowSResultsInfo();
        }

        #endregion

        private void button1_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "HashList Format (*.hlf)|*.hlf|All (*.*)|*.*";
                dialog.DefaultExt = "hlf";
                dialog.AddExtension = true;
                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.Multiselect = false;
                dialog.ValidateNames = true;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    V_S_HLFPath.Text = dialog.FileName;
                }
            }
        }


        private void openHashListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            button1_Click(sender, e);

            TabCtrl.SelectedIndex = 1;
        }

        private void runAsAdministratorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Util.RestartAsAdministrator();
        }

        private void M_S_LogClear_Click(object sender, EventArgs e)
        {
            lock (_lock)
            {
                M_S_Log.Clear();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && IsRunning)
            {
                DialogResult result = MessageBox.Show(
                    "Are you sure you want to close this window? Any processed data will be lost.",
                    "Warn",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );

                if (result == DialogResult.No)
                {
                    e.Cancel = true; // 取消关闭
                }
            }
        }
    }
}
