namespace MsgPackExplorer {
  partial class LsMsgPackExplorer {
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
      this.components = new System.ComponentModel.Container();
      System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LsMsgPackExplorer));
      System.Windows.Forms.ListViewItem listViewItem1 = new System.Windows.Forms.ListViewItem(new string[] {
            "0",
            "No data..."}, -1);
      this.treeView1 = new System.Windows.Forms.TreeView();
      this.imageList1 = new System.Windows.Forms.ImageList(this.components);
      this.splitter1 = new System.Windows.Forms.Splitter();
      this.propertyGrid1 = new System.Windows.Forms.PropertyGrid();
      this.panel1 = new System.Windows.Forms.Panel();
      this.richTextBox1 = new System.Windows.Forms.RichTextBox();
      this.splitter2 = new System.Windows.Forms.Splitter();
      this.statusStrip1 = new System.Windows.Forms.StatusStrip();
      this.offsetLableText = new System.Windows.Forms.ToolStripStatusLabel();
      this.statusOffset = new System.Windows.Forms.ToolStripStatusLabel();
      this.panel2 = new System.Windows.Forms.Panel();
      this.splitter3 = new System.Windows.Forms.Splitter();
      this.panel3 = new System.Windows.Forms.Panel();
      this.listView1 = new System.Windows.Forms.ListView();
      this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
      this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
      this.imageListValidation = new System.Windows.Forms.ImageList(this.components);
      this.splitter4 = new System.Windows.Forms.Splitter();
      this.errorDetails = new System.Windows.Forms.TextBox();
      this.panel1.SuspendLayout();
      this.statusStrip1.SuspendLayout();
      this.panel2.SuspendLayout();
      this.panel3.SuspendLayout();
      this.SuspendLayout();
      // 
      // treeView1
      // 
      this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.treeView1.DrawMode = System.Windows.Forms.TreeViewDrawMode.OwnerDrawText;
      this.treeView1.HideSelection = false;
      this.treeView1.ImageIndex = 0;
      this.treeView1.ImageList = this.imageList1;
      this.treeView1.Location = new System.Drawing.Point(0, 0);
      this.treeView1.Name = "treeView1";
      this.treeView1.SelectedImageIndex = 0;
      this.treeView1.Size = new System.Drawing.Size(277, 245);
      this.treeView1.StateImageList = this.imageList1;
      this.treeView1.TabIndex = 0;
      this.treeView1.DrawNode += new System.Windows.Forms.DrawTreeNodeEventHandler(this.treeView1_DrawNode);
      this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
      // 
      // imageList1
      // 
      this.imageList1.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList1.ImageStream")));
      this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
      this.imageList1.Images.SetKeyName(0, "Null.png");
      this.imageList1.Images.SetKeyName(1, "Bool.png");
      this.imageList1.Images.SetKeyName(2, "Int.png");
      this.imageList1.Images.SetKeyName(3, "Float.png");
      this.imageList1.Images.SetKeyName(4, "Bin.png");
      this.imageList1.Images.SetKeyName(5, "String.png");
      this.imageList1.Images.SetKeyName(6, "Array.png");
      this.imageList1.Images.SetKeyName(7, "Map.png");
      this.imageList1.Images.SetKeyName(8, "Key.png");
      this.imageList1.Images.SetKeyName(9, "Value.png");
      this.imageList1.Images.SetKeyName(10, "Extension.png");
      this.imageList1.Images.SetKeyName(11, "Broken.png");
      // 
      // splitter1
      // 
      this.splitter1.Dock = System.Windows.Forms.DockStyle.Right;
      this.splitter1.Location = new System.Drawing.Point(277, 0);
      this.splitter1.Name = "splitter1";
      this.splitter1.Size = new System.Drawing.Size(7, 352);
      this.splitter1.TabIndex = 1;
      this.splitter1.TabStop = false;
      // 
      // propertyGrid1
      // 
      this.propertyGrid1.CategoryForeColor = System.Drawing.SystemColors.InactiveCaptionText;
      this.propertyGrid1.Dock = System.Windows.Forms.DockStyle.Top;
      this.propertyGrid1.Location = new System.Drawing.Point(0, 0);
      this.propertyGrid1.Name = "propertyGrid1";
      this.propertyGrid1.Size = new System.Drawing.Size(355, 252);
      this.propertyGrid1.TabIndex = 2;
      // 
      // panel1
      // 
      this.panel1.Controls.Add(this.richTextBox1);
      this.panel1.Controls.Add(this.splitter2);
      this.panel1.Controls.Add(this.propertyGrid1);
      this.panel1.Controls.Add(this.statusStrip1);
      this.panel1.Dock = System.Windows.Forms.DockStyle.Right;
      this.panel1.Location = new System.Drawing.Point(284, 0);
      this.panel1.Name = "panel1";
      this.panel1.Size = new System.Drawing.Size(355, 352);
      this.panel1.TabIndex = 4;
      // 
      // richTextBox1
      // 
      this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.richTextBox1.Font = new System.Drawing.Font("Segoe UI Mono", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
      this.richTextBox1.HideSelection = false;
      this.richTextBox1.Location = new System.Drawing.Point(0, 259);
      this.richTextBox1.Name = "richTextBox1";
      this.richTextBox1.ReadOnly = true;
      this.richTextBox1.Size = new System.Drawing.Size(355, 71);
      this.richTextBox1.TabIndex = 4;
      this.richTextBox1.Text = "";
      this.richTextBox1.SelectionChanged += new System.EventHandler(this.richTextBox1_SelectionChanged);
      // 
      // splitter2
      // 
      this.splitter2.Dock = System.Windows.Forms.DockStyle.Top;
      this.splitter2.Location = new System.Drawing.Point(0, 252);
      this.splitter2.Name = "splitter2";
      this.splitter2.Size = new System.Drawing.Size(355, 7);
      this.splitter2.TabIndex = 3;
      this.splitter2.TabStop = false;
      // 
      // statusStrip1
      // 
      this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.offsetLableText,
            this.statusOffset});
      this.statusStrip1.Location = new System.Drawing.Point(0, 330);
      this.statusStrip1.Name = "statusStrip1";
      this.statusStrip1.Size = new System.Drawing.Size(355, 22);
      this.statusStrip1.TabIndex = 5;
      this.statusStrip1.Text = "statusStrip1";
      // 
      // offsetLableText
      // 
      this.offsetLableText.Name = "offsetLableText";
      this.offsetLableText.Size = new System.Drawing.Size(45, 17);
      this.offsetLableText.Text = "Offset: ";
      // 
      // statusOffset
      // 
      this.statusOffset.ForeColor = System.Drawing.Color.Navy;
      this.statusOffset.Name = "statusOffset";
      this.statusOffset.Size = new System.Drawing.Size(13, 17);
      this.statusOffset.Text = "0";
      // 
      // panel2
      // 
      this.panel2.Controls.Add(this.treeView1);
      this.panel2.Controls.Add(this.splitter3);
      this.panel2.Controls.Add(this.panel3);
      this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
      this.panel2.Location = new System.Drawing.Point(0, 0);
      this.panel2.Name = "panel2";
      this.panel2.Size = new System.Drawing.Size(277, 352);
      this.panel2.TabIndex = 5;
      // 
      // splitter3
      // 
      this.splitter3.Dock = System.Windows.Forms.DockStyle.Bottom;
      this.splitter3.Location = new System.Drawing.Point(0, 245);
      this.splitter3.Name = "splitter3";
      this.splitter3.Size = new System.Drawing.Size(277, 7);
      this.splitter3.TabIndex = 4;
      this.splitter3.TabStop = false;
      // 
      // panel3
      // 
      this.panel3.Controls.Add(this.listView1);
      this.panel3.Controls.Add(this.splitter4);
      this.panel3.Controls.Add(this.errorDetails);
      this.panel3.Dock = System.Windows.Forms.DockStyle.Bottom;
      this.panel3.Location = new System.Drawing.Point(0, 252);
      this.panel3.Name = "panel3";
      this.panel3.Size = new System.Drawing.Size(277, 100);
      this.panel3.TabIndex = 5;
      // 
      // listView1
      // 
      this.listView1.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2});
      this.listView1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.listView1.FullRowSelect = true;
      this.listView1.GridLines = true;
      this.listView1.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
      this.listView1.HideSelection = false;
      this.listView1.Items.AddRange(new System.Windows.Forms.ListViewItem[] {
            listViewItem1});
      this.listView1.Location = new System.Drawing.Point(0, 0);
      this.listView1.Name = "listView1";
      this.listView1.Size = new System.Drawing.Size(88, 100);
      this.listView1.SmallImageList = this.imageList1;
      this.listView1.StateImageList = this.imageListValidation;
      this.listView1.TabIndex = 1;
      this.listView1.UseCompatibleStateImageBehavior = false;
      this.listView1.View = System.Windows.Forms.View.Details;
      this.listView1.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
      // 
      // columnHeader1
      // 
      this.columnHeader1.Text = "Bytes";
      // 
      // columnHeader2
      // 
      this.columnHeader2.Text = "Description";
      this.columnHeader2.Width = 500;
      // 
      // imageListValidation
      // 
      this.imageListValidation.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageListValidation.ImageStream")));
      this.imageListValidation.TransparentColor = System.Drawing.Color.Transparent;
      this.imageListValidation.Images.SetKeyName(0, "Error.png");
      this.imageListValidation.Images.SetKeyName(1, "Warning.png");
      this.imageListValidation.Images.SetKeyName(2, "Info.png");
      this.imageListValidation.Images.SetKeyName(3, "Broken.png");
      // 
      // splitter4
      // 
      this.splitter4.Dock = System.Windows.Forms.DockStyle.Right;
      this.splitter4.Location = new System.Drawing.Point(88, 0);
      this.splitter4.Name = "splitter4";
      this.splitter4.Size = new System.Drawing.Size(7, 100);
      this.splitter4.TabIndex = 2;
      this.splitter4.TabStop = false;
      this.splitter4.Visible = false;
      // 
      // errorDetails
      // 
      this.errorDetails.Dock = System.Windows.Forms.DockStyle.Right;
      this.errorDetails.Location = new System.Drawing.Point(95, 0);
      this.errorDetails.Multiline = true;
      this.errorDetails.Name = "errorDetails";
      this.errorDetails.ReadOnly = true;
      this.errorDetails.Size = new System.Drawing.Size(182, 100);
      this.errorDetails.TabIndex = 3;
      this.errorDetails.Visible = false;
      // 
      // MsgPackExplorer
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.Controls.Add(this.panel2);
      this.Controls.Add(this.splitter1);
      this.Controls.Add(this.panel1);
      this.Name = "MsgPackExplorer";
      this.Size = new System.Drawing.Size(639, 352);
      this.panel1.ResumeLayout(false);
      this.panel1.PerformLayout();
      this.statusStrip1.ResumeLayout(false);
      this.statusStrip1.PerformLayout();
      this.panel2.ResumeLayout(false);
      this.panel3.ResumeLayout(false);
      this.panel3.PerformLayout();
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.TreeView treeView1;
    private System.Windows.Forms.Splitter splitter1;
    private System.Windows.Forms.PropertyGrid propertyGrid1;
    private System.Windows.Forms.ImageList imageList1;
    private System.Windows.Forms.Panel panel1;
    private System.Windows.Forms.Splitter splitter2;
    private System.Windows.Forms.RichTextBox richTextBox1;
    private System.Windows.Forms.StatusStrip statusStrip1;
    private System.Windows.Forms.ToolStripStatusLabel offsetLableText;
    private System.Windows.Forms.ToolStripStatusLabel statusOffset;
    private System.Windows.Forms.Panel panel2;
    private System.Windows.Forms.Splitter splitter3;
    private System.Windows.Forms.ListView listView1;
    private System.Windows.Forms.ColumnHeader columnHeader1;
    private System.Windows.Forms.ColumnHeader columnHeader2;
    private System.Windows.Forms.ImageList imageListValidation;
    private System.Windows.Forms.Panel panel3;
    private System.Windows.Forms.Splitter splitter4;
    private System.Windows.Forms.TextBox errorDetails;
  }
}
