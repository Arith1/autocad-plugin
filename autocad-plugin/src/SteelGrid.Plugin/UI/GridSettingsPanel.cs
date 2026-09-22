using System;
using System.Drawing;
using System.Windows.Forms;
using SteelGrid.Core.Model;

namespace SteelGrid.Plugin.UI
{
    /// <summary>排条参数面板，包含“排条参数”和“输出选项”两个标签页。</summary>
    public sealed class GridSettingsPanel : UserControl
    {
        private readonly TabControl _tabs = new TabControl();
        private readonly TabPage _paramTab = new TabPage("排条参数");
        private readonly TabPage _outputTab = new TabPage("输出选项");
        private readonly Panel _paramHost = new Panel { AutoScroll = true };
        private readonly Panel _outputHost = new Panel { AutoScroll = true };
        private readonly TableLayoutPanel _paramLayout = new TableLayoutPanel();

        private readonly NumericUpDown _shrink = CreateNumber(0.0, 200.0, 5.0);
        private readonly ComboBox _loadDirection = CreateLoadDirectionCombo();
        private readonly NumericUpDown _frame = CreateNumber(1.0, 200.0, 5.0);
        private readonly ComboBox _horizontalType = CreateBarTypeCombo();
        private readonly NumericUpDown _horizontalThickness = CreateNumber(0.1, 100.0, 5.0);
        private readonly NumericUpDown _horizontalPitch = CreateNumber(1.0, 500.0, 36.85);
        private readonly ComboBox _verticalType = CreateBarTypeCombo();
        private readonly NumericUpDown _verticalThickness = CreateNumber(0.1, 100.0, 5.0);
        private readonly NumericUpDown _verticalPitch = CreateNumber(1.0, 500.0, 36.85);

        private readonly RadioButton _horizontalOutput = new RadioButton
        {
            Text = "横向输出",
            AutoSize = true
        };
        private readonly RadioButton _verticalOutput = new RadioButton
        {
            Text = "纵向输出",
            AutoSize = true
        };
        private readonly RadioButton _topDownFirst = new RadioButton
        {
            Text = "先自上至下后自左至右",
            AutoSize = true
        };
        private readonly RadioButton _leftRightFirst = new RadioButton
        {
            Text = "先自左至右后自上至下",
            AutoSize = true
        };
        private readonly NumericUpDown _perRowColumns = CreateInteger(1, 100, 10);
        private readonly NumericUpDown _perColumnRows = CreateInteger(1, 100, 10);

        private readonly Button _saveButton = new Button
        {
            Text = "保存参数",
            Height = 38
        };

        public event EventHandler<GridSettingsData> SaveClicked;

        public GridSettingsPanel(GridSettingsData settings)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 560;
            Height = 800;

            _paramLayout.Dock = DockStyle.Top;
            _paramLayout.Height = 16 + 9 * 30 + 10;
            _paramLayout.ColumnCount = 2;
            _paramLayout.RowCount = 9;
            _paramLayout.Padding = new Padding(14, 16, 14, 10);
            _paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            _paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 9; i++)
            {
                _paramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            }

            _paramHost.Dock = DockStyle.Fill;
            _outputHost.Dock = DockStyle.Fill;
            _paramHost.Controls.Add(_paramLayout);

            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(_paramTab);
            _tabs.TabPages.Add(_outputTab);
            _paramTab.Controls.Add(_paramHost);
            _outputTab.Controls.Add(_outputHost);

            _saveButton.Dock = DockStyle.Bottom;
            _saveButton.Click += (sender, e) =>
            {
                SaveClicked?.Invoke(this, GetSettings());
            };

            Controls.Add(_tabs);
            Controls.Add(_saveButton);

            LoadSettings(settings);

            AddRow(_paramLayout, 0, "缩尺(mm):", _shrink);
            AddRow(_paramLayout, 1, "受力方向:", _loadDirection);
            AddRow(_paramLayout, 2, "边框厚度(mm):", _frame);
            AddRow(_paramLayout, 3, "横向材料:", _horizontalType);
            AddRow(_paramLayout, 4, "横向厚度(mm):", _horizontalThickness);
            AddRow(_paramLayout, 5, "横向材料中心距(mm):", _horizontalPitch);
            AddRow(_paramLayout, 6, "纵向材料:", _verticalType);
            AddRow(_paramLayout, 7, "纵向材料厚度(mm):", _verticalThickness);
            AddRow(_paramLayout, 8, "纵向材料中心距(mm):", _verticalPitch);

            var directionGroup = new GroupBox
            {
                Text = "输出方向",
                Location = new Point(18, 18),
                Size = new Size(500, 96)
            };
            _horizontalOutput.Location = new Point(24, 30);
            _verticalOutput.Location = new Point(24, 58);
            directionGroup.Controls.Add(_horizontalOutput);
            directionGroup.Controls.Add(_verticalOutput);
            _outputHost.Controls.Add(directionGroup);

            var orderGroup = new GroupBox
            {
                Text = "生成顺序",
                Location = new Point(18, 130),
                Size = new Size(500, 96)
            };
            _topDownFirst.Location = new Point(24, 30);
            _leftRightFirst.Location = new Point(24, 58);
            orderGroup.Controls.Add(_topDownFirst);
            orderGroup.Controls.Add(_leftRightFirst);
            _outputHost.Controls.Add(orderGroup);

