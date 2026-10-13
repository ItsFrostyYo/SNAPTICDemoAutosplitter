using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;
using System.Reflection;

namespace LiveSplit.SNAPTICDemo
{
    internal sealed class SNAPTICDemoFactory : IComponentFactory
    {
        public string ComponentName => "SNAPTIC Demo Autosplitter";
        public string Description => "Starts after the second opening cutscene and optionally resets on the main menu.";
        public ComponentCategory Category => ComponentCategory.Control;
        public string UpdateName => ComponentName;
        public string UpdateURL => "https://raw.githubusercontent.com/ItsFrostyYo/SNAPTICDemoAutosplitter/main/";
        public string XMLURL => UpdateURL + "Components/SNAPTICDemo.Updates.xml";
        public Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        public IComponent Create(LiveSplitState state) => new SNAPTICDemoComponent(state);
    }
}
