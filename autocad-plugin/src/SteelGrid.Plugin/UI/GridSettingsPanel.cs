using System;
using System.Drawing;
using System.Windows.Forms;
using SteelGrid.Core.Model;

namespace SteelGrid.Plugin.UI
{
    /// <summary>排条参数面板。使用固定行布局，确保标签和输入框同行显示。</summary>
    public sealed class GridSettingsPanel : UserControl
    {
        private readonly NumericUpDown _shrink = CreateNumber(0.0, 200.0, 5.0);
        private readonly ComboBox _loadDirection = CreateLoadDirectionCombo();
        private readonly NumericUpDown _frame = CreateNumber(1.0, 200.0, 5.0);
        private readonly ComboBox _horizontalType = CreateBarTypeCombo();
        private readonly NumericUpDown _horizontalThickness = CreateNumber(0.1, 100.0, 5.0);
        private readonly NumericUpDown _horizontalPitch = CreateNumber(1.0, 500.0, 36.85);
        private readonly ComboBox _verticalType = CreateBarTypeCombo();
        private readonly NumericUpDown _verticalThickness = CreateNumber(0.1, 100.0, 5.0);
        private readonly NumericUpDown _verticalPitch = CreateNumber(1.0, 500.0, 36.85);
        private readonly Button _saveButton = new Button
        {
            Text = "保存参数",
            Size = new Size(170, 34)
        };

        public event EventHandler<GridSettingsData> SaveClicked;

        public GridSettingsPanel(GridSettingsData settings)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScroll = true;
            Width = 520;
            Height = 760;

            LoadSettings(settings);

            AddRow("缩尺(mm):", _shrink);
            AddRow("受力方向:", _loadDirection);
            AddRow("边框厚度(mm):", _frame);
            AddRow("横向材料:", _horizontalType);
            AddRow("横向厚度(mm):", _horizontalThickness);
            AddRow("横向材料中心距(mm):", _horizontalPitch);
            AddRow("纵向材料:", _verticalType);
            AddRow("纵向材料厚度(mm):", _verticalThickness);
            AddRow("纵向材料中心距(mm):", _verticalPitch);

            _saveButton.Location = new Point(180, 18 + 9 * 52 + 14);
            _saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _saveButton.Click += (sender, e) =>
            {
                var data = GetSettings();
                SaveClicked?.Invoke(this, data);
            };
            Controls.Add(_saveButton);
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
                VerticalPitch = (double)_verticalPitch.Value
            };
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

        private void AddRow(string labelText, Control input)
        {
            var rowIndex = Controls.Count / 2;
            var y = 18 + rowIndex * 52;

            var label = new Label
            {
                Text = labelText,
                Location = new Point(18, y + 5),
                Size = new Size(205, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            input.Location = new Point(232, y);
            input.Size = new Size(245, 28);
            input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Controls.Add(label);
            Controls.Add(input);
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
