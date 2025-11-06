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
        this.resetButton = new System.Windows.Forms.Button();
        this.statusLabel = new System.Windows.Forms.Label();
        this.sidebarPanel = new System.Windows.Forms.Panel();
        this.stackGroupBox = new System.Windows.Forms.GroupBox();
        this.stackListBox = new System.Windows.Forms.ListBox();
        this.registersGroupBox = new System.Windows.Forms.GroupBox();
        this.registerTable = new System.Windows.Forms.TableLayoutPanel();
        this.mainSplitContainer = new System.Windows.Forms.SplitContainer();
        this.codeListBox = new System.Windows.Forms.ListBox();
        this.memoryGroupBox = new System.Windows.Forms.GroupBox();
        this.memoryListBox = new System.Windows.Forms.ListBox();
        this.openRomDialog = new System.Windows.Forms.OpenFileDialog();
        this.toolbarPanel.SuspendLayout();
        this.sidebarPanel.SuspendLayout();
        this.stackGroupBox.SuspendLayout();
        this.registersGroupBox.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).BeginInit();
        this.mainSplitContainer.Panel1.SuspendLayout();
        this.mainSplitContainer.Panel2.SuspendLayout();
        this.mainSplitContainer.SuspendLayout();
        this.memoryGroupBox.SuspendLayout();
        this.SuspendLayout();
        // 
        // toolbarPanel
        // 
        this.toolbarPanel.AutoSize = true;
        this.toolbarPanel.Controls.Add(this.loadButton);
        this.toolbarPanel.Controls.Add(this.stepButton);
        this.toolbarPanel.Controls.Add(this.resetButton);
        this.toolbarPanel.Dock = System.Windows.Forms.DockStyle.Top;
        this.toolbarPanel.Location = new System.Drawing.Point(0, 0);
        this.toolbarPanel.Name = "toolbarPanel";
        this.toolbarPanel.Padding = new System.Windows.Forms.Padding(10);
        this.toolbarPanel.Size = new System.Drawing.Size(1000, 50);
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
        // resetButton
        // 
        this.resetButton.AutoSize = true;
        this.resetButton.Location = new System.Drawing.Point(171, 13);
        this.resetButton.Name = "resetButton";
        this.resetButton.Size = new System.Drawing.Size(52, 27);
        this.resetButton.TabIndex = 2;
        this.resetButton.Text = "Reset";
        this.resetButton.UseVisualStyleBackColor = true;
        this.resetButton.Click += new System.EventHandler(this.OnResetClicked);
        // 
        // statusLabel
        // 
        this.statusLabel.Dock = System.Windows.Forms.DockStyle.Top;
        this.statusLabel.Location = new System.Drawing.Point(0, 50);
        this.statusLabel.Name = "statusLabel";
        this.statusLabel.Padding = new System.Windows.Forms.Padding(12, 0, 12, 8);
        this.statusLabel.Size = new System.Drawing.Size(1000, 28);
        this.statusLabel.TabIndex = 1;
        this.statusLabel.Text = "No cartridge loaded.";
        // 
        // sidebarPanel
        // 
        this.sidebarPanel.Controls.Add(this.stackGroupBox);
        this.sidebarPanel.Controls.Add(this.registersGroupBox);
        this.sidebarPanel.Dock = System.Windows.Forms.DockStyle.Left;
        this.sidebarPanel.Location = new System.Drawing.Point(0, 78);
        this.sidebarPanel.Name = "sidebarPanel";
        this.sidebarPanel.Padding = new System.Windows.Forms.Padding(0, 0, 4, 0);
        this.sidebarPanel.Size = new System.Drawing.Size(280, 522);
        this.sidebarPanel.TabIndex = 3;
        // 
        // stackGroupBox
        // 
        this.stackGroupBox.Controls.Add(this.stackListBox);
        this.stackGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.stackGroupBox.Location = new System.Drawing.Point(0, 238);
        this.stackGroupBox.Name = "stackGroupBox";
        this.stackGroupBox.Padding = new System.Windows.Forms.Padding(12);
        this.stackGroupBox.Size = new System.Drawing.Size(276, 284);
        this.stackGroupBox.TabIndex = 1;
        this.stackGroupBox.TabStop = false;
        this.stackGroupBox.Text = "Stack";
        // 
        // stackListBox
        // 
        this.stackListBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.stackListBox.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.stackListBox.FormattingEnabled = true;
        this.stackListBox.ItemHeight = 20;
        this.stackListBox.Location = new System.Drawing.Point(12, 27);
        this.stackListBox.Name = "stackListBox";
        this.stackListBox.Size = new System.Drawing.Size(252, 245);
        this.stackListBox.TabIndex = 0;
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
        this.mainSplitContainer.Location = new System.Drawing.Point(280, 78);
        this.mainSplitContainer.Name = "mainSplitContainer";
        this.mainSplitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
        // 
        // mainSplitContainer.Panel1
        // 
        this.mainSplitContainer.Panel1.Controls.Add(this.codeListBox);
        // 
        // mainSplitContainer.Panel2
        // 
        this.mainSplitContainer.Panel2.Controls.Add(this.memoryGroupBox);
        this.mainSplitContainer.Size = new System.Drawing.Size(720, 522);
        this.mainSplitContainer.SplitterDistance = 260;
        this.mainSplitContainer.TabIndex = 4;
        // 
        // codeListBox
        // 
        this.codeListBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.codeListBox.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.codeListBox.FormattingEnabled = true;
        this.codeListBox.ItemHeight = 20;
        this.codeListBox.Location = new System.Drawing.Point(0, 0);
        this.codeListBox.Name = "codeListBox";
        this.codeListBox.Size = new System.Drawing.Size(720, 260);
        this.codeListBox.TabIndex = 0;
        // 
        // memoryGroupBox
        // 
        this.memoryGroupBox.Controls.Add(this.memoryListBox);
        this.memoryGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.memoryGroupBox.Location = new System.Drawing.Point(0, 0);
        this.memoryGroupBox.Name = "memoryGroupBox";
        this.memoryGroupBox.Padding = new System.Windows.Forms.Padding(12);
        this.memoryGroupBox.Size = new System.Drawing.Size(720, 258);
        this.memoryGroupBox.TabIndex = 0;
        this.memoryGroupBox.TabStop = false;
        this.memoryGroupBox.Text = "Memory";
        // 
        // memoryListBox
        // 
        this.memoryListBox.Dock = System.Windows.Forms.DockStyle.Fill;
        this.memoryListBox.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.memoryListBox.FormattingEnabled = true;
        this.memoryListBox.ItemHeight = 20;
        this.memoryListBox.Location = new System.Drawing.Point(12, 27);
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
        this.Controls.Add(this.statusLabel);
        this.Controls.Add(this.toolbarPanel);
        this.MinimumSize = new System.Drawing.Size(840, 540);
        this.Name = "DebuggerForm";
        this.Text = "SharpBoy Debugger";
        this.toolbarPanel.ResumeLayout(false);
        this.toolbarPanel.PerformLayout();
        this.sidebarPanel.ResumeLayout(false);
        this.sidebarPanel.PerformLayout();
        this.stackGroupBox.ResumeLayout(false);
        this.registersGroupBox.ResumeLayout(false);
        this.registersGroupBox.PerformLayout();
        this.mainSplitContainer.Panel1.ResumeLayout(false);
        this.mainSplitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).EndInit();
        this.mainSplitContainer.ResumeLayout(false);
        this.memoryGroupBox.ResumeLayout(false);
        this.ResumeLayout(false);
        this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.FlowLayoutPanel toolbarPanel;
    private System.Windows.Forms.Button loadButton;
    private System.Windows.Forms.Button stepButton;
    private System.Windows.Forms.Button resetButton;
    private System.Windows.Forms.Label statusLabel;
    private System.Windows.Forms.Panel sidebarPanel;
    private System.Windows.Forms.GroupBox registersGroupBox;
    private System.Windows.Forms.TableLayoutPanel registerTable;
    private System.Windows.Forms.GroupBox stackGroupBox;
    private System.Windows.Forms.ListBox stackListBox;
    private System.Windows.Forms.SplitContainer mainSplitContainer;
    private System.Windows.Forms.ListBox codeListBox;
    private System.Windows.Forms.GroupBox memoryGroupBox;
    private System.Windows.Forms.ListBox memoryListBox;
    private System.Windows.Forms.OpenFileDialog openRomDialog;
}
