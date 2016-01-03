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
      this.toolStrip1 = new System.Windows.Forms.ToolStrip();
      this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
      this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
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
            this.toolStripButton1});
      this.toolStrip1.Location = new System.Drawing.Point(0, 0);
      this.toolStrip1.Name = "toolStrip1";
      this.toolStrip1.Padding = new System.Windows.Forms.Padding(0);
      this.toolStrip1.Size = new System.Drawing.Size(700, 25);
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
      this.aboutToolStripMenuItem.Size = new System.Drawing.Size(107, 22);
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
      // lsMsgPackExplorer1
      // 
      this.lsMsgPackExplorer1.ContinueOnError = false;
      this.lsMsgPackExplorer1.Data = null;
      this.lsMsgPackExplorer1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.lsMsgPackExplorer1.Item = null;
      this.lsMsgPackExplorer1.Location = new System.Drawing.Point(0, 25);
      this.lsMsgPackExplorer1.Name = "lsMsgPackExplorer1";
      this.lsMsgPackExplorer1.Size = new System.Drawing.Size(700, 337);
      this.lsMsgPackExplorer1.TabIndex = 1;
      // 
      // FiddlerWrapper
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.Controls.Add(this.lsMsgPackExplorer1);
      this.Controls.Add(this.toolStrip1);
      this.Name = "FiddlerWrapper";
      this.Size = new System.Drawing.Size(700, 362);
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
  }
}
