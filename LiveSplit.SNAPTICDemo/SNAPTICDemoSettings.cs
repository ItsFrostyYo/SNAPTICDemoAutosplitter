using System.Drawing;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.SNAPTICDemo
{
    public sealed class SNAPTICDemoSettings : UserControl
    {
        private readonly CheckBox startCheckBox;
        private readonly CheckBox splitCheckBox;
        private readonly CheckBox endSplitCheckBox;
        private readonly CheckBox resetCheckBox;
        private readonly ToolTip toolTip;

        public bool Start => startCheckBox.Checked;
        public bool Split => splitCheckBox.Checked;
        public bool EndSplit => endSplitCheckBox.Checked;
        public bool Reset => resetCheckBox.Checked;

        public SNAPTICDemoSettings()
        {
            toolTip = new ToolTip
            {
                AutoPopDelay = 10000,
                InitialDelay = 400,
                ReshowDelay = 100
            };

            startCheckBox = CreateCheckBox("Start", 10);
            splitCheckBox = CreateCheckBox("Split", 33);
            endSplitCheckBox = CreateCheckBox("End Split", 56);
            endSplitCheckBox.Location = new Point(30, 56);
            resetCheckBox = CreateCheckBox("Reset", 79);
            splitCheckBox.CheckedChanged += (sender, args) =>
            {
                UpdateSplitOptions();
                SNAPTICDemoLog.Write("Setting changed: Split=" + splitCheckBox.Checked + ".");
            };
            startCheckBox.CheckedChanged += (sender, args) =>
                SNAPTICDemoLog.Write("Setting changed: Start=" + startCheckBox.Checked + ".");
            endSplitCheckBox.CheckedChanged += (sender, args) =>
                SNAPTICDemoLog.Write("Setting changed: EndSplit=" + endSplitCheckBox.Checked + ".");
            resetCheckBox.CheckedChanged += (sender, args) =>
                SNAPTICDemoLog.Write("Setting changed: Reset=" + resetCheckBox.Checked + ".");

            toolTip.SetToolTip(
                startCheckBox,
                "Start after the second opening cutscene finishes or is skipped and control is gained.");
            toolTip.SetToolTip(
                splitCheckBox,
                "Enable or disable all automatic end splits.");
            toolTip.SetToolTip(
                endSplitCheckBox,
                "Split when the player enters either the UpperPlatform bounds or the SkyPot bounds.");
            toolTip.SetToolTip(
                resetCheckBox,
                "Reset when SNAPTIC returns to the main menu containing the Play button.");

            AutoScaleDimensions = new SizeF(6f, 13f);
            AutoScaleMode = AutoScaleMode.Font;
            Size = new Size(300, 105);
            Controls.Add(startCheckBox);
            Controls.Add(splitCheckBox);
            Controls.Add(endSplitCheckBox);
            Controls.Add(resetCheckBox);
            UpdateSplitOptions();
        }

        private static CheckBox CreateCheckBox(string text, int top)
        {
            return new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Location = new Point(10, top),
                Text = text,
                UseVisualStyleBackColor = true
            };
        }

        public XmlNode GetSettings(XmlDocument document)
        {
            XmlElement settings = document.CreateElement("Settings");
            AppendBoolean(document, settings, "Start", Start);
            AppendBoolean(document, settings, "Split", Split);
            AppendBoolean(document, settings, "EndSplit", EndSplit);
            AppendBoolean(document, settings, "Reset", Reset);
            return settings;
        }

        public void SetSettings(XmlNode settings)
        {
            startCheckBox.Checked = ReadBoolean(settings, "Start", true);
            splitCheckBox.Checked = ReadBoolean(settings, "Split", true);
            XmlNode endSplit = settings?.SelectSingleNode("EndSplit");
            if (endSplit != null)
                endSplitCheckBox.Checked = ReadBoolean(endSplit, true);
            else
                endSplitCheckBox.Checked =
                    ReadBoolean(settings, "GlitchlessEndSplit", true) ||
                    ReadBoolean(settings, "GlitchedEndSplit", false);

            XmlNode reset = settings?.SelectSingleNode("Reset");
            if (reset == null)
                reset = settings?.SelectSingleNode("ResetOnMainMenu");
            resetCheckBox.Checked = ReadBoolean(reset, true);
            UpdateSplitOptions();
        }

        private void UpdateSplitOptions()
        {
            endSplitCheckBox.Enabled = Split;
        }

        private static void AppendBoolean(
            XmlDocument document,
            XmlElement parent,
            string name,
            bool value)
        {
            XmlElement setting = document.CreateElement(name);
            setting.InnerText = value.ToString();
            parent.AppendChild(setting);
        }

        private static bool ReadBoolean(XmlNode settings, string name, bool defaultValue)
        {
            return ReadBoolean(settings?.SelectSingleNode(name), defaultValue);
        }

        private static bool ReadBoolean(XmlNode setting, bool defaultValue)
        {
            bool value;
            return setting != null && bool.TryParse(setting.InnerText, out value)
                ? value
                : defaultValue;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                toolTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
