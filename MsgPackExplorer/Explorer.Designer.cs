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
      this.msgPackExplorer1 = new MsgPackExplorer.LsMsgPackExplorer();
      this.SuspendLayout();
      // 
      // msgPackExplorer1
      // 
      this.msgPackExplorer1.Cursor = System.Windows.Forms.Cursors.Default;
      this.msgPackExplorer1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.msgPackExplorer1.Location = new System.Drawing.Point(0, 0);
      this.msgPackExplorer1.Name = "msgPackExplorer1";
      this.msgPackExplorer1.Size = new System.Drawing.Size(986, 603);
      this.msgPackExplorer1.TabIndex = 0;
      // 
      // Explorer
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(986, 603);
      this.Controls.Add(this.msgPackExplorer1);
      this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
      this.Name = "Explorer";
      this.Text = "MsgPack Explorer";
      this.ResumeLayout(false);

    }

    #endregion

    private LsMsgPackExplorer msgPackExplorer1;
  }
}
