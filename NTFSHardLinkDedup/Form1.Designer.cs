namespace NTFSHardLinkDedup
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            openHashListToolStripMenuItem = new ToolStripMenuItem();
            operationsToolStripMenuItem = new ToolStripMenuItem();
            runAsAdministratorToolStripMenuItem = new ToolStripMenuItem();
            TabCtrl = new TabControl();
            Page_Make = new TabPage();
            groupBox3 = new GroupBox();
            M_S_LogClear = new Button();
            label15 = new Label();
            label6 = new Label();
            M_S_Log = new TextBox();
            M_S_HLF = new Label();
            label9 = new Label();
            M_S_HardLinkCounter = new Label();
            label10 = new Label();
            M_S_ShaCounter = new Label();
            label8 = new Label();
            M_S_Counter = new Label();
            label7 = new Label();
            M_S_Stage = new Label();
            label5 = new Label();
            groupBox2 = new GroupBox();
            M_T_Stop = new Button();
            M_T_Run = new Button();
            M_P = new GroupBox();
            M_P_SelectSavePath = new Button();
            M_P_SavePath = new TextBox();
            label4 = new Label();
            M_P_SelectRootPath = new Button();
            M_P_RootPath = new TextBox();
            label3 = new Label();
            label2 = new Label();
            label1 = new Label();
            Page_View = new TabPage();
            V_Count = new Label();
            V_ResultsC = new Label();
            V_S_Result = new ListView();
            columnHeader1 = new ColumnHeader();
            columnHeader2 = new ColumnHeader();
            columnHeader3 = new ColumnHeader();
            contextMenuStrip1 = new ContextMenuStrip(components);
            T_Copy = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            T_CopyHash = new ToolStripMenuItem();
            T_CopyPath = new ToolStripMenuItem();
            T_CopySize = new ToolStripMenuItem();
            T_CopySizeRaw = new ToolStripMenuItem();
            V_S = new GroupBox();
            V_S_Max = new NumericUpDown();
            label14 = new Label();
            V_S_Search = new Button();
            V_S_Keywords = new TextBox();
            button1 = new Button();
            V_S_HLFPath = new TextBox();
            label13 = new Label();
            label11 = new Label();
            label12 = new Label();
            menuStrip1.SuspendLayout();
            TabCtrl.SuspendLayout();
            Page_Make.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox2.SuspendLayout();
            M_P.SuspendLayout();
            Page_View.SuspendLayout();
            contextMenuStrip1.SuspendLayout();
            V_S.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)V_S_Max).BeginInit();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, operationsToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(988, 25);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openHashListToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(39, 21);
            fileToolStripMenuItem.Text = "File";
            // 
            // openHashListToolStripMenuItem
            // 
            openHashListToolStripMenuItem.Name = "openHashListToolStripMenuItem";
            openHashListToolStripMenuItem.Size = new Size(160, 22);
            openHashListToolStripMenuItem.Text = "Open HashList";
            openHashListToolStripMenuItem.Click += openHashListToolStripMenuItem_Click;
            // 
            // operationsToolStripMenuItem
            // 
            operationsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { runAsAdministratorToolStripMenuItem });
            operationsToolStripMenuItem.Name = "operationsToolStripMenuItem";
            operationsToolStripMenuItem.Size = new Size(85, 21);
            operationsToolStripMenuItem.Text = "Operations";
            // 
            // runAsAdministratorToolStripMenuItem
            // 
            runAsAdministratorToolStripMenuItem.Name = "runAsAdministratorToolStripMenuItem";
            runAsAdministratorToolStripMenuItem.Size = new Size(197, 22);
            runAsAdministratorToolStripMenuItem.Text = "Run as administrator";
            runAsAdministratorToolStripMenuItem.Click += runAsAdministratorToolStripMenuItem_Click;
            // 
            // TabCtrl
            // 
            TabCtrl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            TabCtrl.Controls.Add(Page_Make);
            TabCtrl.Controls.Add(Page_View);
            TabCtrl.Location = new Point(12, 28);
            TabCtrl.Name = "TabCtrl";
            TabCtrl.SelectedIndex = 0;
            TabCtrl.Size = new Size(964, 474);
            TabCtrl.TabIndex = 1;
            // 
            // Page_Make
            // 
            Page_Make.Controls.Add(groupBox3);
            Page_Make.Controls.Add(groupBox2);
            Page_Make.Controls.Add(M_P);
            Page_Make.Controls.Add(label2);
            Page_Make.Controls.Add(label1);
            Page_Make.Location = new Point(4, 26);
            Page_Make.Name = "Page_Make";
            Page_Make.Padding = new Padding(3);
            Page_Make.Size = new Size(956, 444);
            Page_Make.TabIndex = 0;
            Page_Make.Text = "Make";
            Page_Make.UseVisualStyleBackColor = true;
            // 
            // groupBox3
            // 
            groupBox3.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            groupBox3.Controls.Add(M_S_LogClear);
            groupBox3.Controls.Add(label15);
            groupBox3.Controls.Add(label6);
            groupBox3.Controls.Add(M_S_Log);
            groupBox3.Controls.Add(M_S_HLF);
            groupBox3.Controls.Add(label9);
            groupBox3.Controls.Add(M_S_HardLinkCounter);
            groupBox3.Controls.Add(label10);
            groupBox3.Controls.Add(M_S_ShaCounter);
            groupBox3.Controls.Add(label8);
            groupBox3.Controls.Add(M_S_Counter);
            groupBox3.Controls.Add(label7);
            groupBox3.Controls.Add(M_S_Stage);
            groupBox3.Controls.Add(label5);
            groupBox3.Location = new Point(137, 116);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(813, 304);
            groupBox3.TabIndex = 4;
            groupBox3.TabStop = false;
            groupBox3.Text = "3. Status indicator";
            // 
            // M_S_LogClear
            // 
            M_S_LogClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            M_S_LogClear.Location = new Point(732, 13);
            M_S_LogClear.Name = "M_S_LogClear";
            M_S_LogClear.Size = new Size(75, 23);
            M_S_LogClear.TabIndex = 13;
            M_S_LogClear.Text = "Clear";
            M_S_LogClear.UseVisualStyleBackColor = true;
            M_S_LogClear.Click += M_S_LogClear_Click;
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(10, 108);
            label15.Name = "label15";
            label15.Size = new Size(233, 17);
            label15.TabIndex = 12;
            label15.Text = "---------------------------------------------";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(245, 16);
            label6.Name = "label6";
            label6.Size = new Size(30, 17);
            label6.TabIndex = 11;
            label6.Text = "Log";
            // 
            // M_S_Log
            // 
            M_S_Log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            M_S_Log.Location = new Point(245, 36);
            M_S_Log.MaxLength = int.MaxValue;
            M_S_Log.Multiline = true;
            M_S_Log.Name = "M_S_Log";
            M_S_Log.ReadOnly = true;
            M_S_Log.ScrollBars = ScrollBars.Vertical;
            M_S_Log.Size = new Size(562, 262);
            M_S_Log.TabIndex = 10;
            // 
            // M_S_HLF
            // 
            M_S_HLF.AutoSize = true;
            M_S_HLF.Location = new Point(37, 279);
            M_S_HLF.Name = "M_S_HLF";
            M_S_HLF.Size = new Size(61, 17);
            M_S_HLF.TabIndex = 9;
            M_S_HLF.Text = "Waiting...";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label9.Location = new Point(10, 259);
            label9.Name = "label9";
            label9.Size = new Size(149, 20);
            label9.TabIndex = 8;
            label9.Text = "HashList file creating";
            // 
            // M_S_HardLinkCounter
            // 
            M_S_HardLinkCounter.AutoSize = true;
            M_S_HardLinkCounter.Location = new Point(37, 242);
            M_S_HardLinkCounter.Name = "M_S_HardLinkCounter";
            M_S_HardLinkCounter.Size = new Size(61, 17);
            M_S_HardLinkCounter.TabIndex = 7;
            M_S_HardLinkCounter.Text = "Waiting...";
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label10.Location = new Point(10, 222);
            label10.Name = "label10";
            label10.Size = new Size(126, 20);
            label10.TabIndex = 6;
            label10.Text = "Hardlink creating";
            // 
            // M_S_ShaCounter
            // 
            M_S_ShaCounter.AutoSize = true;
            M_S_ShaCounter.Location = new Point(37, 205);
            M_S_ShaCounter.Name = "M_S_ShaCounter";
            M_S_ShaCounter.Size = new Size(61, 17);
            M_S_ShaCounter.TabIndex = 5;
            M_S_ShaCounter.Text = "Waiting...";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label8.Location = new Point(10, 185);
            label8.Name = "label8";
            label8.Size = new Size(144, 20);
            label8.TabIndex = 4;
            label8.Text = "SHA-256 processing";
            // 
            // M_S_Counter
            // 
            M_S_Counter.Location = new Point(37, 145);
            M_S_Counter.Name = "M_S_Counter";
            M_S_Counter.Size = new Size(202, 40);
            M_S_Counter.TabIndex = 3;
            M_S_Counter.Text = "Waiting...";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label7.Location = new Point(10, 125);
            label7.Name = "label7";
            label7.Size = new Size(110, 20);
            label7.TabIndex = 2;
            label7.Text = "Items scanning";
            // 
            // M_S_Stage
            // 
            M_S_Stage.Location = new Point(10, 36);
            M_S_Stage.Name = "M_S_Stage";
            M_S_Stage.Size = new Size(229, 72);
            M_S_Stage.TabIndex = 1;
            M_S_Stage.Text = "Waiting...";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label5.Location = new Point(10, 16);
            label5.Name = "label5";
            label5.Size = new Size(47, 20);
            label5.TabIndex = 0;
            label5.Text = "Stage";
            // 
            // groupBox2
            // 
            groupBox2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            groupBox2.Controls.Add(M_T_Stop);
            groupBox2.Controls.Add(M_T_Run);
            groupBox2.Location = new Point(6, 116);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(125, 304);
            groupBox2.TabIndex = 3;
            groupBox2.TabStop = false;
            groupBox2.Text = "2. Task controller";
            // 
            // M_T_Stop
            // 
            M_T_Stop.Location = new Point(6, 51);
            M_T_Stop.Name = "M_T_Stop";
            M_T_Stop.Size = new Size(113, 23);
            M_T_Stop.TabIndex = 1;
            M_T_Stop.Text = "Stop";
            M_T_Stop.UseVisualStyleBackColor = true;
            M_T_Stop.Click += M_T_Stop_Click;
            // 
            // M_T_Run
            // 
            M_T_Run.Location = new Point(6, 22);
            M_T_Run.Name = "M_T_Run";
            M_T_Run.Size = new Size(113, 23);
            M_T_Run.TabIndex = 0;
            M_T_Run.Text = "Run";
            M_T_Run.UseVisualStyleBackColor = true;
            M_T_Run.Click += M_T_Run_Click;
            // 
            // M_P
            // 
            M_P.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            M_P.Controls.Add(M_P_SelectSavePath);
            M_P.Controls.Add(M_P_SavePath);
            M_P.Controls.Add(label4);
            M_P.Controls.Add(M_P_SelectRootPath);
            M_P.Controls.Add(M_P_RootPath);
            M_P.Controls.Add(label3);
            M_P.Location = new Point(6, 27);
            M_P.Name = "M_P";
            M_P.Size = new Size(944, 83);
            M_P.TabIndex = 2;
            M_P.TabStop = false;
            M_P.Text = "1. Parameter settings";
            // 
            // M_P_SelectSavePath
            // 
            M_P_SelectSavePath.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            M_P_SelectSavePath.Location = new Point(867, 45);
            M_P_SelectSavePath.Name = "M_P_SelectSavePath";
            M_P_SelectSavePath.Size = new Size(71, 23);
            M_P_SelectSavePath.TabIndex = 5;
            M_P_SelectSavePath.Text = "Select";
            M_P_SelectSavePath.UseVisualStyleBackColor = true;
            M_P_SelectSavePath.Click += M_P_SelectSavePath_Click;
            // 
            // M_P_SavePath
            // 
            M_P_SavePath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            M_P_SavePath.Location = new Point(84, 45);
            M_P_SavePath.Name = "M_P_SavePath";
            M_P_SavePath.PlaceholderText = "Select or input the save path of hash list (*.hlf).";
            M_P_SavePath.Size = new Size(777, 23);
            M_P_SavePath.TabIndex = 4;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(10, 51);
            label4.Name = "label4";
            label4.Size = new Size(67, 17);
            label4.TabIndex = 3;
            label4.Text = "Save Path:";
            // 
            // M_P_SelectRootPath
            // 
            M_P_SelectRootPath.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            M_P_SelectRootPath.Location = new Point(867, 16);
            M_P_SelectRootPath.Name = "M_P_SelectRootPath";
            M_P_SelectRootPath.Size = new Size(71, 23);
            M_P_SelectRootPath.TabIndex = 2;
            M_P_SelectRootPath.Text = "Select";
            M_P_SelectRootPath.UseVisualStyleBackColor = true;
            M_P_SelectRootPath.Click += M_P_SelectRootPath_Click;
            // 
            // M_P_RootPath
            // 
            M_P_RootPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            M_P_RootPath.Location = new Point(84, 16);
            M_P_RootPath.Name = "M_P_RootPath";
            M_P_RootPath.PlaceholderText = "Select or input the root path to scan. (e.g. D:\\) Using MFT path like D:\\$MFT to enable MFT scanning.";
            M_P_RootPath.Size = new Size(777, 23);
            M_P_RootPath.TabIndex = 1;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(10, 22);
            label3.Name = "label3";
            label3.Size = new Size(68, 17);
            label3.TabIndex = 0;
            label3.Text = "Root Path:";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(65, 7);
            label2.Name = "label2";
            label2.Size = new Size(296, 17);
            label2.TabIndex = 1;
            label2.Text = "Scan disk, calculate SHA-256 and make hardlinks.";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label1.Location = new Point(6, 3);
            label1.Name = "label1";
            label1.Size = new Size(53, 21);
            label1.TabIndex = 0;
            label1.Text = "Make";
            // 
            // Page_View
            // 
            Page_View.Controls.Add(V_Count);
            Page_View.Controls.Add(V_ResultsC);
            Page_View.Controls.Add(V_S_Result);
            Page_View.Controls.Add(V_S);
            Page_View.Controls.Add(button1);
            Page_View.Controls.Add(V_S_HLFPath);
            Page_View.Controls.Add(label13);
            Page_View.Controls.Add(label11);
            Page_View.Controls.Add(label12);
            Page_View.Location = new Point(4, 26);
            Page_View.Name = "Page_View";
            Page_View.Padding = new Padding(3);
            Page_View.Size = new Size(956, 444);
            Page_View.TabIndex = 1;
            Page_View.Text = "View";
            Page_View.UseVisualStyleBackColor = true;
            // 
            // V_Count
            // 
            V_Count.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            V_Count.Location = new Point(600, 148);
            V_Count.Name = "V_Count";
            V_Count.Size = new Size(350, 17);
            V_Count.TabIndex = 12;
            V_Count.Text = "Ready";
            V_Count.TextAlign = ContentAlignment.TopRight;
            // 
            // V_ResultsC
            // 
            V_ResultsC.AutoSize = true;
            V_ResultsC.Location = new Point(6, 148);
            V_ResultsC.Name = "V_ResultsC";
            V_ResultsC.Size = new Size(52, 17);
            V_ResultsC.TabIndex = 11;
            V_ResultsC.Text = "Results:";
            // 
            // V_S_Result
            // 
            V_S_Result.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            V_S_Result.Columns.AddRange(new ColumnHeader[] { columnHeader1, columnHeader2, columnHeader3 });
            V_S_Result.ContextMenuStrip = contextMenuStrip1;
            V_S_Result.GridLines = true;
            V_S_Result.Location = new Point(6, 170);
            V_S_Result.Name = "V_S_Result";
            V_S_Result.Size = new Size(944, 265);
            V_S_Result.TabIndex = 10;
            V_S_Result.UseCompatibleStateImageBehavior = false;
            V_S_Result.View = View.Details;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "SHA-256";
            columnHeader1.Width = 250;
            // 
            // columnHeader2
            // 
            columnHeader2.Text = "Path";
            columnHeader2.Width = 550;
            // 
            // columnHeader3
            // 
            columnHeader3.Text = "Size";
            columnHeader3.Width = 100;
            // 
            // contextMenuStrip1
            // 
            contextMenuStrip1.Items.AddRange(new ToolStripItem[] { T_Copy, toolStripSeparator1, T_CopyHash, T_CopyPath, T_CopySize, T_CopySizeRaw });
            contextMenuStrip1.Name = "contextMenuStrip1";
            contextMenuStrip1.Size = new Size(170, 120);
            // 
            // T_Copy
            // 
            T_Copy.Name = "T_Copy";
            T_Copy.Size = new Size(169, 22);
            T_Copy.Text = "Copy";
            T_Copy.Click += T_Copy_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(166, 6);
            // 
            // T_CopyHash
            // 
            T_CopyHash.Name = "T_CopyHash";
            T_CopyHash.Size = new Size(169, 22);
            T_CopyHash.Text = "Copy Hash";
            T_CopyHash.Click += T_CopyHash_Click;
            // 
            // T_CopyPath
            // 
            T_CopyPath.Name = "T_CopyPath";
            T_CopyPath.Size = new Size(169, 22);
            T_CopyPath.Text = "Copy Path";
            T_CopyPath.Click += T_CopyPath_Click;
            // 
            // T_CopySize
            // 
            T_CopySize.Name = "T_CopySize";
            T_CopySize.Size = new Size(169, 22);
            T_CopySize.Text = "Copy Size";
            T_CopySize.Click += T_CopySize_Click;
            // 
            // T_CopySizeRaw
            // 
            T_CopySizeRaw.Name = "T_CopySizeRaw";
            T_CopySizeRaw.Size = new Size(169, 22);
            T_CopySizeRaw.Text = "Copy Size (Raw)";
            T_CopySizeRaw.Click += T_CopySizeRaw_Click;
            // 
            // V_S
            // 
            V_S.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            V_S.Controls.Add(V_S_Max);
            V_S.Controls.Add(label14);
            V_S.Controls.Add(V_S_Search);
            V_S.Controls.Add(V_S_Keywords);
            V_S.Location = new Point(6, 60);
            V_S.Name = "V_S";
            V_S.Size = new Size(944, 85);
            V_S.TabIndex = 9;
            V_S.TabStop = false;
            V_S.Text = "Searching options";
            // 
            // V_S_Max
            // 
            V_S_Max.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            V_S_Max.Location = new Point(94, 51);
            V_S_Max.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
            V_S_Max.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            V_S_Max.Name = "V_S_Max";
            V_S_Max.Size = new Size(120, 23);
            V_S_Max.TabIndex = 3;
            V_S_Max.Value = new decimal(new int[] { 20000, 0, 0, 0 });
            V_S_Max.ValueChanged += V_S_Max_ValueChanged;
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(6, 54);
            label14.Name = "label14";
            label14.Size = new Size(82, 17);
            label14.TabIndex = 2;
            label14.Text = "Max results: ";
            // 
            // V_S_Search
            // 
            V_S_Search.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            V_S_Search.Location = new Point(867, 51);
            V_S_Search.Name = "V_S_Search";
            V_S_Search.Size = new Size(71, 23);
            V_S_Search.TabIndex = 1;
            V_S_Search.Text = "Search";
            V_S_Search.UseVisualStyleBackColor = true;
            V_S_Search.Click += V_S_Search_Click;
            // 
            // V_S_Keywords
            // 
            V_S_Keywords.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            V_S_Keywords.Location = new Point(6, 22);
            V_S_Keywords.MaxLength = 65536;
            V_S_Keywords.Name = "V_S_Keywords";
            V_S_Keywords.PlaceholderText = "Input keywords(AND: space,OR: |,Wholematch: \"...\") . (pattern: H| + Sha256 to search sha256, * to show all) ";
            V_S_Keywords.Size = new Size(932, 23);
            V_S_Keywords.TabIndex = 0;
            V_S_Keywords.KeyDown += V_S_Keywords_KeyDown;
            // 
            // button1
            // 
            button1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button1.Location = new Point(879, 31);
            button1.Name = "button1";
            button1.Size = new Size(71, 23);
            button1.TabIndex = 8;
            button1.Text = "Select";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // V_S_HLFPath
            // 
            V_S_HLFPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            V_S_HLFPath.Location = new Point(100, 31);
            V_S_HLFPath.Name = "V_S_HLFPath";
            V_S_HLFPath.PlaceholderText = "Select or input the path of hash list (*.hlf).";
            V_S_HLFPath.Size = new Size(773, 23);
            V_S_HLFPath.TabIndex = 7;
            V_S_HLFPath.TextChanged += V_S_HLFPath_TextChanged;
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(6, 37);
            label13.Name = "label13";
            label13.Size = new Size(88, 17);
            label13.TabIndex = 6;
            label13.Text = "HashList Path:";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(65, 7);
            label11.Name = "label11";
            label11.Size = new Size(192, 17);
            label11.TabIndex = 3;
            label11.Text = "Open,view and searching items.";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            label12.Location = new Point(6, 3);
            label12.Name = "label12";
            label12.Size = new Size(47, 21);
            label12.TabIndex = 2;
            label12.Text = "View";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(988, 514);
            Controls.Add(TabCtrl);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "Form1";
            Text = "NTFS HardLink Deduplicator";
            FormClosing += Form1_FormClosing;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            TabCtrl.ResumeLayout(false);
            Page_Make.ResumeLayout(false);
            Page_Make.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox2.ResumeLayout(false);
            M_P.ResumeLayout(false);
            M_P.PerformLayout();
            Page_View.ResumeLayout(false);
            Page_View.PerformLayout();
            contextMenuStrip1.ResumeLayout(false);
            V_S.ResumeLayout(false);
            V_S.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)V_S_Max).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private TabControl TabCtrl;
        private TabPage Page_Make;
        private TabPage Page_View;
        private Label label1;
        private Label label2;
        private GroupBox M_P;
        private Button M_P_SelectRootPath;
        private TextBox M_P_RootPath;
        private Label label3;
        private Button M_P_SelectSavePath;
        private TextBox M_P_SavePath;
        private Label label4;
        private GroupBox groupBox2;
        private GroupBox groupBox3;
        private Button M_T_Run;
        private Button M_T_Stop;
        private Label M_S_Counter;
        private Label label7;
        private Label M_S_Stage;
        private Label M_S_HardLinkCounter;
        private Label label10;
        private Label M_S_ShaCounter;
        private Label label8;
        private Label M_S_HLF;
        private Label label9;
        private TextBox M_S_Log;
        private Label label6;
        private Label label11;
        private Label label12;
        private Button button1;
        private TextBox V_S_HLFPath;
        private Label label13;
        private GroupBox V_S;
        private Button V_S_Search;
        private TextBox V_S_Keywords;
        private NumericUpDown V_S_Max;
        private Label label14;
        private ListView V_S_Result;
        private Label V_ResultsC;
        private ColumnHeader columnHeader1;
        private ColumnHeader columnHeader2;
        private ToolStripMenuItem openHashListToolStripMenuItem;
        private Label label15;
        private ToolStripMenuItem operationsToolStripMenuItem;
        private ToolStripMenuItem runAsAdministratorToolStripMenuItem;
        private Button M_S_LogClear;
        private ColumnHeader columnHeader3;
        private Label V_Count;
        private ContextMenuStrip contextMenuStrip1;
        private ToolStripMenuItem T_Copy;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem T_CopyHash;
        private ToolStripMenuItem T_CopyPath;
        private ToolStripMenuItem T_CopySize;
        private ToolStripMenuItem T_CopySizeRaw;
        private Label label5;
    }
}
