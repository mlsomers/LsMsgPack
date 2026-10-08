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
      System.Windows.Forms.ListViewItem listViewItem5 = new System.Windows.Forms.ListViewItem(new string[] {
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
      this.objectsPane = new System.Windows.Forms.Panel();
      this.treeViewObjects = new System.Windows.Forms.TreeView();
      this.splittObjProps = new System.Windows.Forms.Splitter();
      this.propertyGridObjects = new System.Windows.Forms.PropertyGrid();
      this.splitterObj = new System.Windows.Forms.Splitter();
      this.toolStrip1 = new System.Windows.Forms.ToolStrip();
      this.toolFileMenu = new System.Windows.Forms.ToolStripDropDownButton();
      this.btnOpen = new System.Windows.Forms.ToolStripMenuItem();
      this.fromClipboardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolToolsMenu = new System.Windows.Forms.ToolStripDropDownButton();
      this.btnGenerateTestFiles = new System.Windows.Forms.ToolStripMenuItem();
      this.btnProcessAfterError = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
      this.installAsFiddlerInspectorToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.menUnistallFiddler = new System.Windows.Forms.ToolStripMenuItem();
      this.installAsVsPluginToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.menUninstallVisualStudio = new System.Windows.Forms.ToolStripMenuItem();
      this.toolViewMenu = new System.Windows.Forms.ToolStripDropDownButton();
      this.objectsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
      this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
      this.ddLimitItems = new System.Windows.Forms.ToolStripComboBox();
      this.toolStripLabel2 = new System.Windows.Forms.ToolStripLabel();
      this.ddEndianess = new System.Windows.Forms.ToolStripComboBox();
      this.toolHelpMenu = new System.Windows.Forms.ToolStripDropDownButton();
      this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
      this.searchMatchCase = new System.Windows.Forms.ToolStripButton();
      this.searchTextBox = new System.Windows.Forms.ToolStripTextBox();
      this.searchPrev = new System.Windows.Forms.ToolStripButton();
      this.searchNext = new System.Windows.Forms.ToolStripButton();
      this.searchPosCount = new System.Windows.Forms.ToolStripLabel();
      this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
      this.saveTestSuiteDialog = new System.Windows.Forms.SaveFileDialog();
      this.lblObj = new System.Windows.Forms.Label();
      this.lblProps = new System.Windows.Forms.Label();
      this.panel1.SuspendLayout();
      this.statusStrip1.SuspendLayout();
      this.panel2.SuspendLayout();
      this.panel3.SuspendLayout();
      this.objectsPane.SuspendLayout();
      this.toolStrip1.SuspendLayout();
      this.SuspendLayout();
      // 
      // treeView1
      // 
      this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.treeView1.DrawMode = System.Windows.Forms.TreeViewDrawMode.OwnerDrawText;
      this.treeView1.FullRowSelect = true;
      this.treeView1.HideSelection = false;
      this.treeView1.ImageIndex = 0;
      this.treeView1.ImageList = this.imageList1;
      this.treeView1.Location = new System.Drawing.Point(0, 0);
      this.treeView1.Name = "treeView1";
      this.treeView1.SelectedImageIndex = 0;
      this.treeView1.Size = new System.Drawing.Size(277, 372);
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
      this.imageList1.Images.SetKeyName(12, "Explore.png");
      // 
      // splitter1
      // 
      this.splitter1.Dock = System.Windows.Forms.DockStyle.Right;
      this.splitter1.Location = new System.Drawing.Point(277, 25);
      this.splitter1.Name = "splitter1";
      this.splitter1.Size = new System.Drawing.Size(7, 479);
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
      this.panel1.Location = new System.Drawing.Point(284, 25);
      this.panel1.Name = "panel1";
      this.panel1.Size = new System.Drawing.Size(355, 479);
      this.panel1.TabIndex = 4;
      // 
      // richTextBox1
      // 
      this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.richTextBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
      this.richTextBox1.HideSelection = false;
      this.richTextBox1.Location = new System.Drawing.Point(0, 259);
      this.richTextBox1.Name = "richTextBox1";
      this.richTextBox1.ReadOnly = true;
      this.richTextBox1.Size = new System.Drawing.Size(355, 198);
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
      this.statusStrip1.Location = new System.Drawing.Point(0, 457);
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
      this.panel2.Location = new System.Drawing.Point(0, 25);
      this.panel2.Name = "panel2";
      this.panel2.Size = new System.Drawing.Size(277, 479);
      this.panel2.TabIndex = 5;
      // 
      // splitter3
      // 
      this.splitter3.Dock = System.Windows.Forms.DockStyle.Bottom;
      this.splitter3.Location = new System.Drawing.Point(0, 372);
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
      this.panel3.Location = new System.Drawing.Point(0, 379);
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
            listViewItem5});
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
      // objectsPane
      // 
      this.objectsPane.Controls.Add(this.treeViewObjects);
      this.objectsPane.Controls.Add(this.splittObjProps);
      this.objectsPane.Controls.Add(this.propertyGridObjects);
      this.objectsPane.Dock = System.Windows.Forms.DockStyle.Bottom;
      this.objectsPane.Location = new System.Drawing.Point(0, 497);
      this.objectsPane.Name = "objectsPane";
      this.objectsPane.Size = new System.Drawing.Size(639, 205);
      this.objectsPane.TabIndex = 6;
      this.objectsPane.Visible = false;
      // 
      // treeViewObjects
      // 
      this.treeViewObjects.Dock = System.Windows.Forms.DockStyle.Fill;
      this.treeViewObjects.DrawMode = System.Windows.Forms.TreeViewDrawMode.OwnerDrawText;
      this.treeViewObjects.FullRowSelect = true;
      this.treeViewObjects.HideSelection = false;
      this.treeViewObjects.ImageIndex = 0;
      this.treeViewObjects.ImageList = this.imageList1;
      this.treeViewObjects.Location = new System.Drawing.Point(0, 0);
      this.treeViewObjects.Name = "treeViewObjects";
      this.treeViewObjects.SelectedImageIndex = 0;
      this.treeViewObjects.Size = new System.Drawing.Size(332, 205);
      this.treeViewObjects.StateImageList = this.imageList1;
      this.treeViewObjects.TabIndex = 1;
      this.treeViewObjects.DrawNode += new System.Windows.Forms.DrawTreeNodeEventHandler(this.treeView1_DrawNode);
      this.treeViewObjects.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeViewObjects_AfterSelect);
      // 
      // splittObjProps
      // 
      this.splittObjProps.Dock = System.Windows.Forms.DockStyle.Right;
      this.splittObjProps.Location = new System.Drawing.Point(332, 0);
      this.splittObjProps.Name = "splittObjProps";
      this.splittObjProps.Size = new System.Drawing.Size(7, 205);
      this.splittObjProps.TabIndex = 2;
      this.splittObjProps.TabStop = false;
      this.splittObjProps.SplitterMoved += new System.Windows.Forms.SplitterEventHandler(this.splittObjProps_SplitterMoved);
      this.splittObjProps.LocationChanged += new System.EventHandler(this.splittObjProps_VisibleChanged);
      this.splittObjProps.VisibleChanged += new System.EventHandler(this.splittObjProps_VisibleChanged);
      // 
      // propertyGridObjects
      // 
      this.propertyGridObjects.CategoryForeColor = System.Drawing.SystemColors.InactiveCaptionText;
      this.propertyGridObjects.Dock = System.Windows.Forms.DockStyle.Right;
      this.propertyGridObjects.Location = new System.Drawing.Point(339, 0);
      this.propertyGridObjects.Name = "propertyGridObjects";
      this.propertyGridObjects.PropertySort = System.Windows.Forms.PropertySort.Categorized;
      this.propertyGridObjects.Size = new System.Drawing.Size(300, 205);
      this.propertyGridObjects.TabIndex = 3;
      // 
      // splitterObj
      // 
      this.splitterObj.Dock = System.Windows.Forms.DockStyle.Bottom;
      this.splitterObj.Location = new System.Drawing.Point(0, 479);
      this.splitterObj.Name = "splitterObj";
      this.splitterObj.Size = new System.Drawing.Size(639, 18);
      this.splitterObj.TabIndex = 7;
      this.splitterObj.TabStop = false;
      this.splitterObj.Visible = false;
      //
      // toolStrip1
      //
      this.toolStrip1.GripMargin = new System.Windows.Forms.Padding(0);
      this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
      this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolFileMenu,
            this.toolToolsMenu,
            this.toolViewMenu,
            this.toolStripSeparator1,
            this.toolStripLabel1,
            this.ddLimitItems,
            this.toolStripLabel2,
            this.ddEndianess,
            this.toolHelpMenu,
            this.toolStripSeparator3,
            this.searchMatchCase,
            this.searchTextBox,
            this.searchPrev,
            this.searchNext,
            this.searchPosCount});
      this.toolStrip1.Location = new System.Drawing.Point(0, 0);
      this.toolStrip1.Name = "toolStrip1";
      this.toolStrip1.Padding = new System.Windows.Forms.Padding(0);
      this.toolStrip1.Size = new System.Drawing.Size(639, 25);
      this.toolStrip1.TabIndex = 8;
      this.toolStrip1.Text = "toolStrip1";
      //
      // toolFileMenu
      //
      this.toolFileMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.btnOpen,
            this.fromClipboardToolStripMenuItem});
      this.toolFileMenu.Image = global::MsgPackExplorer.Properties.Resources.Open;
      this.toolFileMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolFileMenu.Name = "toolFileMenu";
      this.toolFileMenu.Size = new System.Drawing.Size(54, 22);
      this.toolFileMenu.Text = "File";
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
      // toolToolsMenu
      //
      this.toolToolsMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.btnGenerateTestFiles,
            this.btnProcessAfterError,
            this.toolStripSeparator2,
            this.installAsFiddlerInspectorToolStripMenuItem,
            this.installAsVsPluginToolStripMenuItem});
      this.toolToolsMenu.Image = global::MsgPackExplorer.Properties.Resources.Tools;
      this.toolToolsMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolToolsMenu.Name = "toolToolsMenu";
      this.toolToolsMenu.Size = new System.Drawing.Size(63, 22);
      this.toolToolsMenu.Text = "Tools";
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
      this.btnProcessAfterError.ToolTipText = "Enable this to get a \"best effort\" view of contents after an error. Note that the" +
    " structure and remainder are totally unreliable and this feature is only for deb" +
    "ugging purposes.";
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
      this.menUnistallFiddler.Size = new System.Drawing.Size(120, 22);
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
      this.menUninstallVisualStudio.Size = new System.Drawing.Size(120, 22);
      this.menUninstallVisualStudio.Text = "Uninstall";
      this.menUninstallVisualStudio.Click += new System.EventHandler(this.menUninstallVisualStudio_Click);
      //
      // toolViewMenu
      //
      this.toolViewMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.objectsMenuItem});
      this.toolViewMenu.Image = global::MsgPackExplorer.Properties.Resources.Explore16;
      this.toolViewMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolViewMenu.Name = "toolViewMenu";
      this.toolViewMenu.Size = new System.Drawing.Size(61, 22);
      this.toolViewMenu.Text = "View";
      //
      // objectsMenuItem
      //
      this.objectsMenuItem.CheckOnClick = true;
      this.objectsMenuItem.Image = global::MsgPackExplorer.Properties.Resources.Explore16;
      this.objectsMenuItem.Name = "objectsMenuItem";
      this.objectsMenuItem.Size = new System.Drawing.Size(114, 22);
      this.objectsMenuItem.Text = "Objects";
      this.objectsMenuItem.ToolTipText = "Show the objects the data was written from (switched on when the data looks like " +
    "objects)";
      this.objectsMenuItem.CheckedChanged += new System.EventHandler(this.objectsMenuItem_CheckedChanged);
      //
      // toolStripSeparator1
      //
      this.toolStripSeparator1.Name = "toolStripSeparator1";
      this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
      //
      // toolStripLabel1
      //
      this.toolStripLabel1.Name = "toolStripLabel1";
      this.toolStripLabel1.Size = new System.Drawing.Size(37, 22);
      this.toolStripLabel1.Text = "Limit:";
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
      this.ddLimitItems.Size = new System.Drawing.Size(90, 25);
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
      // toolHelpMenu
      //
      this.toolHelpMenu.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
      this.toolHelpMenu.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
      this.toolHelpMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
      this.toolHelpMenu.Image = global::MsgPackExplorer.Properties.Resources.Help;
      this.toolHelpMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
      this.toolHelpMenu.Name = "toolHelpMenu";
      this.toolHelpMenu.Size = new System.Drawing.Size(29, 22);
      this.toolHelpMenu.Text = "Help";
      //
      // aboutToolStripMenuItem
      //
      this.aboutToolStripMenuItem.Image = global::MsgPackExplorer.Properties.Resources.Info;
      this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
      this.aboutToolStripMenuItem.Size = new System.Drawing.Size(107, 22);
      this.aboutToolStripMenuItem.Text = "About";
      this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
      //
      // toolStripSeparator3
      //
      this.toolStripSeparator3.Name = "toolStripSeparator3";
      this.toolStripSeparator3.Size = new System.Drawing.Size(6, 25);
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
      this.splitterObj.SplitterMoved += new System.Windows.Forms.SplitterEventHandler(this.splitterObj_SplitterMoved);
      this.splitterObj.LocationChanged += new System.EventHandler(this.splitterObj_VisibleChanged);
      this.splitterObj.VisibleChanged += new System.EventHandler(this.splitterObj_VisibleChanged);
      this.splitterObj.Layout += new System.Windows.Forms.LayoutEventHandler(this.splitterObj_Layout);
      // 
      // lblObj
      // 
      this.lblObj.AutoSize = true;
      this.lblObj.Location = new System.Drawing.Point(3, 481);
      this.lblObj.Name = "lblObj";
      this.lblObj.Size = new System.Drawing.Size(43, 13);
      this.lblObj.TabIndex = 8;
      this.lblObj.Text = "Objects";
      // 
      // lblProps
      // 
      this.lblProps.AutoSize = true;
      this.lblProps.Location = new System.Drawing.Point(336, 481);
      this.lblProps.Name = "lblProps";
      this.lblProps.Size = new System.Drawing.Size(54, 13);
      this.lblProps.TabIndex = 9;
      this.lblProps.Text = "Properties";
      // 
      // LsMsgPackExplorer
      //
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.Controls.Add(this.panel2);
      this.Controls.Add(this.splitter1);
      this.Controls.Add(this.panel1);
      this.Controls.Add(this.lblObj);
      this.Controls.Add(this.lblProps);
      this.Controls.Add(this.splitterObj);
      this.Controls.Add(this.objectsPane);
      this.Controls.Add(this.toolStrip1);
      this.Name = "LsMsgPackExplorer";
      this.Size = new System.Drawing.Size(639, 727);
      this.panel1.ResumeLayout(false);
      this.panel1.PerformLayout();
      this.statusStrip1.ResumeLayout(false);
      this.statusStrip1.PerformLayout();
      this.panel2.ResumeLayout(false);
      this.panel3.ResumeLayout(false);
      this.panel3.PerformLayout();
      this.objectsPane.ResumeLayout(false);
      this.toolStrip1.ResumeLayout(false);
      this.toolStrip1.PerformLayout();
      this.ResumeLayout(false);
      this.PerformLayout();

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
    private System.Windows.Forms.Panel objectsPane;
    private System.Windows.Forms.Splitter splitterObj;
    private System.Windows.Forms.TreeView treeViewObjects;
    private System.Windows.Forms.Splitter splittObjProps;
    private System.Windows.Forms.PropertyGrid propertyGridObjects;
    private System.Windows.Forms.Label lblObj;
    private System.Windows.Forms.Label lblProps;
    private System.Windows.Forms.ToolStrip toolStrip1;
    private System.Windows.Forms.ToolStripDropDownButton toolFileMenu;
    private System.Windows.Forms.ToolStripMenuItem btnOpen;
    private System.Windows.Forms.ToolStripMenuItem fromClipboardToolStripMenuItem;
    private System.Windows.Forms.ToolStripDropDownButton toolToolsMenu;
    private System.Windows.Forms.ToolStripMenuItem btnGenerateTestFiles;
    private System.Windows.Forms.ToolStripMenuItem btnProcessAfterError;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
    private System.Windows.Forms.ToolStripMenuItem installAsFiddlerInspectorToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem menUnistallFiddler;
    private System.Windows.Forms.ToolStripMenuItem installAsVsPluginToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem menUninstallVisualStudio;
    private System.Windows.Forms.ToolStripDropDownButton toolViewMenu;
    private System.Windows.Forms.ToolStripMenuItem objectsMenuItem;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
    private System.Windows.Forms.ToolStripLabel toolStripLabel1;
    private System.Windows.Forms.ToolStripComboBox ddLimitItems;
    private System.Windows.Forms.ToolStripLabel toolStripLabel2;
    private System.Windows.Forms.ToolStripComboBox ddEndianess;
    private System.Windows.Forms.ToolStripDropDownButton toolHelpMenu;
    private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
    private System.Windows.Forms.ToolStripButton searchMatchCase;
    private System.Windows.Forms.ToolStripTextBox searchTextBox;
    private System.Windows.Forms.ToolStripButton searchPrev;
    private System.Windows.Forms.ToolStripButton searchNext;
    private System.Windows.Forms.ToolStripLabel searchPosCount;
    private System.Windows.Forms.OpenFileDialog openFileDialog1;
    private System.Windows.Forms.SaveFileDialog saveTestSuiteDialog;
  }
}
