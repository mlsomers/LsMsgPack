namespace MsgPackExplorer {
  partial class Explorer {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing) {
      if(disposing && (components != null)) {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
      System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Explorer));
      this.toolStrip1 = new System.Windows.Forms.ToolStrip();
      this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
      this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripDropDownButton3 = new System.Windows.Forms.ToolStripDropDownButton();
      this.btnOpen = new System.Windows.Forms.ToolStripMenuItem();
      this.fromClipboardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripDropDownButton2 = new System.Windows.Forms.ToolStripDropDownButton();
      this.btnGenerateTestFiles = new System.Windows.Forms.ToolStripMenuItem();
      this.btnProcessAfterError = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
      this.installAsFiddlerInspectorToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.menUnistallFiddler = new System.Windows.Forms.ToolStripMenuItem();
      this.installAsVsPluginToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.menUninstallVisualStudio = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
      this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
      this.ddLimitItems = new System.Windows.Forms.ToolStripComboBox();
      this.toolStripLabel2 = new System.Windows.Forms.ToolStripLabel();
      this.ddEndianess = new System.Windows.Forms.ToolStripComboBox();
      this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
      this.saveTestSuiteDialog = new System.Windows.Forms.SaveFileDialog();
      this.msgPackExplorer1 = new MsgPackExplorer.LsMsgPackExplorer();
      this.toolStrip1.SuspendLayout();
      this.SuspendLayout();
      // 
      // toolStrip1
      // 
      this.toolStrip1.GripMargin = new System.Windows.Forms.Padding(0);
      this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
      this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripDropDownButton1,
            this.toolStripDropDownButton3,
            this.toolStripDropDownButton2,
            this.toolStripSeparator1,
            this.toolStripLabel1,
            this.ddLimitItems,
            this.toolStripLabel2,
            this.ddEndianess});
      this.toolStrip1.Location = new System.Drawing.Point(0, 0);
      this.toolStrip1.Name = "toolStrip1";
      this.toolStrip1.Padding = new System.Windows.Forms.Padding(0);
      this.toolStrip1.Size = new System.Drawing.Size(929, 25);
      this.toolStrip1.TabIndex = 1;
      this.toolStrip1.Text = "toolStrip1";
      // 
      // toolStripDropDownButton1
      // 
      this.toolStripDropDownButton1.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
      this.toolStripDropDownButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
      this.toolStripDropDownButton1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
      this.toolStripDropDownButton1.Image = global::MsgPackExplorer.Properties.Resources.Help;
      this.toolStripDropDownButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolStripDropDownButton1.Name = "toolStripDropDownButton1";
      this.toolStripDropDownButton1.Size = new System.Drawing.Size(29, 22);
      this.toolStripDropDownButton1.Text = "toolStripDropDownButton1";
      // 
      // aboutToolStripMenuItem
      // 
      this.aboutToolStripMenuItem.Image = global::MsgPackExplorer.Properties.Resources.Info;
      this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
      this.aboutToolStripMenuItem.Size = new System.Drawing.Size(107, 22);
      this.aboutToolStripMenuItem.Text = "About";
      this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
      // 
      // toolStripDropDownButton3
      // 
      this.toolStripDropDownButton3.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.btnOpen,
            this.fromClipboardToolStripMenuItem});
      this.toolStripDropDownButton3.Image = global::MsgPackExplorer.Properties.Resources.Open;
      this.toolStripDropDownButton3.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolStripDropDownButton3.Name = "toolStripDropDownButton3";
      this.toolStripDropDownButton3.Size = new System.Drawing.Size(54, 22);
      this.toolStripDropDownButton3.Text = "File";
      // 
      // btnOpen
      // 
      this.btnOpen.Image = global::MsgPackExplorer.Properties.Resources.Open;
      this.btnOpen.Name = "btnOpen";
      this.btnOpen.Size = new System.Drawing.Size(157, 22);
      this.btnOpen.Text = "Open...";
      this.btnOpen.Click += new System.EventHandler(this.btnOpen_Click);
      // 
      // fromClipboardToolStripMenuItem
      // 
      this.fromClipboardToolStripMenuItem.Image = global::MsgPackExplorer.Properties.Resources.Clipboard;
      this.fromClipboardToolStripMenuItem.Name = "fromClipboardToolStripMenuItem";
      this.fromClipboardToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
      this.fromClipboardToolStripMenuItem.Text = "From Clipboard";
      this.fromClipboardToolStripMenuItem.Click += new System.EventHandler(this.fromClipboardToolStripMenuItem_Click);
      // 
      // toolStripDropDownButton2
      // 
      this.toolStripDropDownButton2.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.btnGenerateTestFiles,
            this.btnProcessAfterError,
            this.toolStripSeparator2,
            this.installAsFiddlerInspectorToolStripMenuItem,
            this.installAsVsPluginToolStripMenuItem});
      this.toolStripDropDownButton2.Image = global::MsgPackExplorer.Properties.Resources.Tools;
      this.toolStripDropDownButton2.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolStripDropDownButton2.Name = "toolStripDropDownButton2";
      this.toolStripDropDownButton2.Size = new System.Drawing.Size(63, 22);
      this.toolStripDropDownButton2.Text = "Tools";
      // 
      // btnGenerateTestFiles
      // 
      this.btnGenerateTestFiles.Image = global::MsgPackExplorer.Properties.Resources.Gears;
      this.btnGenerateTestFiles.Name = "btnGenerateTestFiles";
      this.btnGenerateTestFiles.Size = new System.Drawing.Size(227, 22);
      this.btnGenerateTestFiles.Text = "Generate test files";
      this.btnGenerateTestFiles.Click += new System.EventHandler(this.btnGenerateTestFiles_Click);
      // 
      // btnProcessAfterError
      // 
      this.btnProcessAfterError.CheckOnClick = true;
      this.btnProcessAfterError.Image = global::MsgPackExplorer.Properties.Resources.Broken;
      this.btnProcessAfterError.Name = "btnProcessAfterError";
      this.btnProcessAfterError.Size = new System.Drawing.Size(227, 22);
      this.btnProcessAfterError.Text = "Keep processing after errors";
      this.btnProcessAfterError.CheckedChanged += new System.EventHandler(this.btnProcessAfterError_CheckedChanged);
      // 
      // toolStripSeparator2
      // 
      this.toolStripSeparator2.Name = "toolStripSeparator2";
      this.toolStripSeparator2.Size = new System.Drawing.Size(224, 6);
      // 
      // installAsFiddlerInspectorToolStripMenuItem
      // 
      this.installAsFiddlerInspectorToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menUnistallFiddler});
      this.installAsFiddlerInspectorToolStripMenuItem.Name = "installAsFiddlerInspectorToolStripMenuItem";
      this.installAsFiddlerInspectorToolStripMenuItem.Size = new System.Drawing.Size(227, 22);
      this.installAsFiddlerInspectorToolStripMenuItem.Text = "Install as Fiddler Inspector";
      this.installAsFiddlerInspectorToolStripMenuItem.Click += new System.EventHandler(this.installAsFiddlerInspectorToolStripMenuItem_Click);
      // 
      // menUnistallFiddler
      // 
      this.menUnistallFiddler.Name = "menUnistallFiddler";
      this.menUnistallFiddler.Size = new System.Drawing.Size(180, 22);
      this.menUnistallFiddler.Text = "Uninstall";
      this.menUnistallFiddler.Click += new System.EventHandler(this.menUnistallFiddler_Click);
      // 
      // installAsVsPluginToolStripMenuItem
      // 
      this.installAsVsPluginToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menUninstallVisualStudio});
      this.installAsVsPluginToolStripMenuItem.Name = "installAsVsPluginToolStripMenuItem";
      this.installAsVsPluginToolStripMenuItem.Size = new System.Drawing.Size(227, 22);
      this.installAsVsPluginToolStripMenuItem.Text = "Install as Visual Studio Plugin";
      this.installAsVsPluginToolStripMenuItem.Click += new System.EventHandler(this.installVisualStudioPluginToolStripMenuItem_Click);
      // 
      // menUninstallVisualStudio
      // 
      this.menUninstallVisualStudio.Name = "menUninstallVisualStudio";
      this.menUninstallVisualStudio.Size = new System.Drawing.Size(180, 22);
      this.menUninstallVisualStudio.Text = "Uninstall";
      this.menUninstallVisualStudio.Click += new System.EventHandler(this.menUninstallVisualStudio_Click);
      // 
      // toolStripSeparator1
      // 
      this.toolStripSeparator1.Name = "toolStripSeparator1";
      this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
      // 
      // toolStripLabel1
      // 
      this.toolStripLabel1.Name = "toolStripLabel1";
      this.toolStripLabel1.Size = new System.Drawing.Size(100, 22);
      this.toolStripLabel1.Text = "Limit items in list:";
      // 
      // ddLimitItems
      // 
      this.ddLimitItems.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
      this.ddLimitItems.FlatStyle = System.Windows.Forms.FlatStyle.System;
      this.ddLimitItems.Items.AddRange(new object[] {
            "500",
            "1000",
            "10000",
            "100000",
            "All (no limit)"});
      this.ddLimitItems.Name = "ddLimitItems";
      this.ddLimitItems.Size = new System.Drawing.Size(121, 25);
      this.ddLimitItems.ToolTipText = "More items take longer to process and it may seem like the application freezes fo" +
    "r a while";
      this.ddLimitItems.DropDownClosed += new System.EventHandler(this.ddLimitItems_TextChanged);
      this.ddLimitItems.TextChanged += new System.EventHandler(this.ddLimitItems_TextChanged);
      // 
      // toolStripLabel2
      // 
      this.toolStripLabel2.Name = "toolStripLabel2";
      this.toolStripLabel2.Size = new System.Drawing.Size(69, 22);
      this.toolStripLabel2.Text = "Endianness:";
      this.toolStripLabel2.ToolTipText = "Override specification (for debugging purposes)";
      // 
      // ddEndianess
      // 
      this.ddEndianess.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
      this.ddEndianess.FlatStyle = System.Windows.Forms.FlatStyle.System;
      this.ddEndianess.Name = "ddEndianess";
      this.ddEndianess.Size = new System.Drawing.Size(240, 25);
      this.ddEndianess.ToolTipText = resources.GetString("ddEndianess.ToolTipText");
      this.ddEndianess.DropDownClosed += new System.EventHandler(this.ddEndianess_DropDownClosed);
      this.ddEndianess.TextChanged += new System.EventHandler(this.ddEndianess_DropDownClosed);
      // 
      // openFileDialog1
      // 
      this.openFileDialog1.DefaultExt = "MsgPack";
      this.openFileDialog1.Filter = "All (*.*)|*.*|Bin (*.bin)|*.bin|MsgPack (*.MsgPack)|*.MsgPack";
      this.openFileDialog1.Title = "Open a raw MsgPack file";
      // 
      // saveTestSuiteDialog
      // 
      this.saveTestSuiteDialog.DefaultExt = "MsgPack";
      this.saveTestSuiteDialog.FileName = "FileName will be ignored";
      this.saveTestSuiteDialog.Title = "Save Test Suite files";
      // 
      // msgPackExplorer1
      // 
      this.msgPackExplorer1.ContinueOnError = false;
      this.msgPackExplorer1.Cursor = System.Windows.Forms.Cursors.Default;
      this.msgPackExplorer1.Data = null;
      this.msgPackExplorer1.DisplayLimit = ((long)(1000));
      this.msgPackExplorer1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.msgPackExplorer1.EndianHandling = LsMsgPack.EndianAction.SwapIfCurrentSystemIsLittleEndian;
      this.msgPackExplorer1.Item = null;
      this.msgPackExplorer1.Location = new System.Drawing.Point(0, 25);
      this.msgPackExplorer1.Name = "msgPackExplorer1";
      this.msgPackExplorer1.Size = new System.Drawing.Size(929, 470);
      this.msgPackExplorer1.TabIndex = 0;
      // 
      // Explorer
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(929, 495);
      this.Controls.Add(this.msgPackExplorer1);
      this.Controls.Add(this.toolStrip1);
      this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
      this.Name = "Explorer";
      this.Text = "MsgPack Explorer";
      this.toolStrip1.ResumeLayout(false);
      this.toolStrip1.PerformLayout();
      this.ResumeLayout(false);
      this.PerformLayout();

    }

    #endregion

    private LsMsgPackExplorer msgPackExplorer1;
    private System.Windows.Forms.ToolStrip toolStrip1;
    private System.Windows.Forms.OpenFileDialog openFileDialog1;
    private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton1;
    private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
    private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton2;
    private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton3;
    private System.Windows.Forms.ToolStripMenuItem btnOpen;
    private System.Windows.Forms.ToolStripMenuItem btnGenerateTestFiles;
    private System.Windows.Forms.SaveFileDialog saveTestSuiteDialog;
    private System.Windows.Forms.ToolStripMenuItem btnProcessAfterError;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
    private System.Windows.Forms.ToolStripLabel toolStripLabel1;
    private System.Windows.Forms.ToolStripComboBox ddLimitItems;
    private System.Windows.Forms.ToolStripLabel toolStripLabel2;
    private System.Windows.Forms.ToolStripComboBox ddEndianess;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
    private System.Windows.Forms.ToolStripMenuItem installAsFiddlerInspectorToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem installAsVsPluginToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem fromClipboardToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem menUnistallFiddler;
    private System.Windows.Forms.ToolStripMenuItem menUninstallVisualStudio;
  }
}

