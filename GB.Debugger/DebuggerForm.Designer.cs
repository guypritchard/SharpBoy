namespace GB.Debugger;

partial class DebuggerForm
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
        this.toolbarPanel = new System.Windows.Forms.FlowLayoutPanel();
        this.loadButton = new System.Windows.Forms.Button();
        this.stepButton = new System.Windows.Forms.Button();
        this.stepBackButton = new System.Windows.Forms.Button();
        this.resetButton = new System.Windows.Forms.Button();
        this.statusLabel = new System.Windows.Forms.Label();
        this.sidebarPanel = new System.Windows.Forms.Panel();
        this.stackGroupBox = new System.Windows.Forms.GroupBox();
        this.stackLegendLabel = new System.Windows.Forms.Label();
        this.stackListBox = new System.Windows.Forms.ListBox();
        this.registersGroupBox = new System.Windows.Forms.GroupBox();
        this.registerTable = new System.Windows.Forms.TableLayoutPanel();
        this.interruptsGroupBox = new System.Windows.Forms.GroupBox();
        this.interruptsTable = new System.Windows.Forms.TableLayoutPanel();
        this.interruptNameHeaderLabel = new System.Windows.Forms.Label();
        this.interruptIfHeaderLabel = new System.Windows.Forms.Label();
        this.interruptIeHeaderLabel = new System.Windows.Forms.Label();
        this.interruptVblankLabel = new System.Windows.Forms.Label();
        this.interruptLcdStatLabel = new System.Windows.Forms.Label();
        this.interruptTimerLabel = new System.Windows.Forms.Label();
        this.interruptSerialLabel = new System.Windows.Forms.Label();
        this.interruptJoypadLabel = new System.Windows.Forms.Label();
        this.interruptIfVblankCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIeVblankCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIfLcdStatCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIeLcdStatCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIfTimerCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIeTimerCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIfSerialCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIeSerialCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIfJoypadCheckBox = new System.Windows.Forms.CheckBox();
        this.interruptIeJoypadCheckBox = new System.Windows.Forms.CheckBox();
        this.mainSplitContainer = new System.Windows.Forms.SplitContainer();
        this.codePanel = new System.Windows.Forms.Panel();
        this.codeListBox = new System.Windows.Forms.ListBox();
        this.codeLegendLabel = new System.Windows.Forms.Label();
        this.memoryGroupBox = new System.Windows.Forms.GroupBox();
        this.memoryLegendLabel = new System.Windows.Forms.Label();
        this.memoryListBox = new System.Windows.Forms.ListBox();
        this.openRomDialog = new System.Windows.Forms.OpenFileDialog();
        this.toolbarPanel.SuspendLayout();
        this.sidebarPanel.SuspendLayout();
        this.stackGroupBox.SuspendLayout();
        this.registersGroupBox.SuspendLayout();
        this.interruptsGroupBox.SuspendLayout();
        this.interruptsTable.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).BeginInit();
        this.mainSplitContainer.Panel1.SuspendLayout();
        this.mainSplitContainer.Panel2.SuspendLayout();
        this.mainSplitContainer.SuspendLayout();
        this.codePanel.SuspendLayout();
        this.memoryGroupBox.SuspendLayout();
        this.SuspendLayout();
        // 
        // toolbarPanel
        // 
        this.toolbarPanel.AutoSize = true;
        this.toolbarPanel.Controls.Add(this.loadButton);
        this.toolbarPanel.Controls.Add(this.stepButton);
        this.toolbarPanel.Controls.Add(this.stepBackButton);
        this.toolbarPanel.Controls.Add(this.resetButton);
        this.toolbarPanel.Controls.Add(this.statusLabel);
        this.toolbarPanel.Dock = System.Windows.Forms.DockStyle.Top;
        this.toolbarPanel.Location = new System.Drawing.Point(0, 0);
        this.toolbarPanel.Name = "toolbarPanel";
        this.toolbarPanel.Padding = new System.Windows.Forms.Padding(10, 10, 10, 6);
        this.toolbarPanel.Size = new System.Drawing.Size(1000, 44);
        this.toolbarPanel.TabIndex = 0;
        // 
        // loadButton
        // 
        this.loadButton.AutoSize = true;
        this.loadButton.Location = new System.Drawing.Point(13, 13);
        this.loadButton.Name = "loadButton";
        this.loadButton.Size = new System.Drawing.Size(99, 27);
        this.loadButton.TabIndex = 0;
        this.loadButton.Text = "Load ROM...";
        this.loadButton.UseVisualStyleBackColor = true;
        this.loadButton.Click += new System.EventHandler(this.OnLoadRomClicked);
        // 
        // stepButton
        // 
        this.stepButton.AutoSize = true;
        this.stepButton.Location = new System.Drawing.Point(118, 13);
        this.stepButton.Name = "stepButton";
        this.stepButton.Size = new System.Drawing.Size(47, 27);
        this.stepButton.TabIndex = 1;
        this.stepButton.Text = "Step";
        this.stepButton.UseVisualStyleBackColor = true;
        this.stepButton.Click += new System.EventHandler(this.OnStepClicked);
        // 
        // stepBackButton
        // 
        this.stepBackButton.AutoSize = true;
        this.stepBackButton.Location = new System.Drawing.Point(171, 13);
        this.stepBackButton.Name = "stepBackButton";
        this.stepBackButton.Size = new System.Drawing.Size(89, 27);
        this.stepBackButton.TabIndex = 2;
        this.stepBackButton.Text = "Step Back";
        this.stepBackButton.UseVisualStyleBackColor = true;
        this.stepBackButton.Click += new System.EventHandler(this.OnStepBackClicked);
        // 
        // resetButton
        // 
        this.resetButton.AutoSize = true;
        this.resetButton.Location = new System.Drawing.Point(266, 13);
        this.resetButton.Name = "resetButton";
        this.resetButton.Size = new System.Drawing.Size(52, 27);
        this.resetButton.TabIndex = 3;
        this.resetButton.Text = "Reset";
        this.resetButton.UseVisualStyleBackColor = true;
        this.resetButton.Click += new System.EventHandler(this.OnResetClicked);
        // 
        // statusLabel
        // 
        this.statusLabel.AutoSize = true;
        this.statusLabel.Name = "statusLabel";
        this.statusLabel.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
        this.statusLabel.Margin = new System.Windows.Forms.Padding(18, 6, 0, 0);
        this.statusLabel.Size = new System.Drawing.Size(156, 28);
        this.statusLabel.TabIndex = 1;
        this.statusLabel.Text = "No cartridge loaded.";
        // 
        // sidebarPanel
        // 
        this.sidebarPanel.Controls.Add(this.stackGroupBox);
        this.sidebarPanel.Controls.Add(this.interruptsGroupBox);
        this.sidebarPanel.Controls.Add(this.registersGroupBox);
        this.sidebarPanel.Dock = System.Windows.Forms.DockStyle.Left;
        this.sidebarPanel.Location = new System.Drawing.Point(0, 44);
        this.sidebarPanel.Name = "sidebarPanel";
        this.sidebarPanel.Padding = new System.Windows.Forms.Padding(0, 0, 4, 0);
        this.sidebarPanel.Size = new System.Drawing.Size(280, 522);
        this.sidebarPanel.TabIndex = 3;
        // 
        // stackGroupBox
        // 
        this.stackGroupBox.Controls.Add(this.stackListBox);
        this.stackGroupBox.Controls.Add(this.stackLegendLabel);
        this.stackGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.stackGroupBox.Location = new System.Drawing.Point(0, 238);
        this.stackGroupBox.Name = "stackGroupBox";
        this.stackGroupBox.Padding = new System.Windows.Forms.Padding(12);
        this.stackGroupBox.Size = new System.Drawing.Size(276, 284);
        this.stackGroupBox.TabIndex = 2;
        this.stackGroupBox.TabStop = false;
        this.stackGroupBox.Text = "Stack";
        // 
        // stackLegendLabel
        // 
        this.stackLegendLabel.AutoSize = true;
        this.stackLegendLabel.Dock = System.Windows.Forms.DockStyle.Top;
        this.stackLegendLabel.ForeColor = System.Drawing.SystemColors.GrayText;
        this.stackLegendLabel.Location = new System.Drawing.Point(12, 27);
        this.stackLegendLabel.Name = "stackLegendLabel";
        this.stackLegendLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 6);
        this.stackLegendLabel.Size = new System.Drawing.Size(231, 46);
        this.stackLegendLabel.TabIndex = 1;
        this.stackLegendLabel.Text = "Top entry (->) is the word at SP.\r\nShows used stack; values are 16-bit little-endian.";
        // 
        // stackListBox
        // 
        this.stackListBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.stackListBox.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.stackListBox.FormattingEnabled = true;
        this.stackListBox.ItemHeight = 20;
        this.stackListBox.Location = new System.Drawing.Point(12, 73);
        this.stackListBox.Name = "stackListBox";
        this.stackListBox.Size = new System.Drawing.Size(252, 245);
        this.stackListBox.TabIndex = 0;
        // 
        // interruptsGroupBox
        // 
        this.interruptsGroupBox.AutoSize = true;
        this.interruptsGroupBox.Controls.Add(this.interruptsTable);
        this.interruptsGroupBox.Dock = System.Windows.Forms.DockStyle.Top;
        this.interruptsGroupBox.Location = new System.Drawing.Point(0, 238);
        this.interruptsGroupBox.Name = "interruptsGroupBox";
        this.interruptsGroupBox.Padding = new System.Windows.Forms.Padding(12);
        this.interruptsGroupBox.Size = new System.Drawing.Size(276, 170);
        this.interruptsGroupBox.TabIndex = 1;
        this.interruptsGroupBox.TabStop = false;
        this.interruptsGroupBox.Text = "Interrupts";
        // 
        // interruptsTable
        // 
        this.interruptsTable.AutoSize = true;
        this.interruptsTable.ColumnCount = 3;
        this.interruptsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
        this.interruptsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
        this.interruptsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
        this.interruptsTable.Controls.Add(this.interruptNameHeaderLabel, 0, 0);
        this.interruptsTable.Controls.Add(this.interruptIfHeaderLabel, 1, 0);
        this.interruptsTable.Controls.Add(this.interruptIeHeaderLabel, 2, 0);
        this.interruptsTable.Controls.Add(this.interruptVblankLabel, 0, 1);
        this.interruptsTable.Controls.Add(this.interruptIfVblankCheckBox, 1, 1);
        this.interruptsTable.Controls.Add(this.interruptIeVblankCheckBox, 2, 1);
        this.interruptsTable.Controls.Add(this.interruptLcdStatLabel, 0, 2);
        this.interruptsTable.Controls.Add(this.interruptIfLcdStatCheckBox, 1, 2);
        this.interruptsTable.Controls.Add(this.interruptIeLcdStatCheckBox, 2, 2);
        this.interruptsTable.Controls.Add(this.interruptTimerLabel, 0, 3);
        this.interruptsTable.Controls.Add(this.interruptIfTimerCheckBox, 1, 3);
        this.interruptsTable.Controls.Add(this.interruptIeTimerCheckBox, 2, 3);
        this.interruptsTable.Controls.Add(this.interruptSerialLabel, 0, 4);
        this.interruptsTable.Controls.Add(this.interruptIfSerialCheckBox, 1, 4);
        this.interruptsTable.Controls.Add(this.interruptIeSerialCheckBox, 2, 4);
        this.interruptsTable.Controls.Add(this.interruptJoypadLabel, 0, 5);
        this.interruptsTable.Controls.Add(this.interruptIfJoypadCheckBox, 1, 5);
        this.interruptsTable.Controls.Add(this.interruptIeJoypadCheckBox, 2, 5);
        this.interruptsTable.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptsTable.Location = new System.Drawing.Point(12, 27);
        this.interruptsTable.Name = "interruptsTable";
        this.interruptsTable.RowCount = 6;
        this.interruptsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
        this.interruptsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
        this.interruptsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
        this.interruptsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
        this.interruptsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
        this.interruptsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
        this.interruptsTable.Size = new System.Drawing.Size(252, 131);
        this.interruptsTable.TabIndex = 0;
        // 
        // interruptNameHeaderLabel
        // 
        this.interruptNameHeaderLabel.AutoSize = true;
        this.interruptNameHeaderLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptNameHeaderLabel.Name = "interruptNameHeaderLabel";
        this.interruptNameHeaderLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
        this.interruptNameHeaderLabel.Text = "Flag";
        this.interruptNameHeaderLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // interruptIfHeaderLabel
        // 
        this.interruptIfHeaderLabel.AutoSize = true;
        this.interruptIfHeaderLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptIfHeaderLabel.Name = "interruptIfHeaderLabel";
        this.interruptIfHeaderLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
        this.interruptIfHeaderLabel.Text = "IF";
        this.interruptIfHeaderLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        // 
        // interruptIeHeaderLabel
        // 
        this.interruptIeHeaderLabel.AutoSize = true;
        this.interruptIeHeaderLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptIeHeaderLabel.Name = "interruptIeHeaderLabel";
        this.interruptIeHeaderLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
        this.interruptIeHeaderLabel.Text = "IE";
        this.interruptIeHeaderLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        // 
        // interruptVblankLabel
        // 
        this.interruptVblankLabel.AutoSize = true;
        this.interruptVblankLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptVblankLabel.Name = "interruptVblankLabel";
        this.interruptVblankLabel.Padding = new System.Windows.Forms.Padding(0, 3, 0, 3);
        this.interruptVblankLabel.Text = "VBlank";
        this.interruptVblankLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // interruptLcdStatLabel
        // 
        this.interruptLcdStatLabel.AutoSize = true;
        this.interruptLcdStatLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptLcdStatLabel.Name = "interruptLcdStatLabel";
        this.interruptLcdStatLabel.Padding = new System.Windows.Forms.Padding(0, 3, 0, 3);
        this.interruptLcdStatLabel.Text = "LCD STAT";
        this.interruptLcdStatLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // interruptTimerLabel
        // 
        this.interruptTimerLabel.AutoSize = true;
        this.interruptTimerLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptTimerLabel.Name = "interruptTimerLabel";
        this.interruptTimerLabel.Padding = new System.Windows.Forms.Padding(0, 3, 0, 3);
        this.interruptTimerLabel.Text = "Timer";
        this.interruptTimerLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // interruptSerialLabel
        // 
        this.interruptSerialLabel.AutoSize = true;
        this.interruptSerialLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptSerialLabel.Name = "interruptSerialLabel";
        this.interruptSerialLabel.Padding = new System.Windows.Forms.Padding(0, 3, 0, 3);
        this.interruptSerialLabel.Text = "Serial";
        this.interruptSerialLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // interruptJoypadLabel
        // 
        this.interruptJoypadLabel.AutoSize = true;
        this.interruptJoypadLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.interruptJoypadLabel.Name = "interruptJoypadLabel";
        this.interruptJoypadLabel.Padding = new System.Windows.Forms.Padding(0, 3, 0, 3);
        this.interruptJoypadLabel.Text = "Joypad";
        this.interruptJoypadLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // interruptIfVblankCheckBox
        // 
        this.interruptIfVblankCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIfVblankCheckBox.AutoCheck = false;
        this.interruptIfVblankCheckBox.AutoSize = true;
        this.interruptIfVblankCheckBox.Name = "interruptIfVblankCheckBox";
        this.interruptIfVblankCheckBox.TabStop = false;
        this.interruptIfVblankCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIeVblankCheckBox
        // 
        this.interruptIeVblankCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIeVblankCheckBox.AutoCheck = false;
        this.interruptIeVblankCheckBox.AutoSize = true;
        this.interruptIeVblankCheckBox.Name = "interruptIeVblankCheckBox";
        this.interruptIeVblankCheckBox.TabStop = false;
        this.interruptIeVblankCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIfLcdStatCheckBox
        // 
        this.interruptIfLcdStatCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIfLcdStatCheckBox.AutoCheck = false;
        this.interruptIfLcdStatCheckBox.AutoSize = true;
        this.interruptIfLcdStatCheckBox.Name = "interruptIfLcdStatCheckBox";
        this.interruptIfLcdStatCheckBox.TabStop = false;
        this.interruptIfLcdStatCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIeLcdStatCheckBox
        // 
        this.interruptIeLcdStatCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIeLcdStatCheckBox.AutoCheck = false;
        this.interruptIeLcdStatCheckBox.AutoSize = true;
        this.interruptIeLcdStatCheckBox.Name = "interruptIeLcdStatCheckBox";
        this.interruptIeLcdStatCheckBox.TabStop = false;
        this.interruptIeLcdStatCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIfTimerCheckBox
        // 
        this.interruptIfTimerCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIfTimerCheckBox.AutoCheck = false;
        this.interruptIfTimerCheckBox.AutoSize = true;
        this.interruptIfTimerCheckBox.Name = "interruptIfTimerCheckBox";
        this.interruptIfTimerCheckBox.TabStop = false;
        this.interruptIfTimerCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIeTimerCheckBox
        // 
        this.interruptIeTimerCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIeTimerCheckBox.AutoCheck = false;
        this.interruptIeTimerCheckBox.AutoSize = true;
        this.interruptIeTimerCheckBox.Name = "interruptIeTimerCheckBox";
        this.interruptIeTimerCheckBox.TabStop = false;
        this.interruptIeTimerCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIfSerialCheckBox
        // 
        this.interruptIfSerialCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIfSerialCheckBox.AutoCheck = false;
        this.interruptIfSerialCheckBox.AutoSize = true;
        this.interruptIfSerialCheckBox.Name = "interruptIfSerialCheckBox";
        this.interruptIfSerialCheckBox.TabStop = false;
        this.interruptIfSerialCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIeSerialCheckBox
        // 
        this.interruptIeSerialCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIeSerialCheckBox.AutoCheck = false;
        this.interruptIeSerialCheckBox.AutoSize = true;
        this.interruptIeSerialCheckBox.Name = "interruptIeSerialCheckBox";
        this.interruptIeSerialCheckBox.TabStop = false;
        this.interruptIeSerialCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIfJoypadCheckBox
        // 
        this.interruptIfJoypadCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIfJoypadCheckBox.AutoCheck = false;
        this.interruptIfJoypadCheckBox.AutoSize = true;
        this.interruptIfJoypadCheckBox.Name = "interruptIfJoypadCheckBox";
        this.interruptIfJoypadCheckBox.TabStop = false;
        this.interruptIfJoypadCheckBox.UseVisualStyleBackColor = true;
        // 
        // interruptIeJoypadCheckBox
        // 
        this.interruptIeJoypadCheckBox.Anchor = System.Windows.Forms.AnchorStyles.None;
        this.interruptIeJoypadCheckBox.AutoCheck = false;
        this.interruptIeJoypadCheckBox.AutoSize = true;
        this.interruptIeJoypadCheckBox.Name = "interruptIeJoypadCheckBox";
        this.interruptIeJoypadCheckBox.TabStop = false;
        this.interruptIeJoypadCheckBox.UseVisualStyleBackColor = true;
        // 
        // registersGroupBox
        // 
        this.registersGroupBox.AutoSize = true;
        this.registersGroupBox.Controls.Add(this.registerTable);
        this.registersGroupBox.Dock = System.Windows.Forms.DockStyle.Top;
        this.registersGroupBox.Location = new System.Drawing.Point(0, 0);
        this.registersGroupBox.Name = "registersGroupBox";
        this.registersGroupBox.Padding = new System.Windows.Forms.Padding(12);
        this.registersGroupBox.Size = new System.Drawing.Size(276, 238);
        this.registersGroupBox.TabIndex = 0;
        this.registersGroupBox.TabStop = false;
        this.registersGroupBox.Text = "Registers";
        // 
        // registerTable
        // 
        this.registerTable.AutoSize = true;
        this.registerTable.ColumnCount = 2;
        this.registerTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
        this.registerTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
        this.registerTable.Dock = System.Windows.Forms.DockStyle.Fill;
        this.registerTable.Location = new System.Drawing.Point(12, 27);
        this.registerTable.Name = "registerTable";
        this.registerTable.RowCount = 1;
        this.registerTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
        this.registerTable.Size = new System.Drawing.Size(252, 199);
        this.registerTable.TabIndex = 0;
        // 
        // mainSplitContainer
        // 
        this.mainSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
        this.mainSplitContainer.Location = new System.Drawing.Point(0, 0);
        this.mainSplitContainer.Name = "mainSplitContainer";
        this.mainSplitContainer.Orientation = System.Windows.Forms.Orientation.Vertical;
        // 
        // mainSplitContainer.Panel1
        // 
        this.mainSplitContainer.Panel1.Controls.Add(this.codePanel);
        // 
        // mainSplitContainer.Panel2
        // 
        this.mainSplitContainer.Panel2.Controls.Add(this.memoryGroupBox);
        this.mainSplitContainer.Size = new System.Drawing.Size(720, 522);
        this.mainSplitContainer.SplitterDistance = 420;
        this.mainSplitContainer.TabIndex = 4;
        // 
        // codePanel
        // 
        this.codePanel.Controls.Add(this.codeListBox);
        this.codePanel.Controls.Add(this.codeLegendLabel);
        this.codePanel.Dock = System.Windows.Forms.DockStyle.Fill;
        this.codePanel.Location = new System.Drawing.Point(0, 0);
        this.codePanel.Name = "codePanel";
        this.codePanel.Size = new System.Drawing.Size(420, 522);
        this.codePanel.TabIndex = 1;
        // 
        // codeListBox
        // 
        this.codeListBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.codeListBox.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.codeListBox.FormattingEnabled = true;
        this.codeListBox.HorizontalScrollbar = true;
        this.codeListBox.IntegralHeight = false;
        this.codeListBox.ItemHeight = 20;
        this.codeListBox.Location = new System.Drawing.Point(0, 38);
        this.codeListBox.Name = "codeListBox";
        this.codeListBox.ScrollAlwaysVisible = true;
        this.codeListBox.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
        this.codeListBox.Size = new System.Drawing.Size(720, 260);
        this.codeListBox.TabIndex = 0;
        // 
        // codeLegendLabel
        // 
        this.codeLegendLabel.Dock = System.Windows.Forms.DockStyle.Top;
        this.codeLegendLabel.ForeColor = System.Drawing.SystemColors.GrayText;
        this.codeLegendLabel.Location = new System.Drawing.Point(0, 0);
        this.codeLegendLabel.Name = "codeLegendLabel";
        this.codeLegendLabel.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
        this.codeLegendLabel.Size = new System.Drawing.Size(720, 38);
        this.codeLegendLabel.TabIndex = 1;
        this.codeLegendLabel.Text = "ROM disassembly; \">\" marks the instruction at PC.";
        // 
        // memoryGroupBox
        // 
        this.memoryGroupBox.Controls.Add(this.memoryListBox);
        this.memoryGroupBox.Controls.Add(this.memoryLegendLabel);
        this.memoryGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.memoryGroupBox.Location = new System.Drawing.Point(0, 0);
        this.memoryGroupBox.Name = "memoryGroupBox";
        this.memoryGroupBox.Padding = new System.Windows.Forms.Padding(12);
        this.memoryGroupBox.Size = new System.Drawing.Size(720, 258);
        this.memoryGroupBox.TabIndex = 0;
        this.memoryGroupBox.TabStop = false;
        this.memoryGroupBox.Text = "Memory";
        // 
        // memoryLegendLabel
        // 
        this.memoryLegendLabel.AutoSize = true;
        this.memoryLegendLabel.Dock = System.Windows.Forms.DockStyle.Top;
        this.memoryLegendLabel.ForeColor = System.Drawing.SystemColors.GrayText;
        this.memoryLegendLabel.Location = new System.Drawing.Point(12, 27);
        this.memoryLegendLabel.Name = "memoryLegendLabel";
        this.memoryLegendLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 6);
        this.memoryLegendLabel.Size = new System.Drawing.Size(308, 46);
        this.memoryLegendLabel.TabIndex = 1;
        this.memoryLegendLabel.Text = "Legend: \">\" row contains the current PC; \"*\" means\r\nthe row had a write and \"r\" m" +
