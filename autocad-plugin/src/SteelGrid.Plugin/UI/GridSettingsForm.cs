using System;
using System.Windows.Forms;

namespace SteelGrid.Plugin.UI
{
    /// <summary>独立的钢格板排条参数窗口。</summary>
    public sealed class GridSettingsForm : Form
    {
        private readonly GridSettingsPanel _settingsPanel;

        public GridSettingsForm(GridSettingsData settings, Action<GridSettingsData> saveCallback)
        {
            Text = "钢格板排条参数";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = true;
            ClientSize = new System.Drawing.Size(520, 800);
            MinimumSize = new System.Drawing.Size(500, 780);
            AutoScaleMode = AutoScaleMode.None;

            _settingsPanel = new GridSettingsPanel(settings);
            _settingsPanel.Dock = DockStyle.Fill;
            _settingsPanel.SaveClicked += (sender, data) =>
            {
                saveCallback(data);
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_settingsPanel);
        }
    }
}
