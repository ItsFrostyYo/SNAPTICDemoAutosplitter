using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;
using System.Reflection;

namespace LiveSplit.SNAPTICDemo
{
    internal sealed class SNAPTICDemoFactory : IComponentFactory
    {
        public string ComponentName => "SNAPTIC Demo Autosplitter";
        public string Description => "A LiveSplit autosplitter component for SNAPTIC Demo that reads the games Godot state directly from memory and provides Automatic Start, Reset and Splits.";
        public ComponentCategory Category => ComponentCategory.Control;
        public string UpdateName => ComponentName;
        public string UpdateURL => "https://raw.githubusercontent.com/ItsFrostyYo/SNAPTICDemoAutosplitter/main/";
        public string XMLURL => UpdateURL + "Components/SNAPTICDemo.Updates.xml";
        public Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        public IComponent Create(LiveSplitState state) => new SNAPTICDemoComponent(state);
    }
}