"eans a read in the last step. Addresses/bytes are hex.";
        // 
        // memoryListBox
        // 
        this.memoryListBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.memoryListBox.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.memoryListBox.FormattingEnabled = true;
        this.memoryListBox.ItemHeight = 20;
        this.memoryListBox.Location = new System.Drawing.Point(12, 73);
        this.memoryListBox.Name = "memoryListBox";
        this.memoryListBox.Size = new System.Drawing.Size(696, 219);
        this.memoryListBox.TabIndex = 0;
        // 
        // openRomDialog
        // 
        this.openRomDialog.Filter = "Game Boy ROM (*.gb;*.gbc)|*.gb;*.gbc|All files (*.*)|*.*";
        this.openRomDialog.Title = "Select Game Boy ROM";
        // 
        // DebuggerForm
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1000, 600);
        this.Controls.Add(this.mainSplitContainer);
        this.Controls.Add(this.sidebarPanel);
        this.Controls.Add(this.toolbarPanel);
        this.MinimumSize = new System.Drawing.Size(840, 540);
        this.Name = "DebuggerForm";
        this.KeyPreview = true;
        this.Text = "SharpBoy Debugger";
        this.toolbarPanel.ResumeLayout(false);
        this.toolbarPanel.PerformLayout();
        this.sidebarPanel.ResumeLayout(false);
        this.sidebarPanel.PerformLayout();
        this.stackGroupBox.ResumeLayout(false);
        this.interruptsTable.ResumeLayout(false);
        this.interruptsTable.PerformLayout();
        this.interruptsGroupBox.ResumeLayout(false);
        this.interruptsGroupBox.PerformLayout();
        this.registersGroupBox.ResumeLayout(false);
        this.registersGroupBox.PerformLayout();
        this.mainSplitContainer.Panel1.ResumeLayout(false);
        this.mainSplitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).EndInit();
        this.mainSplitContainer.ResumeLayout(false);
        this.codePanel.ResumeLayout(false);
        this.memoryGroupBox.ResumeLayout(false);
        this.ResumeLayout(false);
        this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.FlowLayoutPanel toolbarPanel;
    private System.Windows.Forms.Button loadButton;
    private System.Windows.Forms.Button stepButton;
    private System.Windows.Forms.Button stepBackButton;
    private System.Windows.Forms.Button resetButton;
    private System.Windows.Forms.Label statusLabel;
    private System.Windows.Forms.Panel sidebarPanel;
    private System.Windows.Forms.GroupBox registersGroupBox;
    private System.Windows.Forms.TableLayoutPanel registerTable;
    private System.Windows.Forms.GroupBox interruptsGroupBox;
    private System.Windows.Forms.TableLayoutPanel interruptsTable;
    private System.Windows.Forms.Label interruptNameHeaderLabel;
    private System.Windows.Forms.Label interruptIfHeaderLabel;
    private System.Windows.Forms.Label interruptIeHeaderLabel;
    private System.Windows.Forms.Label interruptVblankLabel;
    private System.Windows.Forms.Label interruptLcdStatLabel;
    private System.Windows.Forms.Label interruptTimerLabel;
    private System.Windows.Forms.Label interruptSerialLabel;
    private System.Windows.Forms.Label interruptJoypadLabel;
    private System.Windows.Forms.CheckBox interruptIfVblankCheckBox;
    private System.Windows.Forms.CheckBox interruptIeVblankCheckBox;
    private System.Windows.Forms.CheckBox interruptIfLcdStatCheckBox;
    private System.Windows.Forms.CheckBox interruptIeLcdStatCheckBox;
    private System.Windows.Forms.CheckBox interruptIfTimerCheckBox;
    private System.Windows.Forms.CheckBox interruptIeTimerCheckBox;
    private System.Windows.Forms.CheckBox interruptIfSerialCheckBox;
    private System.Windows.Forms.CheckBox interruptIeSerialCheckBox;
    private System.Windows.Forms.CheckBox interruptIfJoypadCheckBox;
    private System.Windows.Forms.CheckBox interruptIeJoypadCheckBox;
    private System.Windows.Forms.GroupBox stackGroupBox;
    private System.Windows.Forms.Label stackLegendLabel;
    private System.Windows.Forms.ListBox stackListBox;
    private System.Windows.Forms.SplitContainer mainSplitContainer;
    private System.Windows.Forms.Panel codePanel;
    private System.Windows.Forms.ListBox codeListBox;
    private System.Windows.Forms.Label codeLegendLabel;
    private System.Windows.Forms.GroupBox memoryGroupBox;
    private System.Windows.Forms.Label memoryLegendLabel;
    private System.Windows.Forms.ListBox memoryListBox;
    private System.Windows.Forms.OpenFileDialog openRomDialog;
}