            var countGroup = new GroupBox
            {
                Text = "每行/列数量",
                Location = new Point(18, 242),
                Size = new Size(500, 116)
            };
            var perRowLabel = new Label
            {
                Text = "横向输出每行个数:",
                Location = new Point(24, 28),
                AutoSize = true
            };
            _perRowColumns.Location = new Point(190, 24);
            _perRowColumns.Width = 90;
            var perColumnLabel = new Label
            {
                Text = "纵向输出每列个数:",
                Location = new Point(24, 68),
                AutoSize = true
            };
            _perColumnRows.Location = new Point(190, 64);
            _perColumnRows.Width = 90;
            countGroup.Controls.Add(perRowLabel);
            countGroup.Controls.Add(_perRowColumns);
            countGroup.Controls.Add(perColumnLabel);
            countGroup.Controls.Add(_perColumnRows);
            _outputHost.Controls.Add(countGroup);
        }

        private void LoadSettings(GridSettingsData settings)
        {
            _shrink.Value = ClampDecimal(settings.Shrink, 0.0, 200.0);
            _frame.Value = ClampDecimal(settings.FrameT, 1.0, 200.0);
            _loadDirection.SelectedIndex = settings.LoadDirection == LoadDirection.Horizontal ? 1 : 0;
            _horizontalType.SelectedIndex = settings.HorizontalType == BarType.TwistedSquare ? 1 : 0;
            _horizontalThickness.Value = ClampDecimal(settings.HorizontalThickness, 0.1, 100.0);
            _horizontalPitch.Value = ClampDecimal(settings.HorizontalPitch, 1.0, 500.0);
            _verticalType.SelectedIndex = settings.VerticalType == BarType.TwistedSquare ? 1 : 0;
            _verticalThickness.Value = ClampDecimal(settings.VerticalThickness, 0.1, 100.0);
            _verticalPitch.Value = ClampDecimal(settings.VerticalPitch, 1.0, 500.0);

            _horizontalOutput.Checked = settings.OutputFlow != OutputFlow.Vertical;
            _verticalOutput.Checked = settings.OutputFlow == OutputFlow.Vertical;
            _topDownFirst.Checked = settings.GenerationOrder != GenerationOrder.LeftRightFirst;
            _leftRightFirst.Checked = settings.GenerationOrder == GenerationOrder.LeftRightFirst;
            _perRowColumns.Value = ClampDecimal(settings.PerRowColumns, 1, 100);
            _perColumnRows.Value = ClampDecimal(settings.PerColumnRows, 1, 100);
        }

        public GridSettingsData GetSettings()
        {
            return new GridSettingsData
            {
                Shrink = (double)_shrink.Value,
                FrameT = (double)_frame.Value,
                LoadDirection = _loadDirection.SelectedIndex == 1
                    ? LoadDirection.Horizontal
                    : LoadDirection.Vertical,
                HorizontalType = _horizontalType.SelectedIndex == 1
                    ? BarType.TwistedSquare
                    : BarType.Flat,
                HorizontalThickness = (double)_horizontalThickness.Value,
                HorizontalPitch = (double)_horizontalPitch.Value,
                VerticalType = _verticalType.SelectedIndex == 1
                    ? BarType.TwistedSquare
                    : BarType.Flat,
                VerticalThickness = (double)_verticalThickness.Value,
                VerticalPitch = (double)_verticalPitch.Value,
                OutputFlow = _verticalOutput.Checked ? OutputFlow.Vertical : OutputFlow.Horizontal,
                GenerationOrder = _leftRightFirst.Checked
                    ? GenerationOrder.LeftRightFirst
                    : GenerationOrder.TopDownFirst,
                PerRowColumns = (int)_perRowColumns.Value,
                PerColumnRows = (int)_perColumnRows.Value
            };
        }

        private static void AddRow(TableLayoutPanel layout, int row, string labelText, Control input)
        {
            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            input.Height = 26;
            input.Margin = new Padding(0);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(input, 1, row);
        }

        private static decimal ClampDecimal(double value, double min, double max)
        {
            if (value < min)
            {
                return (decimal)min;
            }

            if (value > max)
            {
                return (decimal)max;
            }

            return (decimal)value;
        }

        private static NumericUpDown CreateNumber(double min, double max, double value)
        {
            return new NumericUpDown
            {
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                Value = (decimal)value,
                DecimalPlaces = 2
            };
        }

        private static NumericUpDown CreateInteger(double min, double max, double value)
        {
            return new NumericUpDown
            {
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                Value = (decimal)value,
                DecimalPlaces = 0
            };
        }

        private static ComboBox CreateBarTypeCombo()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.Add("扁钢");
            combo.Items.Add("扭绞方钢");
            combo.SelectedIndex = 0;
            return combo;
        }

        private static ComboBox CreateLoadDirectionCombo()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.Add("垂直");
            combo.Items.Add("水平");
            combo.SelectedIndex = 0;
            return combo;
        }
    }
}
