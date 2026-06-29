namespace Publisher;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;
    private TableLayoutPanel _rootLayout = null!;
    private TableLayoutPanel _selectorsLayout = null!;
    private TableLayoutPanel _imageLayout = null!;
    private GroupBox _postsGroup = null!;
    private GroupBox _previewGroup = null!;
    private GroupBox _postTextGroup = null!;
    private FlowLayoutPanel _actionsPanel = null!;
    private Label _botLabel = null!;
    private Label _channelLabel = null!;
    private Label _imageLabel = null!;
    private ListBox _postsList = null!;
    private TextBox _previewBox = null!;
    private ComboBox _botCombo = null!;
    private ComboBox _channelCombo = null!;
    private CheckBox _useProxyCheckBox = null!;
    private TextBox _postTextBox = null!;
    private TextBox _imagePathBox = null!;
    private Button _selectImageButton = null!;
    private Button _clearImageButton = null!;
    private Button _clearButton = null!;
    private Button _sendButton = null!;
    private Label _statusLabel = null!;
    private OpenFileDialog _imageDialog = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _rootLayout = new TableLayoutPanel();
        _selectorsLayout = new TableLayoutPanel();
        _imageLayout = new TableLayoutPanel();
        _botLabel = new Label();
        _botCombo = new ComboBox();
        _channelLabel = new Label();
        _channelCombo = new ComboBox();
        _useProxyCheckBox = new CheckBox();
        _imageLabel = new Label();
        _imagePathBox = new TextBox();
        _selectImageButton = new Button();
        _clearImageButton = new Button();
        _postsGroup = new GroupBox();
        _postsList = new ListBox();
        _previewGroup = new GroupBox();
        _previewBox = new TextBox();
        _postTextGroup = new GroupBox();
        _postTextBox = new TextBox();
        _actionsPanel = new FlowLayoutPanel();
        _sendButton = new Button();
        _clearButton = new Button();
        _statusLabel = new Label();
        _imageDialog = new OpenFileDialog();
        _rootLayout.SuspendLayout();
        _selectorsLayout.SuspendLayout();
        _imageLayout.SuspendLayout();
        _postsGroup.SuspendLayout();
        _previewGroup.SuspendLayout();
        _postTextGroup.SuspendLayout();
        _actionsPanel.SuspendLayout();
        SuspendLayout();
        // 
        // _rootLayout
        // 
        _rootLayout.ColumnCount = 2;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
        _rootLayout.Controls.Add(_selectorsLayout, 0, 0);
        _rootLayout.Controls.Add(_imageLayout, 0, 1);
        _rootLayout.Controls.Add(_postsGroup, 0, 2);
        _rootLayout.Controls.Add(_previewGroup, 1, 2);
        _rootLayout.Controls.Add(_postTextGroup, 1, 3);
        _rootLayout.Controls.Add(_actionsPanel, 0, 4);
        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.Location = new Point(12, 12);
        _rootLayout.Name = "_rootLayout";
        _rootLayout.RightToLeft = RightToLeft.Yes;
        _rootLayout.RowCount = 5;
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.Size = new Size(1010, 619);
        _rootLayout.TabIndex = 0;
        // 
        // _selectorsLayout
        // 
        _selectorsLayout.AutoSize = true;
        _selectorsLayout.ColumnCount = 5;
        _rootLayout.SetColumnSpan(_selectorsLayout, 2);
        _selectorsLayout.ColumnStyles.Add(new ColumnStyle());
        _selectorsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _selectorsLayout.ColumnStyles.Add(new ColumnStyle());
        _selectorsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _selectorsLayout.ColumnStyles.Add(new ColumnStyle());
        _selectorsLayout.Controls.Add(_botLabel, 0, 0);
        _selectorsLayout.Controls.Add(_botCombo, 1, 0);
        _selectorsLayout.Controls.Add(_channelLabel, 2, 0);
        _selectorsLayout.Controls.Add(_channelCombo, 3, 0);
        _selectorsLayout.Controls.Add(_useProxyCheckBox, 4, 0);
        _selectorsLayout.Dock = DockStyle.Fill;
        _selectorsLayout.Location = new Point(3, 3);
        _selectorsLayout.Name = "_selectorsLayout";
        _selectorsLayout.Padding = new Padding(0, 0, 0, 8);
        _selectorsLayout.RowCount = 1;
        _selectorsLayout.RowStyles.Add(new RowStyle());
        _selectorsLayout.Size = new Size(1004, 37);
        _selectorsLayout.TabIndex = 0;
        // 
        // _imageLayout
        // 
        _imageLayout.AutoSize = true;
        _imageLayout.ColumnCount = 4;
        _rootLayout.SetColumnSpan(_imageLayout, 2);
        _imageLayout.ColumnStyles.Add(new ColumnStyle());
        _imageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _imageLayout.ColumnStyles.Add(new ColumnStyle());
        _imageLayout.ColumnStyles.Add(new ColumnStyle());
        _imageLayout.Controls.Add(_imageLabel, 0, 0);
        _imageLayout.Controls.Add(_imagePathBox, 1, 0);
        _imageLayout.Controls.Add(_selectImageButton, 2, 0);
        _imageLayout.Controls.Add(_clearImageButton, 3, 0);
        _imageLayout.Dock = DockStyle.Fill;
        _imageLayout.Location = new Point(3, 46);
        _imageLayout.Name = "_imageLayout";
        _imageLayout.Padding = new Padding(0, 0, 0, 8);
        _imageLayout.RowCount = 1;
        _imageLayout.RowStyles.Add(new RowStyle());
        _imageLayout.Size = new Size(1004, 45);
        _imageLayout.TabIndex = 1;
        // 
        // _imageLabel
        // 
        _imageLabel.Anchor = AnchorStyles.Right;
        _imageLabel.AutoSize = true;
        _imageLabel.Location = new Point(952, 8);
        _imageLabel.Name = "_imageLabel";
        _imageLabel.Size = new Size(49, 19);
        _imageLabel.TabIndex = 0;
        _imageLabel.Text = "عکس:";
        // 
        // _imagePathBox
        // 
        _imagePathBox.Dock = DockStyle.Fill;
        _imagePathBox.Location = new Point(199, 3);
        _imagePathBox.Name = "_imagePathBox";
        _imagePathBox.ReadOnly = true;
        _imagePathBox.Size = new Size(747, 25);
        _imagePathBox.TabIndex = 1;
        // 
        // _selectImageButton
        // 
        _selectImageButton.AutoSize = true;
        _selectImageButton.Location = new Point(92, 3);
        _selectImageButton.Name = "_selectImageButton";
        _selectImageButton.Size = new Size(101, 29);
        _selectImageButton.TabIndex = 2;
        _selectImageButton.Text = "انتخاب عکس";
        _selectImageButton.UseVisualStyleBackColor = true;
        _selectImageButton.Click += SelectImageButton_Click;
        // 
        // _clearImageButton
        // 
        _clearImageButton.AutoSize = true;
        _clearImageButton.Location = new Point(3, 3);
        _clearImageButton.Name = "_clearImageButton";
        _clearImageButton.Size = new Size(83, 29);
        _clearImageButton.TabIndex = 3;
        _clearImageButton.Text = "حذف عکس";
        _clearImageButton.UseVisualStyleBackColor = true;
        _clearImageButton.Click += ClearImageButton_Click;
        // 
        // _botLabel
        // 
        _botLabel.Anchor = AnchorStyles.Right;
        _botLabel.AutoSize = true;
        _botLabel.Location = new Point(967, 5);
        _botLabel.Name = "_botLabel";
        _botLabel.Size = new Size(34, 19);
        _botLabel.TabIndex = 0;
        _botLabel.Text = "بات:";
        // 
        // _botCombo
        // 
        _botCombo.DisplayMember = "Name";
        _botCombo.Dock = DockStyle.Fill;
        _botCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _botCombo.FormattingEnabled = true;
        _botCombo.Location = new Point(509, 3);
        _botCombo.Name = "_botCombo";
        _botCombo.Size = new Size(452, 25);
        _botCombo.TabIndex = 1;
        _botCombo.ValueMember = "BotId";
        // 
        // _channelLabel
        // 
        _channelLabel.Anchor = AnchorStyles.Right;
        _channelLabel.AutoSize = true;
        _channelLabel.Location = new Point(462, 5);
        _channelLabel.Name = "_channelLabel";
        _channelLabel.Size = new Size(41, 19);
        _channelLabel.TabIndex = 2;
        _channelLabel.Text = "کانال:";
        // 
        // _channelCombo
        // 
        _channelCombo.DisplayMember = "Title";
        _channelCombo.Dock = DockStyle.Fill;
        _channelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _channelCombo.FormattingEnabled = true;
        _channelCombo.Location = new Point(3, 3);
        _channelCombo.Name = "_channelCombo";
        _channelCombo.Size = new Size(453, 25);
        _channelCombo.TabIndex = 3;
        _channelCombo.ValueMember = "ChatId";
        //
        // _useProxyCheckBox
        //
        _useProxyCheckBox.Anchor = AnchorStyles.Right;
        _useProxyCheckBox.AutoSize = true;
        _useProxyCheckBox.Location = new Point(3, 4);
        _useProxyCheckBox.Name = "_useProxyCheckBox";
        _useProxyCheckBox.Size = new Size(113, 23);
        _useProxyCheckBox.TabIndex = 4;
        _useProxyCheckBox.Text = "استفاده از پروکسی";
        _useProxyCheckBox.UseVisualStyleBackColor = true;
        // 
        // _postsGroup
        // 
        _postsGroup.Controls.Add(_postsList);
        _postsGroup.Dock = DockStyle.Fill;
        _postsGroup.Location = new Point(690, 46);
        _postsGroup.Name = "_postsGroup";
        _postsGroup.Padding = new Padding(10);
        _rootLayout.SetRowSpan(_postsGroup, 2);
        _postsGroup.Size = new Size(317, 518);
        _postsGroup.TabIndex = 1;
        _postsGroup.TabStop = false;
        _postsGroup.Text = "فایل‌های آماده ارسال";
        // 
        // _postsList
        // 
        _postsList.DisplayMember = "DisplayName";
        _postsList.Dock = DockStyle.Fill;
        _postsList.FormattingEnabled = true;
        _postsList.Location = new Point(10, 28);
        _postsList.Name = "_postsList";
        _postsList.Size = new Size(297, 480);
        _postsList.TabIndex = 0;
        _postsList.SelectedIndexChanged += PostsList_SelectedIndexChanged;
        // 
        // _previewGroup
        // 
        _previewGroup.Controls.Add(_previewBox);
        _previewGroup.Dock = DockStyle.Fill;
        _previewGroup.Location = new Point(3, 46);
        _previewGroup.Name = "_previewGroup";
        _previewGroup.Padding = new Padding(10);
        _previewGroup.Size = new Size(681, 230);
        _previewGroup.TabIndex = 2;
        _previewGroup.TabStop = false;
        _previewGroup.Text = "محتوای فایل";
        // 
        // _previewBox
        // 
        _previewBox.Dock = DockStyle.Fill;
        _previewBox.Location = new Point(10, 28);
        _previewBox.Multiline = true;
        _previewBox.Name = "_previewBox";
        _previewBox.ReadOnly = true;
        _previewBox.ScrollBars = ScrollBars.Vertical;
        _previewBox.Size = new Size(661, 192);
        _previewBox.TabIndex = 0;
        // 
        // _postTextGroup
        // 
        _postTextGroup.Controls.Add(_postTextBox);
        _postTextGroup.Dock = DockStyle.Fill;
        _postTextGroup.Location = new Point(3, 282);
        _postTextGroup.Name = "_postTextGroup";
        _postTextGroup.Padding = new Padding(10);
        _postTextGroup.Size = new Size(681, 282);
        _postTextGroup.TabIndex = 3;
        _postTextGroup.TabStop = false;
        _postTextGroup.Text = "متن پست";
        // 
        // _postTextBox
        // 
        _postTextBox.Dock = DockStyle.Fill;
        _postTextBox.Location = new Point(10, 28);
        _postTextBox.Multiline = true;
        _postTextBox.Name = "_postTextBox";
        _postTextBox.ScrollBars = ScrollBars.Vertical;
        _postTextBox.Size = new Size(661, 244);
        _postTextBox.TabIndex = 0;
        // 
        // _actionsPanel
        // 
        _actionsPanel.AutoSize = true;
        _rootLayout.SetColumnSpan(_actionsPanel, 2);
        _actionsPanel.Controls.Add(_sendButton);
        _actionsPanel.Controls.Add(_clearButton);
        _actionsPanel.Controls.Add(_statusLabel);
        _actionsPanel.Dock = DockStyle.Fill;
        _actionsPanel.FlowDirection = FlowDirection.RightToLeft;
        _actionsPanel.Location = new Point(3, 570);
        _actionsPanel.Name = "_actionsPanel";
        _actionsPanel.Padding = new Padding(0, 10, 0, 0);
        _actionsPanel.Size = new Size(1004, 46);
        _actionsPanel.TabIndex = 4;
        // 
        // _sendButton
        // 
        _sendButton.AutoSize = true;
        _sendButton.Location = new Point(3, 13);
        _sendButton.Name = "_sendButton";
        _sendButton.Size = new Size(87, 29);
        _sendButton.TabIndex = 0;
        _sendButton.Text = "ارسال پست";
        _sendButton.UseVisualStyleBackColor = true;
        _sendButton.Click += SendButton_Click;
        // 
        // _clearButton
        // 
        _clearButton.AutoSize = true;
        _clearButton.Location = new Point(96, 13);
        _clearButton.Name = "_clearButton";
        _clearButton.Size = new Size(106, 29);
        _clearButton.TabIndex = 1;
        _clearButton.Text = "خالی کردن متن";
        _clearButton.UseVisualStyleBackColor = true;
        _clearButton.Click += ClearButton_Click;
        // 
        // _statusLabel
        // 
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(208, 10);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Padding = new Padding(12, 7, 12, 0);
        _statusLabel.Size = new Size(24, 26);
        _statusLabel.TabIndex = 2;
        // 
        // _imageDialog
        // 
        _imageDialog.Filter = "فایل‌های عکس|*.jpg;*.jpeg;*.png;*.gif;*.webp|همه فایل‌ها|*.*";
        _imageDialog.Title = "انتخاب عکس پست";
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1034, 643);
        Controls.Add(_rootLayout);
        Font = new Font("Segoe UI", 10F);
        MinimumSize = new Size(1050, 680);
        Name = "MainForm";
        Padding = new Padding(12);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ارسال کننده پست تلگرام";
        _rootLayout.ResumeLayout(false);
        _rootLayout.PerformLayout();
        _selectorsLayout.ResumeLayout(false);
        _selectorsLayout.PerformLayout();
        _imageLayout.ResumeLayout(false);
        _imageLayout.PerformLayout();
        _postsGroup.ResumeLayout(false);
        _previewGroup.ResumeLayout(false);
        _previewGroup.PerformLayout();
        _postTextGroup.ResumeLayout(false);
        _postTextGroup.PerformLayout();
        _actionsPanel.ResumeLayout(false);
        _actionsPanel.PerformLayout();
        ResumeLayout(false);
    }
}
