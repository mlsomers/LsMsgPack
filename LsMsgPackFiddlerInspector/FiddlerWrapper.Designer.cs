namespace LsMsgPackFiddlerInspector {
  partial class FiddlerWrapper {
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

    #region Component Designer generated code

    /// <summary> 
    /// Required method for Designer support - do not modify 
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
      System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FiddlerWrapper));
      this.toolStrip1 = new System.Windows.Forms.ToolStrip();
      this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
      this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
      this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
      this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
      this.ddLimitItems = new System.Windows.Forms.ToolStripComboBox();
      this.toolStripLabel2 = new System.Windows.Forms.ToolStripLabel();
      this.ddEndianess = new System.Windows.Forms.ToolStripComboBox();
      this.btnObjects = new System.Windows.Forms.ToolStripButton();
      this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
      this.searchMatchCase = new System.Windows.Forms.ToolStripButton();
      this.searchTextBox = new System.Windows.Forms.ToolStripTextBox();
      this.searchPrev = new System.Windows.Forms.ToolStripButton();
      this.searchNext = new System.Windows.Forms.ToolStripButton();
      this.searchPosCount = new System.Windows.Forms.ToolStripLabel();
      this.lsMsgPackExplorer1 = new MsgPackExplorer.LsMsgPackExplorer();
      this.toolStrip1.SuspendLayout();
      this.SuspendLayout();
      // 
      // toolStrip1
      // 
      this.toolStrip1.GripMargin = new System.Windows.Forms.Padding(0);
      this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
      this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripDropDownButton1,
            this.toolStripButton1,
            this.btnObjects,
            this.toolStripSeparator1,
            this.toolStripLabel1,
            this.ddLimitItems,
            this.toolStripLabel2,
            this.ddEndianess,
            this.toolStripSeparator2,
            this.searchMatchCase,
            this.searchTextBox,
            this.searchPrev,
            this.searchNext,
            this.searchPosCount});
      this.toolStrip1.Location = new System.Drawing.Point(0, 0);
      this.toolStrip1.Name = "toolStrip1";
      this.toolStrip1.Padding = new System.Windows.Forms.Padding(0);
      this.toolStrip1.Size = new System.Drawing.Size(769, 25);
      this.toolStrip1.TabIndex = 0;
      this.toolStrip1.Text = "toolStrip1";
      // 
      // toolStripDropDownButton1
      // 
      this.toolStripDropDownButton1.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
      this.toolStripDropDownButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
      this.toolStripDropDownButton1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
      this.toolStripDropDownButton1.Image = global::LsMsgPackFiddlerInspector.Properties.Resources.Help;
      this.toolStripDropDownButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolStripDropDownButton1.Name = "toolStripDropDownButton1";
      this.toolStripDropDownButton1.Size = new System.Drawing.Size(29, 22);
      this.toolStripDropDownButton1.Text = "Help";
      // 
      // aboutToolStripMenuItem
      // 
      this.aboutToolStripMenuItem.Image = global::LsMsgPackFiddlerInspector.Properties.Resources.Info;
      this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
      this.aboutToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
      this.aboutToolStripMenuItem.Text = "About";
      this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
      // 
      // toolStripButton1
      // 
      this.toolStripButton1.CheckOnClick = true;
      this.toolStripButton1.Image = global::LsMsgPackFiddlerInspector.Properties.Resources.Broken;
      this.toolStripButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolStripButton1.Name = "toolStripButton1";
      this.toolStripButton1.Size = new System.Drawing.Size(173, 22);
      this.toolStripButton1.Text = "Keep processing after errors";
      this.toolStripButton1.ToolTipText = "Enable this to get a \"best effort\" view of contents after an error. Note that the" +
    " structure and remainder are totally unreliable and this feature is only for deb" +
    "ugging purposes.";
      this.toolStripButton1.CheckedChanged += new System.EventHandler(this.toolStripButton1_CheckedChanged);
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
      // btnObjects
      // 
      this.btnObjects.CheckOnClick = true;
      this.btnObjects.Image = global::LsMsgPackFiddlerInspector.Properties.Resources.Explore16;
      this.btnObjects.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.btnObjects.Name = "btnObjects";
      this.btnObjects.Size = new System.Drawing.Size(66, 22);
      this.btnObjects.Text = "Objects";
      this.btnObjects.ToolTipText = "Show the objects the data was written from (switched on when the data starts with" +
    " a schema)";
      this.btnObjects.CheckedChanged += new System.EventHandler(this.btnObjects_CheckedChanged);
      // 
      // toolStripSeparator2
      // 
      this.toolStripSeparator2.Name = "toolStripSeparator2";
      this.toolStripSeparator2.Size = new System.Drawing.Size(6, 25);
      // 
      // searchMatchCase
      // 
      this.searchMatchCase.CheckOnClick = true;
      this.searchMatchCase.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
      this.searchMatchCase.Name = "searchMatchCase";
      this.searchMatchCase.Size = new System.Drawing.Size(25, 22);
      this.searchMatchCase.Text = "Aa";
      this.searchMatchCase.ToolTipText = "Match case (of strings containing the text)";
      // 
      // searchTextBox
      // 
      this.searchTextBox.Font = new System.Drawing.Font("Segoe UI", 9F);
      this.searchTextBox.Name = "searchTextBox";
      this.searchTextBox.Size = new System.Drawing.Size(100, 25);
      this.searchTextBox.ToolTipText = "Search strings containing the text, and values it converts to (numbers, true/fals" +
    "e, null, Guid, dates). Hold Escape to stop searching.";
      // 
      // searchPrev
      // 
      this.searchPrev.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
      this.searchPrev.Enabled = false;
      this.searchPrev.Name = "searchPrev";
      this.searchPrev.Size = new System.Drawing.Size(23, 22);
      this.searchPrev.Text = "<";
      this.searchPrev.ToolTipText = "Previous (Shift+Enter)";
      // 
      // searchNext
      // 
      this.searchNext.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
      this.searchNext.Enabled = false;
      this.searchNext.Name = "searchNext";
      this.searchNext.Size = new System.Drawing.Size(23, 22);
      this.searchNext.Text = ">";
      this.searchNext.ToolTipText = "Next (Enter)";
      // 
      // searchPosCount
      // 
      this.searchPosCount.Name = "searchPosCount";
      this.searchPosCount.Size = new System.Drawing.Size(24, 22);
      this.searchPosCount.Text = "0/0";
      // 
      // lsMsgPackExplorer1
      // 
      this.lsMsgPackExplorer1.ContinueOnError = false;
      this.lsMsgPackExplorer1.Cursor = System.Windows.Forms.Cursors.Default;
      this.lsMsgPackExplorer1.Data = null;
      this.lsMsgPackExplorer1.DisplayLimit = ((long)(1000));
      this.lsMsgPackExplorer1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.lsMsgPackExplorer1.EndianHandling = LsMsgPack.EndianAction.SwapIfCurrentSystemIsLittleEndian;
      this.lsMsgPackExplorer1.Item = null;
      this.lsMsgPackExplorer1.Location = new System.Drawing.Point(0, 25);
      this.lsMsgPackExplorer1.Name = "lsMsgPackExplorer1";
      this.lsMsgPackExplorer1.Size = new System.Drawing.Size(769, 337);
      this.lsMsgPackExplorer1.TabIndex = 1;
      this.lsMsgPackExplorer1.ItemChanged += new System.EventHandler(this.lsMsgPackExplorer1_ItemChanged);
      // 
      // FiddlerWrapper
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.Controls.Add(this.lsMsgPackExplorer1);
      this.Controls.Add(this.toolStrip1);
      this.Name = "FiddlerWrapper";
      this.Size = new System.Drawing.Size(769, 362);
      this.toolStrip1.ResumeLayout(false);
      this.toolStrip1.PerformLayout();
      this.ResumeLayout(false);
      this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.ToolStrip toolStrip1;
    private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton1;
    private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
    private System.Windows.Forms.ToolStripButton toolStripButton1;
    public MsgPackExplorer.LsMsgPackExplorer lsMsgPackExplorer1;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
    private System.Windows.Forms.ToolStripLabel toolStripLabel1;
    private System.Windows.Forms.ToolStripComboBox ddLimitItems;
    private System.Windows.Forms.ToolStripLabel toolStripLabel2;
    private System.Windows.Forms.ToolStripComboBox ddEndianess;
    private System.Windows.Forms.ToolStripButton btnObjects;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
    private System.Windows.Forms.ToolStripButton searchMatchCase;
    private System.Windows.Forms.ToolStripTextBox searchTextBox;
    private System.Windows.Forms.ToolStripButton searchPrev;
    private System.Windows.Forms.ToolStripButton searchNext;
    private System.Windows.Forms.ToolStripLabel searchPosCount;
  }
}
