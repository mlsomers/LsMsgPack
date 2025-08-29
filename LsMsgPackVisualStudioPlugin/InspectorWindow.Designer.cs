using System;

namespace LsMsgPackVisualStudioPlugin
{
  partial class InspectorWindow
  {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
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
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(InspectorWindow));
      this.toolStrip1 = new System.Windows.Forms.ToolStrip();
      this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
      this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
      this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
      this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
      this.ddLimitItems = new System.Windows.Forms.ToolStripComboBox();
      this.toolStripLabel2 = new System.Windows.Forms.ToolStripLabel();
      this.ddEndianess = new System.Windows.Forms.ToolStripComboBox();
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
            this.toolStripSeparator1,
            this.toolStripLabel1,
            this.ddLimitItems,
            this.toolStripLabel2,
            this.ddEndianess});
      this.toolStrip1.Location = new System.Drawing.Point(0, 0);
      this.toolStrip1.Name = "toolStrip1";
      this.toolStrip1.Padding = new System.Windows.Forms.Padding(0);
      this.toolStrip1.Size = new System.Drawing.Size(800, 25);
      this.toolStrip1.TabIndex = 1;
      this.toolStrip1.Text = "toolStrip1";
      this.toolStripButton1.CheckedChanged += new System.EventHandler(this.toolStripButton1_CheckedChanged);
      // 
      // toolStripDropDownButton1
      // 
      this.toolStripDropDownButton1.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
      this.toolStripDropDownButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
      this.toolStripDropDownButton1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
      this.toolStripDropDownButton1.Image = global::LsMsgPackVisualStudioPlugin.Properties.Resources.Help;
      this.toolStripDropDownButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolStripDropDownButton1.Name = "toolStripDropDownButton1";
      this.toolStripDropDownButton1.Size = new System.Drawing.Size(29, 22);
      this.toolStripDropDownButton1.Text = "Help";
      // 
      // aboutToolStripMenuItem
      // 
      this.aboutToolStripMenuItem.Image = global::LsMsgPackVisualStudioPlugin.Properties.Resources.Info;
      this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
      this.aboutToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
      this.aboutToolStripMenuItem.Text = "About";
      this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
      // 
      // toolStripButton1
      // 
      this.toolStripButton1.CheckOnClick = true;
      this.toolStripButton1.Image = global::LsMsgPackVisualStudioPlugin.Properties.Resources.Broken;
      this.toolStripButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolStripButton1.Name = "toolStripButton1";
      this.toolStripButton1.Size = new System.Drawing.Size(173, 22);
      this.toolStripButton1.Text = "Keep processing after errors";
      this.toolStripButton1.ToolTipText = "Enable this to get a \"best effort\" view of contents after an error. Note that the" +
    " structure and remainder are totally unreliable and this feature is only for deb" +
    "ugging purposes.";
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
      // InspectorWindow
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(800, 450);
      this.Controls.Add(this.toolStrip1);
      this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
      this.Name = "InspectorWindow";
      this.Text = "MsgPack Explorer";
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
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
    private System.Windows.Forms.ToolStripLabel toolStripLabel1;
    private System.Windows.Forms.ToolStripComboBox ddLimitItems;
    private System.Windows.Forms.ToolStripLabel toolStripLabel2;
    private System.Windows.Forms.ToolStripComboBox ddEndianess;
  }
}