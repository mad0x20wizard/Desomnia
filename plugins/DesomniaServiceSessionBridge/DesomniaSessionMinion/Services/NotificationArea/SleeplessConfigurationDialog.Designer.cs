namespace MadWizard.Desomnia.Minion
{
    partial class SleeplessConfigurationWindow
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SleeplessConfigurationWindow));
            this.buttonOK = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.checkBoxTime = new System.Windows.Forms.CheckBox();
            this.checkBoxPermanent = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.labelDescription = new System.Windows.Forms.Label();
            this.dateTimePicker = new System.Windows.Forms.DateTimePicker();
            this.groupBoxUsage = new System.Windows.Forms.GroupBox();
            this.progressBarInspection = new System.Windows.Forms.ProgressBar();
            this.checkBoxUsage = new System.Windows.Forms.CheckBox();
            this.timer = new System.Windows.Forms.Timer(this.components);
            this.toolTipProgress = new System.Windows.Forms.ToolTip(this.components);
            this.treeListViewTokens = new BrightIdeasSoftware.TreeListView();
            this.olvColumnName = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumnType = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumnDuration = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.groupBox1.SuspendLayout();
            this.groupBoxUsage.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.treeListViewTokens)).BeginInit();
            this.SuspendLayout();
            // 
            // buttonOK
            // 
            this.buttonOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonOK.AutoSize = true;
            this.buttonOK.Location = new System.Drawing.Point(143, 448);
            this.buttonOK.Margin = new System.Windows.Forms.Padding(2);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(80, 23);
            this.buttonOK.TabIndex = 4;
            this.buttonOK.Text = "OK";
            this.buttonOK.UseVisualStyleBackColor = true;
            this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);
            // 
            // buttonCancel
            // 
            this.buttonCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(227, 448);
            this.buttonCancel.Margin = new System.Windows.Forms.Padding(2);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(80, 23);
            this.buttonCancel.TabIndex = 5;
            this.buttonCancel.Text = "Abbrechen";
            this.buttonCancel.UseVisualStyleBackColor = true;
            this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
            // 
            // checkBoxTime
            // 
            this.checkBoxTime.AutoSize = true;
            this.checkBoxTime.Location = new System.Drawing.Point(18, 36);
            this.checkBoxTime.Name = "checkBoxTime";
            this.checkBoxTime.Size = new System.Drawing.Size(88, 17);
            this.checkBoxTime.TabIndex = 14;
            this.checkBoxTime.Text = "Zeitgesteuert";
            this.checkBoxTime.UseVisualStyleBackColor = true;
            this.checkBoxTime.CheckedChanged += new System.EventHandler(this.checkBoxTime_CheckedChanged);
            // 
            // checkBoxPermanent
            // 
            this.checkBoxPermanent.AutoSize = true;
            this.checkBoxPermanent.Location = new System.Drawing.Point(18, 12);
            this.checkBoxPermanent.Name = "checkBoxPermanent";
            this.checkBoxPermanent.Size = new System.Drawing.Size(73, 17);
            this.checkBoxPermanent.TabIndex = 13;
            this.checkBoxPermanent.Text = "Dauerhaft";
            this.checkBoxPermanent.UseVisualStyleBackColor = true;
            this.checkBoxPermanent.CheckedChanged += new System.EventHandler(this.checkBoxPermanent_CheckedChanged);
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.Controls.Add(this.labelDescription);
            this.groupBox1.Controls.Add(this.dateTimePicker);
            this.groupBox1.Enabled = false;
            this.groupBox1.Location = new System.Drawing.Point(9, 37);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(298, 82);
            this.groupBox1.TabIndex = 12;
            this.groupBox1.TabStop = false;
            // 
            // labelDescription
            // 
            this.labelDescription.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labelDescription.Location = new System.Drawing.Point(8, 45);
            this.labelDescription.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.labelDescription.Name = "labelDescription";
            this.labelDescription.Size = new System.Drawing.Size(270, 34);
            this.labelDescription.TabIndex = 3;
            this.labelDescription.Text = "Zur eingestellten Zeit wird der Schlaflos-Modus automatisch deaktiviert.\r\n";
            this.labelDescription.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // dateTimePicker
            // 
            this.dateTimePicker.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dateTimePicker.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            this.dateTimePicker.Location = new System.Drawing.Point(6, 20);
            this.dateTimePicker.Margin = new System.Windows.Forms.Padding(2);
            this.dateTimePicker.Name = "dateTimePicker";
            this.dateTimePicker.Size = new System.Drawing.Size(286, 20);
            this.dateTimePicker.TabIndex = 0;
            // 
            // groupBoxUsage
            // 
            this.groupBoxUsage.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxUsage.Controls.Add(this.treeListViewTokens);
            this.groupBoxUsage.Controls.Add(this.progressBarInspection);
            this.groupBoxUsage.Location = new System.Drawing.Point(9, 128);
            this.groupBoxUsage.Name = "groupBoxUsage";
            this.groupBoxUsage.Size = new System.Drawing.Size(298, 315);
            this.groupBoxUsage.TabIndex = 17;
            this.groupBoxUsage.TabStop = false;
            // 
            // progressBarInspection
            // 
            this.progressBarInspection.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBarInspection.Cursor = System.Windows.Forms.Cursors.AppStarting;
            this.progressBarInspection.ForeColor = System.Drawing.Color.Gold;
            this.progressBarInspection.Location = new System.Drawing.Point(6, 294);
            this.progressBarInspection.Name = "progressBarInspection";
            this.progressBarInspection.Size = new System.Drawing.Size(286, 14);
            this.progressBarInspection.Step = 1;
            this.progressBarInspection.TabIndex = 19;
            this.progressBarInspection.Value = 50;
            // 
            // checkBoxUsage
            // 
            this.checkBoxUsage.AutoSize = true;
            this.checkBoxUsage.Checked = true;
            this.checkBoxUsage.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxUsage.Location = new System.Drawing.Point(18, 127);
            this.checkBoxUsage.Name = "checkBoxUsage";
            this.checkBoxUsage.Size = new System.Drawing.Size(115, 17);
            this.checkBoxUsage.TabIndex = 18;
            this.checkBoxUsage.Text = "Nutzungsgesteuert";
            this.checkBoxUsage.UseVisualStyleBackColor = true;
            this.checkBoxUsage.CheckedChanged += new System.EventHandler(this.checkBoxUsage_CheckedChanged);
            // 
            // timer
            // 
            this.timer.Enabled = true;
            this.timer.Tick += new System.EventHandler(this.timer_Tick);
            // 
            // treeListViewTokens
            // 
            this.treeListViewTokens.AllColumns.Add(this.olvColumnName);
            this.treeListViewTokens.AllColumns.Add(this.olvColumnType);
            this.treeListViewTokens.AllColumns.Add(this.olvColumnDuration);
            this.treeListViewTokens.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.treeListViewTokens.CellEditUseWholeCell = false;
            this.treeListViewTokens.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.olvColumnName,
            this.olvColumnType});
            this.treeListViewTokens.Cursor = System.Windows.Forms.Cursors.Default;
            this.treeListViewTokens.FullRowSelect = true;
            this.treeListViewTokens.HideSelection = false;
            this.treeListViewTokens.Location = new System.Drawing.Point(6, 19);
            this.treeListViewTokens.Name = "treeListViewTokens";
            this.treeListViewTokens.ShowFilterMenuOnRightClick = false;
            this.treeListViewTokens.ShowGroups = false;
            this.treeListViewTokens.Size = new System.Drawing.Size(286, 269);
            this.treeListViewTokens.TabIndex = 20;
            this.treeListViewTokens.UseCompatibleStateImageBehavior = false;
            this.treeListViewTokens.View = System.Windows.Forms.View.Details;
            this.treeListViewTokens.VirtualMode = true;
            // 
            // olvColumnName
            // 
            this.olvColumnName.Text = "Name";
            this.olvColumnName.Width = 280;
            // 
            // olvColumnType
            // 
            this.olvColumnType.Text = "Typ";
            this.olvColumnType.Width = 280;
            // 
            // olvColumnDuration
            // 
            this.olvColumnDuration.DisplayIndex = 2;
            this.olvColumnDuration.IsVisible = false;
            this.olvColumnDuration.Text = "Dauer";
            this.olvColumnDuration.Width = 120;
            // 
            // SleeplessConfigurationWindow
            // 
            this.AcceptButton = this.buttonOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(316, 477);
            this.Controls.Add(this.checkBoxUsage);
            this.Controls.Add(this.groupBoxUsage);
            this.Controls.Add(this.checkBoxTime);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.checkBoxPermanent);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.buttonOK);
            this.HelpButton = true;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(260, 284);
            this.Name = "SleeplessConfigurationWindow";
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Schlaflos konfigurieren";
            this.TopMost = true;
            this.groupBox1.ResumeLayout(false);
            this.groupBoxUsage.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.treeListViewTokens)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.CheckBox checkBoxTime;
        private System.Windows.Forms.CheckBox checkBoxPermanent;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label labelDescription;
        private System.Windows.Forms.DateTimePicker dateTimePicker;
        private System.Windows.Forms.GroupBox groupBoxUsage;
        private System.Windows.Forms.CheckBox checkBoxUsage;
        private System.Windows.Forms.ProgressBar progressBarInspection;
        private BrightIdeasSoftware.TreeListView treeListViewTokens;
        private BrightIdeasSoftware.OLVColumn olvColumnName;
        private BrightIdeasSoftware.OLVColumn olvColumnType;
        private System.Windows.Forms.Timer timer;
        private System.Windows.Forms.ToolTip toolTipProgress;
        private BrightIdeasSoftware.OLVColumn olvColumnDuration;
    }
}